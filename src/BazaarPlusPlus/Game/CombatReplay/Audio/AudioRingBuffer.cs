#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace BazaarPlusPlus.Game.CombatReplay.Audio;

/// <summary>
/// Lock-free single-producer/single-consumer ring buffer of 32-bit float PCM
/// samples. The producer is the FMOD mixer thread (the DSP read callback) and the
/// consumer is the background WAV writer thread.
///
/// The producer side is wait-free and allocation-free and NEVER touches the read
/// cursor: it only advances <see cref="_writePos"/>, overwriting the oldest slots in
/// place when it laps the reader. The consumer detects the lap and fast-forwards its
/// OWN <see cref="_readPos"/> past the clobbered region before reading, so the newest
/// audio always wins (recent audio matters more than stale audio the writer failed to
/// drain). Keeping each cursor strictly single-writer is the real SPSC contract: there
/// is no read-modify-write race on a shared cursor.
///
/// Indices are monotonic <see cref="long"/> cursors; the physical slot is
/// <c>pos &amp; _mask</c>. Cross-thread visibility: the producer publishes
/// <see cref="_writePos"/> with a release write after copying and the consumer reads it
/// with an acquire load, so it never observes data ahead of the published cursor.
/// (A sustained overrun can still tear a row mid-copy — accepted for lossy live
/// capture; in practice the writer thread keeps up and the ring never overflows.)
/// </summary>
internal sealed class AudioRingBuffer
{
    private readonly float[] _buffer;
    private readonly int _capacity;
    private readonly int _mask;

    // Producer-owned cursor (FMOD mixer thread). Read plainly by the producer,
    // published via Volatile.Write, observed by the consumer via Volatile.Read.
    private long _writePos;

    // Consumer-owned cursor (WAV writer thread). Read plainly by the consumer,
    // published via Volatile.Write, observed by the producer via Volatile.Read.
    private long _readPos;

    public AudioRingBuffer(int capacityFloats)
    {
        var cap = NextPowerOfTwo(capacityFloats);
        _capacity = cap;
        _mask = cap - 1;
        _buffer = new float[cap];
    }

    /// <summary>The buffer capacity in floats (always a power of two, &gt;= 2).</summary>
    public int Capacity => _capacity;

    /// <summary>Total floats written by the producer since construction (diagnostics/tests).</summary>
    public long TotalWritten => Volatile.Read(ref _writePos);

    /// <summary>Total floats consumed by the reader since construction (diagnostics/tests).</summary>
    public long TotalRead => Volatile.Read(ref _readPos);

    /// <summary>
    /// PRODUCER side. Copies <paramref name="floatCount"/> interleaved floats from the
    /// unmanaged <paramref name="source"/> pointer into the ring. Wait-free,
    /// allocation-free, never throws. On overrun the oldest samples are dropped.
    /// </summary>
    public void Write(IntPtr source, int floatCount)
    {
        if (floatCount <= 0 || source == IntPtr.Zero)
            return;

        // Producer owns _writePos and NEVER reads or writes _readPos: on overrun the
        // consumer fast-forwards its own cursor, so each cursor stays single-writer.
        var w = _writePos;

        var srcOffset = 0;
        // A single write larger than the whole ring can only keep the last
        // _capacity floats; discard the leading remainder up front.
        if (floatCount > _capacity)
        {
            srcOffset = floatCount - _capacity;
            floatCount = _capacity;
        }

        var physIndex = (int)(w & _mask);
        var firstRun = Math.Min(floatCount, _capacity - physIndex);
        // Marshal.Copy reads floats from the unmanaged buffer; source is float PCM
        // (4 bytes/sample), so byte offsets are sample offsets * 4.
        Marshal.Copy(source + (srcOffset * sizeof(float)), _buffer, physIndex, firstRun);

        var secondRun = floatCount - firstRun;
        if (secondRun > 0)
        {
            Marshal.Copy(source + ((srcOffset + firstRun) * sizeof(float)), _buffer, 0, secondRun);
        }

        Volatile.Write(ref _writePos, w + floatCount);
    }

    /// <summary>
    /// PRODUCER side (managed overload). Same semantics as the
    /// <see cref="Write(IntPtr,int)"/> path but copies from a managed array. Used by
    /// unit tests and the tap's pre-pinned scratch path.
    /// </summary>
    public void Write(float[] source, int offset, int floatCount)
    {
        if (source == null || floatCount <= 0)
            return;
        if (offset < 0 || offset >= source.Length)
            return;

        // Clamp to the available source range so the copy can never run off the
        // end of the supplied array.
        if (floatCount > source.Length - offset)
            floatCount = source.Length - offset;
        if (floatCount <= 0)
            return;

        // Producer owns _writePos and never touches _readPos (see Write(IntPtr)).
        var w = _writePos;

        if (floatCount > _capacity)
        {
            offset += floatCount - _capacity;
            floatCount = _capacity;
        }

        var physIndex = (int)(w & _mask);
        var firstRun = Math.Min(floatCount, _capacity - physIndex);
        Array.Copy(source, offset, _buffer, physIndex, firstRun);

        var secondRun = floatCount - firstRun;
        if (secondRun > 0)
            Array.Copy(source, offset + firstRun, _buffer, 0, secondRun);

        Volatile.Write(ref _writePos, w + floatCount);
    }

    /// <summary>
    /// CONSUMER side. Drains up to <paramref name="maxFloats"/> floats into
    /// <paramref name="destination"/> starting at <paramref name="destinationOffset"/>.
    /// Returns the number of floats copied (0 when empty). Wait-free, never throws.
    /// </summary>
    public int Read(float[] destination, int destinationOffset, int maxFloats)
    {
        if (destination == null || maxFloats <= 0)
            return 0;
        if (destinationOffset < 0 || destinationOffset >= destination.Length)
            return 0;

        var w = Volatile.Read(ref _writePos);
        // Consumer owns _readPos, so a plain read is sufficient here.
        var r = _readPos;

        // Overrun: the producer lapped us and overwrote the oldest (w - r - _capacity)
        // floats. Fast-forward our OWN cursor past the clobbered region so we only ever
        // read live slots. This is the sole place dropped samples are accounted, keeping
        // the producer free of the consumer cursor (strict SPSC).
        if (w - r > _capacity)
            r = w - _capacity;

        var avail = (int)(w - r);
        var n = Math.Min(Math.Min(avail, maxFloats), destination.Length - destinationOffset);
        if (n <= 0)
            return 0;

        var physIndex = (int)(r & _mask);
        var firstRun = Math.Min(n, _capacity - physIndex);
        Array.Copy(_buffer, physIndex, destination, destinationOffset, firstRun);

        var secondRun = n - firstRun;
        if (secondRun > 0)
            Array.Copy(_buffer, 0, destination, destinationOffset + firstRun, secondRun);

        Volatile.Write(ref _readPos, r + n);
        return n;
    }

    private static int NextPowerOfTwo(int value)
    {
        // Minimum capacity of 2 keeps _mask (cap - 1) valid and gives the producer
        // at least one free slot to make progress.
        if (value < 2)
            return 2;

        // Already a power of two: keep it.
        if ((value & (value - 1)) == 0)
            return value;

        var result = 2;
        while (result < value)
        {
            var next = result << 1;
            // Guard against int overflow for pathological capacities; clamp to the
            // largest representable power of two.
            if (next <= result)
                return result;
            result = next;
        }

        return result;
    }
}
