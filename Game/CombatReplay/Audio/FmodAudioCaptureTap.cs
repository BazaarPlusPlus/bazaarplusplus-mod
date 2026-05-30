#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.AppFramework;

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
    private const ulong FmodPortIndexNone = ulong.MaxValue;

    private static readonly string[] DiagnosticBusPathFields =
    {
        "MasterBusPath",
        "BoardDiegeticBusPath",
        "BoardPresentationBusPath",
        "CombatBusPath",
        "MonsterNonVerbalBusPath",
        "VOBusPath",
        "EnvironmentSpecificBusPath",
        "EnvironmentFocusBusPath",
    };

    private static readonly object s_routingDiagnosticsLock = new();
    private static bool s_routingDiagnosticsLogged;

    // The read callback delegate is created once at type init and stored in a
    // static field so the GC/marshaler can never collect it for the lifetime of
    // any DSP that references it. A per-instance GCHandle in TryStart is
    // belt-and-suspenders on top of this.
    private static readonly FMOD.DSP_READ_CALLBACK s_readCallback = ReadCallbackStatic;

    private readonly string _wavFilePath;
    private readonly string _studioBusPath;
    private readonly bool _allowCoreMasterFallback;

    // The channel group the passthrough DSP is attached to: preferably the configured FMOD
    // STUDIO bus channel group, else the CORE master channel group only when fallback is allowed.
    private FMOD.ChannelGroup _masterGroup;
    private FMOD.Studio.Bus _studioBus;
    private bool _busLocked;
    private string _capturePointLabel = "core-master-channel-group";
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
    private GCHandle _selfHandle;
    private double _sumSquares;
    private long _statSampleCount;
    private float _peakAbs;
    private volatile bool _captureEnabled;
    private bool _attached;
    private bool _stopped;

    public FmodAudioCaptureTap(string wavFilePath)
        : this(wavFilePath, "bus:/", allowCoreMasterFallback: true) { }

    public FmodAudioCaptureTap(
        string wavFilePath,
        string studioBusPath,
        bool allowCoreMasterFallback = false
    )
    {
        _wavFilePath = wavFilePath ?? throw new ArgumentNullException(nameof(wavFilePath));
        _studioBusPath = string.IsNullOrWhiteSpace(studioBusPath) ? "bus:/" : studioBusPath;
        _allowCoreMasterFallback = allowCoreMasterFallback;
    }

    /// <summary>True once the DSP is attached and the writer thread is running.</summary>
    public bool IsCapturing { get; private set; }

    /// <summary>The WAV path this tap writes to (a temp file the muxer consumes).</summary>
    public string WavFilePath => _wavFilePath;

    /// <summary>Human-readable source label used in runtime diagnostics.</summary>
    public string CapturePointLabel => _capturePointLabel;

    /// <summary>
    /// True once at least one mixer-thread callback has pushed samples into the
    /// ring. The recorder can check this to decide whether a usable audio track
    /// exists before dispatching the mux pass.
    /// </summary>
    public bool CapturedAnySamples => (_ring?.TotalWritten ?? 0) > 0;

    /// <summary>Total interleaved float samples pushed by the FMOD mixer callback.</summary>
    public long CapturedSampleFloats => _ring?.TotalWritten ?? 0;

    public double RmsAmplitude =>
        _statSampleCount > 0 ? Math.Sqrt(_sumSquares / _statSampleCount) : 0.0;

    public float PeakAmplitude => _peakAbs;

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

            LogStudioBusRoutingDiagnostics();

            // (4) Resolve the capture channel group. Base audio uses the Studio root
            // bus with a core-master fallback; targeted SFX taps must not fall back or
            // they would duplicate the base capture.
            if (!TryResolveStudioBusChannelGroup(_studioBusPath, out _masterGroup))
            {
                if (!_allowCoreMasterFallback)
                {
                    SafeStopInternal();
                    return false;
                }

                if (sys.getMasterChannelGroup(out _masterGroup) != FMOD.RESULT.OK)
                {
                    BppLog.Warn(Component, "Audio tap: getMasterChannelGroup failed.");
                    SafeStopInternal();
                    return false;
                }

                _capturePointLabel = "core-master-channel-group";
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

            _selfHandle = GCHandle.Alloc(this);
            var setUserDataResult = _dsp.setUserData(GCHandle.ToIntPtr(_selfHandle));
            if (setUserDataResult != FMOD.RESULT.OK)
            {
                BppLog.Warn(Component, $"Audio tap: setUserData failed with {setUserDataResult}.");
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
            // min 65536 floats), open the WAV, pin the callback, enable the tap,
            // and start draining.
            int cap = NextPow2(Math.Max(65536, _sampleRate * Math.Max(1, _channels)));
            _ring = new AudioRingBuffer(cap);
            _wav = new WavStreamWriter(_wavFilePath, _sampleRate, _channels);
            _callbackHandle = GCHandle.Alloc(s_readCallback);

            _writerRun = true;
            _captureEnabled = true;
            _writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "BPP.CombatReplayAudio.WavWriter",
            };
            _writerThread.Start();

            IsCapturing = true;
            BppLog.Info(
                Component,
                $"Audio tap attached at {_capturePointLabel} rate={_sampleRate} channels={_channels} ring={cap}"
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
    /// Tries to resolve the FMOD STUDIO bus channel group and lock it so the
    /// passthrough DSP can attach there. lockChannelGroup is asynchronous, so flushCommands
    /// forces the channel group to instantiate before getChannelGroup. Returns false on any
    /// failure. Logs the bus port index for routing diagnostics.
    /// </summary>
    private bool TryResolveStudioBusChannelGroup(string busPath, out FMOD.ChannelGroup group)
    {
        group = default;
        try
        {
            var bus = FMODUnity.RuntimeManager.GetBus(busPath);
            if (!bus.isValid())
            {
                BppLog.Info(Component, $"Audio tap: Studio bus '{busPath}' is not valid.");
                return false;
            }

            var portResult = bus.getPortIndex(out var portIndex);

            if (bus.lockChannelGroup() != FMOD.RESULT.OK)
            {
                BppLog.Info(Component, $"Audio tap: bus.lockChannelGroup failed for '{busPath}'.");
                return false;
            }

            // lockChannelGroup is queued; flush so the channel group exists before we read it.
            FMODUnity.RuntimeManager.StudioSystem.flushCommands();

            if (bus.getChannelGroup(out group) != FMOD.RESULT.OK || group.handle == IntPtr.Zero)
            {
                BppLog.Info(Component, $"Audio tap: bus.getChannelGroup failed for '{busPath}'.");
                try
                {
                    bus.unlockChannelGroup();
                }
                catch
                {
                    // Best-effort: nothing else to unwind.
                }
                return false;
            }

            _studioBus = bus;
            _busLocked = true;
            _capturePointLabel = $"studio-bus {busPath}";
            BppLog.Info(
                Component,
                $"Audio tap: capturing at Studio bus '{busPath}' (portIndex={FormatPortIndex(portResult, portIndex)})."
            );
            return true;
        }
        catch (Exception ex)
        {
            BppLog.Info(
                Component,
                $"Audio tap: resolving Studio bus '{busPath}' failed ({ex.Message})."
            );
            return false;
        }
    }

    private static void LogStudioBusRoutingDiagnostics()
    {
        lock (s_routingDiagnosticsLock)
        {
            if (s_routingDiagnosticsLogged)
                return;
            s_routingDiagnosticsLogged = true;
        }

        var seenPaths = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            LogSoundManagerBusRouting(seenPaths);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                Component,
                $"Audio tap bus diagnostic: SoundManager scan failed: {ex.Message}"
            );
        }

        try
        {
            LogLoadedBankBusRouting(seenPaths);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                Component,
                $"Audio tap bus diagnostic: loaded-bank scan failed: {ex.Message}"
            );
        }
    }

    private static void LogSoundManagerBusRouting(HashSet<string> seenPaths)
    {
        var soundManager = Services.Get<SoundManager>();
        if (soundManager == null)
        {
            BppLog.Info(Component, "Audio tap bus diagnostic: SoundManager unavailable.");
            return;
        }

        var soundManagerType = typeof(SoundManager);
        foreach (var fieldName in DiagnosticBusPathFields)
        {
            try
            {
                var field = soundManagerType.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                );
                var path = field?.GetValue(soundManager) as string;
                if (string.IsNullOrWhiteSpace(path))
                {
                    BppLog.Info(
                        Component,
                        $"Audio tap bus diagnostic: SoundManager.{fieldName} path=<none>"
                    );
                    continue;
                }

                seenPaths.Add(path);
                var bus = FMODUnity.RuntimeManager.GetBus(path);
                LogBusRouting($"SoundManager.{fieldName}", path, bus);
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    Component,
                    $"Audio tap bus diagnostic: SoundManager.{fieldName} failed: {ex.Message}"
                );
            }
        }
    }

    private static void LogLoadedBankBusRouting(HashSet<string> seenPaths)
    {
        var result = FMODUnity.RuntimeManager.StudioSystem.getBankList(out var banks);
        if (result != FMOD.RESULT.OK)
        {
            BppLog.Info(Component, $"Audio tap bus diagnostic: getBankList failed with {result}.");
            return;
        }

        if (banks == null || banks.Length == 0)
        {
            BppLog.Info(Component, "Audio tap bus diagnostic: no loaded banks.");
            return;
        }

        int totalBusRefs = 0;
        int loggedUniqueBuses = 0;
        foreach (var bank in banks)
        {
            if (!bank.isValid())
                continue;

            string bankPath = GetBankPathForDiagnostic(bank);
            var busResult = bank.getBusList(out var buses);
            if (busResult != FMOD.RESULT.OK || buses == null)
            {
                BppLog.Info(
                    Component,
                    $"Audio tap bus diagnostic: bank={bankPath} getBusList failed with {busResult}."
                );
                continue;
            }

            foreach (var bus in buses)
            {
                totalBusRefs++;
                if (LogBankBusRouting(bankPath, bus, seenPaths))
                    loggedUniqueBuses++;
            }
        }

        BppLog.Info(
            Component,
            $"Audio tap bus diagnostic: loaded-bank scan banks={banks.Length} busRefs={totalBusRefs} loggedUnique={loggedUniqueBuses}."
        );
    }

    private static bool LogBankBusRouting(
        string bankPath,
        FMOD.Studio.Bus bus,
        HashSet<string> seenPaths
    )
    {
        string path = GetBusPathForDiagnostic(bus);
        if (!string.IsNullOrWhiteSpace(path) && seenPaths.Contains(path))
            return false;

        if (!string.IsNullOrWhiteSpace(path))
            seenPaths.Add(path);

        LogBusRouting($"LoadedBank[{bankPath}]", path, bus);
        return true;
    }

    private static void LogBusRouting(string source, string path, FMOD.Studio.Bus bus)
    {
        try
        {
            if (!bus.isValid())
            {
                BppLog.Info(
                    Component,
                    $"Audio tap bus diagnostic: {source} path={path} valid=false"
                );
                return;
            }

            string portIndexLabel;
            var portResult = bus.getPortIndex(out var portIndex);
            portIndexLabel = FormatPortIndex(portResult, portIndex);

            var groupResult = bus.getChannelGroup(out var channelGroup);
            string channelGroupLabel =
                groupResult == FMOD.RESULT.OK && channelGroup.handle != IntPtr.Zero
                    ? "valid"
                    : $"unavailable:{groupResult}";

            BppLog.Info(
                Component,
                $"Audio tap bus diagnostic: {source} path={path} valid=true portIndex={portIndexLabel} channelGroup={channelGroupLabel}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                Component,
                $"Audio tap bus diagnostic: {source} path={path} failed: {ex.Message}"
            );
        }
    }

    private static string GetBankPathForDiagnostic(FMOD.Studio.Bank bank)
    {
        try
        {
            return bank.getPath(out var path) == FMOD.RESULT.OK && !string.IsNullOrWhiteSpace(path)
                ? path
                : "<unknown-bank>";
        }
        catch
        {
            return "<unknown-bank>";
        }
    }

    private static string GetBusPathForDiagnostic(FMOD.Studio.Bus bus)
    {
        try
        {
            return bus.getPath(out var path) == FMOD.RESULT.OK && !string.IsNullOrWhiteSpace(path)
                ? path
                : "<unknown-bus>";
        }
        catch
        {
            return "<unknown-bus>";
        }
    }

    private static string FormatPortIndex(FMOD.RESULT result, ulong portIndex)
    {
        if (result != FMOD.RESULT.OK)
            return $"<unavailable:{result}>";

        return portIndex == FmodPortIndexNone ? $"{portIndex} (NONE)" : portIndex.ToString();
    }

    /// <summary>
    /// MAIN thread (or any). Idempotent teardown. Disables capture first so the
    /// next callback no-ops the copy, removes + releases the DSP, joins the
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
        _captureEnabled = false;

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

        // Release our lock on the Studio bus channel group (if we attached there) so FMOD
        // can free it again. Done after removeDSP so our DSP is already detached.
        try
        {
            if (_busLocked)
            {
                _studioBus.unlockChannelGroup();
                _busLocked = false;
            }
        }
        catch (Exception ex)
        {
            BppLog.Warn(Component, $"Audio tap: unlockChannelGroup failed: {ex.Message}");
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

        try
        {
            if (_selfHandle.IsAllocated)
                _selfHandle.Free();
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
    /// interleaved float PCM into the DSP's tap ring.
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

            // Side-tap: duplicate the samples into this DSP's ring (producer = wait-free).
            var tap = GetTapFromDspState(ref state);
            if (tap != null && tap._captureEnabled && inbuffer != IntPtr.Zero && total > 0)
            {
                // Latch the true interleave width the first time we learn it from the
                // mixer so Stop() can stamp the WAV header with the real channel count
                // (the provisional speaker-mode guess can disagree). Volatile publish so
                // the main thread observes it at teardown.
                if (tap._observedChannels != inchannels)
                    tap._observedChannels = inchannels;

                tap.AccumulateStats(inbuffer, total);
                tap._ring?.Write(inbuffer, total);
            }
        }
        catch
        {
            // Swallow — never throw across the native boundary, never log here.
        }

        return FMOD.RESULT.OK;
    }

    private unsafe void AccumulateStats(IntPtr inbuffer, int total)
    {
        var samples = (float*)inbuffer;
        double sumSquares = 0;
        var peak = _peakAbs;
        for (var i = 0; i < total; i++)
        {
            var sample = samples[i];
            sumSquares += (double)sample * sample;
            var abs = sample < 0 ? -sample : sample;
            if (abs > peak)
                peak = abs;
        }

        _sumSquares += sumSquares;
        _statSampleCount += total;
        _peakAbs = peak;
    }

    private static FmodAudioCaptureTap? GetTapFromDspState(ref FMOD.DSP_STATE state)
    {
        if (state.instance == IntPtr.Zero)
            return null;

        var dsp = new FMOD.DSP(state.instance);
        if (dsp.getUserData(out var userData) != FMOD.RESULT.OK || userData == IntPtr.Zero)
            return null;

        var handle = GCHandle.FromIntPtr(userData);
        return handle.Target as FmodAudioCaptureTap;
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
