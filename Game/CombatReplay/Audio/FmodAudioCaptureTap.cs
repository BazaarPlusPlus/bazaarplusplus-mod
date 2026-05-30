#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Threading;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay.Audio;

/// <summary>
/// Captures the game's mixed audio by attaching a passthrough read-DSP to the
/// FMOD master <see cref="FMOD.ChannelGroup"/> at the TAIL index. The DSP read
/// callback runs on the FMOD mixer thread: it forwards the input buffer to the
/// output buffer unchanged (so the game keeps playing sound) and pushes a
/// duplicate of the interleaved float PCM into a lock-free
/// <see cref="AudioRingBuffer"/>. A background writer thread drains the ring into
/// a <see cref="WavStreamWriter"/>.
///
/// Everything degrades gracefully: any FMOD failure during <see cref="TryStart"/>
/// tears the tap down, logs a Warn under "CombatReplayAudio", and returns false so
/// the recorder proceeds with a silent video. The game audio signal is never
/// mutated or dropped, and the DSP is reliably removed + released on
/// <see cref="Stop"/>/<see cref="Dispose"/> so it never lingers on the master bus.
/// </summary>
internal sealed class FmodAudioCaptureTap : IDisposable
{
    private const string Component = "CombatReplayAudio";

    // Reusable drain buffer for the writer thread. Sized comfortably above a
    // typical FMOD mixer block so a single Read usually empties the ring.
    private const int WriterScratchFloats = 8192;

    // The read callback delegate is created once at type init and stored in a
    // static field so the GC/marshaler can never collect it for the lifetime of
    // any DSP that references it. A per-instance GCHandle in TryStart is
    // belt-and-suspenders on top of this.
    private static readonly FMOD.DSP_READ_CALLBACK s_readCallback = ReadCallbackStatic;
    private static readonly object s_activeLock = new();

    // The tap whose ring the mixer-thread callback feeds. Published under
    // s_activeLock; read as a plain volatile load in the callback. Set to null
    // FIRST during Stop so the very next callback no-ops the tap copy (it still
    // performs passthrough).
    private static volatile FmodAudioCaptureTap? Active;

    private readonly string _wavFilePath;
    private FMOD.ChannelGroup _masterGroup;
    private FMOD.DSP _dsp;
    private AudioRingBuffer? _ring;
    private WavStreamWriter? _wav;
    private Thread? _writerThread;
    private volatile bool _writerRun;
    private int _sampleRate;
    private int _channels;

    // The true interleave width as reported by the FMOD mixer's first read callback.
    // Volatile because the mixer thread publishes it and Stop() (main thread) reads it
    // to stamp the WAV header. 0 until the first callback fires.
    private volatile int _observedChannels;
    private GCHandle _callbackHandle;
    private bool _attached;
    private bool _stopped;

    public FmodAudioCaptureTap(string wavFilePath)
    {
        _wavFilePath = wavFilePath;
    }

    /// <summary>True once the DSP is attached and the writer thread is running.</summary>
    public bool IsCapturing { get; private set; }

    /// <summary>The WAV path this tap writes to (a temp file the muxer consumes).</summary>
    public string WavFilePath => _wavFilePath;

    /// <summary>
    /// True once at least one mixer-thread callback has pushed samples into the
    /// ring. The recorder can check this to decide whether a usable audio track
    /// exists before dispatching the mux pass.
    /// </summary>
    public bool CapturedAnySamples => (_ring?.TotalWritten ?? 0) > 0;

    /// <summary>
    /// MAIN thread. Resolves the FMOD core system, builds the passthrough DSP,
    /// attaches it at the master TAIL, and starts the WAV writer thread. Returns
    /// true iff the DSP is attached and capturing. Any failure tears the tap down
    /// (DSP removed/released if it got that far), logs a Warn, and returns false —
    /// the game audio and the silent video are left untouched.
    /// </summary>
    public bool TryStart()
    {
        try
        {
            // (1) Resolve the core system. The getter can throw
            // SystemNotInitializedException if FMOD is not up yet.
            var sys = FMODUnity.RuntimeManager.CoreSystem;

            // (2) Software format: sample rate + speaker mode.
            if (sys.getSoftwareFormat(out _sampleRate, out var mode, out _) != FMOD.RESULT.OK)
            {
                BppLog.Warn(Component, "Audio tap: getSoftwareFormat failed.");
                SafeStopInternal();
                return false;
            }

            // (3) Provisional channel count from the speaker mode. The first
            // callback may re-latch this from the actual inchannels.
            _channels = MapSpeakerModeToChannels(mode);

            // (4) Master channel group.
            if (sys.getMasterChannelGroup(out _masterGroup) != FMOD.RESULT.OK)
            {
                BppLog.Warn(Component, "Audio tap: getMasterChannelGroup failed.");
                SafeStopInternal();
                return false;
            }

            // (5) Build the passthrough read DSP. Only the read callback and the
            // buffer counts are set; every other callback/field is left default.
            var desc = new FMOD.DSP_DESCRIPTION
            {
                pluginsdkversion = FMOD.VERSION.number,
                name = MakeNameBytes("BPPAudioTap"),
                version = 1,
                numinputbuffers = 1,
                numoutputbuffers = 1,
                read = s_readCallback,
                numparameters = 0,
                paramdesc = IntPtr.Zero,
                userdata = IntPtr.Zero,
            };

            if (sys.createDSP(ref desc, out _dsp) != FMOD.RESULT.OK)
            {
                BppLog.Warn(Component, "Audio tap: createDSP failed.");
                SafeStopInternal();
                return false;
            }

            // (6) Attach at TAIL so we tap the fully-mixed master signal.
            if (_masterGroup.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.TAIL, _dsp) != FMOD.RESULT.OK)
            {
                BppLog.Warn(Component, "Audio tap: addDSP failed.");
                try
                {
                    _dsp.release();
                }
                catch
                {
                    // Best-effort; nothing else to clean up here.
                }
                SafeStopInternal();
                return false;
            }

            _attached = true;

            // (7) Allocate the ring (~1s of audio, rounded to a power of two,
            // min 65536 floats), open the WAV, pin the callback, publish the
            // active tap, and start draining.
            int cap = NextPow2(Math.Max(65536, _sampleRate * Math.Max(1, _channels)));
            _ring = new AudioRingBuffer(cap);
            _wav = new WavStreamWriter(_wavFilePath, _sampleRate, _channels);
            _callbackHandle = GCHandle.Alloc(s_readCallback);

            lock (s_activeLock)
            {
                Active = this;
            }

            _writerRun = true;
            _writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "BPP.CombatReplayAudio.WavWriter",
            };
            _writerThread.Start();

            IsCapturing = true;
            BppLog.Info(
                Component,
                $"Audio tap attached rate={_sampleRate} channels={_channels} ring={cap}"
            );
            return true;
        }
        catch (Exception ex)
        {
            BppLog.Warn(Component, $"Audio tap start failed: {ex.Message}");
            SafeStopInternal();
            return false;
        }
    }

    /// <summary>
    /// MAIN thread (or any). Idempotent teardown. Clears the active tap first so
    /// the next callback no-ops the copy, removes + releases the DSP, joins the
    /// writer thread so the ring fully drains, closes the WAV, and frees the
    /// pinned callback handle. Every sub-step is independently guarded so one
    /// failure never skips the rest.
    /// </summary>
    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;

        // Stop feeding the ring before touching the DSP graph.
        lock (s_activeLock)
        {
            if (Active == this)
                Active = null;
        }

        try
        {
            if (_attached)
                _masterGroup.removeDSP(_dsp);
        }
        catch (Exception ex)
        {
            BppLog.Warn(Component, $"Audio tap: removeDSP failed: {ex.Message}");
        }

        try
        {
            _dsp.release();
        }
        catch
        {
            // The DSP may never have been created; releasing a default handle is
            // a no-op at worst.
        }

        // Signal and join the writer so all buffered samples reach the WAV.
        _writerRun = false;
        try
        {
            _writerThread?.Join(3000);
        }
        catch
        {
            // A failed join must not block the rest of teardown.
        }

        try
        {
            // Stamp the WAV with the real interleave width the mixer reported (if any)
            // before the header is back-patched, so a non-stereo setup never yields a
            // wrong-channel / wrong-pitch track. removeDSP above guarantees no further
            // callback, so _observedChannels is stable here.
            var observed = _observedChannels;
            if (observed > 0)
                _wav?.SetChannelCount(observed);
            _wav?.Dispose();
        }
        catch (Exception ex)
        {
            BppLog.Warn(Component, $"Audio tap: WAV close failed: {ex.Message}");
        }

        try
        {
            if (_callbackHandle.IsAllocated)
                _callbackHandle.Free();
        }
        catch
        {
            // GCHandle.Free can only throw if already freed; ignore.
        }

        IsCapturing = false;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Teardown used on the failure paths of <see cref="TryStart"/>. Identical to
    /// <see cref="Stop"/> but does not require the tap to have fully started.
    /// </summary>
    private void SafeStopInternal()
    {
        try
        {
            Stop();
        }
        catch
        {
            // Stop is already fully guarded; this is only a final safety net so a
            // start failure can never escape as an exception.
        }
    }

    /// <summary>
    /// Background writer thread. Drains the ring into the WAV in a tight loop,
    /// sleeping briefly when empty, then performs a final drain after the stop
    /// signal so no tail samples are lost before the WAV is closed.
    /// </summary>
    private void WriterLoop()
    {
        var scratch = new float[WriterScratchFloats];
        var ring = _ring;
        var wav = _wav;
        if (ring == null || wav == null)
            return;

        try
        {
            while (_writerRun)
            {
                int read = ring.Read(scratch, 0, scratch.Length);
                if (read > 0)
                    wav.WriteSamples(scratch, 0, read);
                else
                    Thread.Sleep(2);
            }

            // Final drain: empty whatever the mixer thread published between the
            // last loop read and the stop signal.
            int tail;
            while ((tail = ring.Read(scratch, 0, scratch.Length)) > 0)
                wav.WriteSamples(scratch, 0, tail);
        }
        catch (Exception ex)
        {
            // Degrade to whatever WAV exists; never crash the writer thread.
            BppLog.Warn(Component, $"Audio writer loop error: {ex.Message}");
        }
    }

    /// <summary>
    /// FMOD mixer-thread callback. MUST be allocation-free, fast, and must never
    /// throw across the native boundary. Forwards the input buffer to the output
    /// buffer unchanged (mandatory passthrough) and pushes a duplicate of the
    /// interleaved float PCM into the active tap's ring.
    /// </summary>
    [AOT.MonoPInvokeCallback(typeof(FMOD.DSP_READ_CALLBACK))]
    private static FMOD.RESULT ReadCallbackStatic(
        ref FMOD.DSP_STATE state,
        IntPtr inbuffer,
        IntPtr outbuffer,
        uint length,
        int inchannels,
        ref int outchannels
    )
    {
        // Always echo the channel count first so the rest of the graph is correct
        // even if anything below is skipped.
        outchannels = inchannels;

        try
        {
            int total = (int)length * inchannels;

            // PASSTHROUGH (mandatory): copy the full interleaved block so the game
            // keeps playing. Guard against a null/aliased buffer.
            if (
                outbuffer != inbuffer
                && outbuffer != IntPtr.Zero
                && inbuffer != IntPtr.Zero
                && total > 0
            )
            {
                unsafe
                {
                    long bytes = (long)total * sizeof(float);
                    Buffer.MemoryCopy((void*)inbuffer, (void*)outbuffer, bytes, bytes);
                }
            }

            // Side-tap: duplicate the samples into our ring (producer = wait-free).
            var tap = Active;
            if (tap != null && inbuffer != IntPtr.Zero && total > 0)
            {
                // Latch the true interleave width the first time we learn it from the
                // mixer so Stop() can stamp the WAV header with the real channel count
                // (the provisional speaker-mode guess can disagree). Volatile publish so
                // the main thread observes it at teardown.
                if (tap._observedChannels != inchannels)
                    tap._observedChannels = inchannels;

                tap._ring?.Write(inbuffer, total);
            }
        }
        catch
        {
            // Swallow — never throw across the native boundary, never log here.
        }

        return FMOD.RESULT.OK;
    }

    /// <summary>
    /// Builds the 32-byte fixed DSP name buffer: up to 31 ASCII bytes of
    /// <paramref name="label"/> with a guaranteed NUL terminator.
    /// </summary>
    private static byte[] MakeNameBytes(string label)
    {
        var bytes = new byte[32];
        int n = Math.Min(31, label.Length);
        for (int i = 0; i < n; i++)
            bytes[i] = (byte)label[i];
        return bytes;
    }

    /// <summary>Maps an FMOD speaker mode to a provisional channel count.</summary>
    private static int MapSpeakerModeToChannels(FMOD.SPEAKERMODE mode) =>
        mode switch
        {
            FMOD.SPEAKERMODE.MONO => 1,
            FMOD.SPEAKERMODE.STEREO => 2,
            FMOD.SPEAKERMODE.QUAD => 4,
            FMOD.SPEAKERMODE.SURROUND => 5,
            FMOD.SPEAKERMODE._5POINT1 => 6,
            FMOD.SPEAKERMODE._7POINT1 => 8,
            FMOD.SPEAKERMODE._7POINT1POINT4 => 12,
            _ => 2,
        };

    /// <summary>Rounds <paramref name="value"/> up to the next power of two (min 2).</summary>
    private static int NextPow2(int value)
    {
        if (value < 2)
            return 2;
        if ((value & (value - 1)) == 0)
            return value;

        int result = 2;
        while (result < value)
        {
            int next = result << 1;
            if (next <= result)
                return result;
            result = next;
        }
        return result;
    }
}
