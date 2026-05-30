using System.Reflection;

// Behavioral unit tests for the three design-mandated pure-logic units of the
// combat-replay audio/video pipeline, reached as internal types via reflection
// (Type.GetType("Full.Name, BazaarPlusPlus")):
//   1) AudioRingBuffer    -- lock-free SPSC correctness
//   2) WavStreamWriter    -- byte-exact WAV header
//   3) WallClockCfrPacer  -- wall-clock repeat/drop counting
// Plus an optional ReplayVideoFramePool Rent/Return + cap micro-check, and the
// ReplayVideoAudioMuxer zero-duration-output guard that protects the silent video
// when a -shortest mux of an empty WAV exits 0 with an empty output.

RingBufferTests.Run();
WavHeaderTests.Run();
CfrPacerTests.Run();
FramePoolTests.Run();
ZeroDurationMuxGuardTests.Run();
MuxerArgumentTests.Run();
MuxerDebugStemTests.Run();
AudioTapPlanTests.Run();

Console.WriteLine("CombatReplayAudioVideo tests passed.");

// ---------------------------------------------------------------------------
// 1) AudioRingBuffer: lock-free single-producer/single-consumer ring buffer.
// ---------------------------------------------------------------------------
file static class RingBufferTests
{
    private static readonly Type RingType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Audio.AudioRingBuffer"
    );

    public static void Run()
    {
        BasicFifoRoundTrip();
        WrapAroundPastCapacity();
        OverflowDropsOldest();
        ConcurrentProducerConsumer();
    }

    // FIFO ordering: push an ascending sequence in uneven chunks, drain it in
    // differently-sized chunks, and assert the drained stream equals the input
    // in order with no gaps or duplicates while it fits under capacity.
    private static void BasicFifoRoundTrip()
    {
        var ring = New(1024);
        TestReflection.Assert(
            Capacity(ring) == 1024,
            "capacity 1024 should round to a power of two unchanged."
        );

        const int total = 700; // < capacity, so nothing is ever dropped.
        var source = new float[total];
        for (var i = 0; i < total; i++)
            source[i] = i;

        // Produce in chunks of 37 (uneven, forces a mid-buffer offset).
        var produced = 0;
        var consumed = 0;
        var dest = new float[total];
        var readBuf = new float[64];
        var next = 0f;

        while (produced < total)
        {
            var chunk = Math.Min(37, total - produced);
            Write(ring, source, produced, chunk);
            produced += chunk;

            // Drain opportunistically in 50-float reads so reads and writes interleave.
            int n;
            while ((n = Read(ring, readBuf, 0, 50)) > 0)
            {
                for (var i = 0; i < n; i++)
                {
                    TestReflection.Assert(
                        readBuf[i] == next,
                        $"FIFO order broke: expected {next}, got {readBuf[i]} at index {consumed}."
                    );
                    dest[consumed++] = readBuf[i];
                    next++;
                }
            }
        }

        // Final drain.
        int tail;
        while ((tail = Read(ring, readBuf, 0, readBuf.Length)) > 0)
        {
            for (var i = 0; i < tail; i++)
            {
                TestReflection.Assert(readBuf[i] == next, $"FIFO tail order broke at {consumed}.");
                dest[consumed++] = readBuf[i];
                next++;
            }
        }

        TestReflection.Assert(
            consumed == total,
            $"Should drain every produced float ({consumed} != {total})."
        );
        TestReflection.Assert(
            TotalWritten(ring) == total && TotalRead(ring) == total,
            "When produced <= capacity, TotalRead should equal TotalWritten with no drops."
        );
        for (var i = 0; i < total; i++)
            TestReflection.Assert(dest[i] == i, $"Drained value {dest[i]} != source {i}.");
    }

    // Write across the physical wrap boundary (more than one lap) without
    // overflowing the live window, and confirm ordering is preserved across the
    // mask wrap.
    private static void WrapAroundPastCapacity()
    {
        var ring = New(16); // small ring, exercises (pos & mask) wrap repeatedly.
        var cap = Capacity(ring);
        TestReflection.Assert(cap == 16, "16 should stay 16 (already a power of two).");

        // Push and immediately drain small chunks far past the physical capacity so
        // monotonic write/read cursors lap the backing array many times.
        const int laps = 400;
        var next = 0f;
        var expect = 0f;
        var rd = new float[8];
        for (var i = 0; i < laps; i++)
        {
            var chunk = new float[5];
            for (var k = 0; k < 5; k++)
                chunk[k] = next++;
            Write(ring, chunk, 0, 5);

            int n;
            while ((n = Read(ring, rd, 0, rd.Length)) > 0)
            {
                for (var j = 0; j < n; j++)
                {
                    TestReflection.Assert(
                        rd[j] == expect,
                        $"Wrap-around order broke: expected {expect}, got {rd[j]}."
                    );
                    expect++;
                }
            }
        }

        TestReflection.Assert(
            expect == laps * 5,
            "Wrap-around round trip should preserve every sample."
        );
        TestReflection.Assert(
            TotalWritten(ring) == laps * 5 && TotalRead(ring) == laps * 5,
            "Drained-as-you-go wrap test should not drop any samples."
        );
    }

    // Overflow policy = overwrite-oldest. Fill past capacity without reading; the
    // producer must not block or throw, a subsequent read must return the NEWEST
    // samples (oldest dropped), survivors must be monotonic and originally-written
    // values, and the read cursor must have advanced past the dropped region.
    private static void OverflowDropsOldest()
    {
        var ring = New(16);
        var cap = Capacity(ring);

        // Many small writes that each fit but collectively overflow: the producer keeps
        // advancing _writePos past the reader; the consumer's fast-forward drops the
        // clobbered oldest on read (the documented overwrite-oldest path).
        const int total = 100;
        var next = 0f;
        for (var i = 0; i < total; i += 4)
        {
            var chunk = new float[4];
            for (var k = 0; k < 4; k++)
                chunk[k] = next++;
            Write(ring, chunk, 0, 4); // never throws, never blocks
        }

        var written = TotalWritten(ring);
        TestReflection.Assert(
            written == total,
            $"Each write should publish its full count (TotalWritten {written} != {total})."
        );
        // Consumer-lazy overwrite-oldest: the producer NEVER advances the read cursor, so
        // before the first read the logical write-ahead (TotalWritten - TotalRead) may
        // exceed capacity; the consumer fast-forwards past the clobbered region when it
        // reads. Only <= capacity slots physically survive, which the drain below proves.

        // Drain survivors and assert they are the newest contiguous, monotonic tail.
        var survivors = new List<float>();
        var rd = new float[cap];
        int n;
        while ((n = Read(ring, rd, 0, rd.Length)) > 0)
            for (var i = 0; i < n; i++)
                survivors.Add(rd[i]);

        TestReflection.Assert(
            survivors.Count > 0 && survivors.Count <= cap,
            "Survivors should be a non-empty window <= capacity."
        );
        // No torn values: every survivor is an originally-written value [0,total).
        foreach (var v in survivors)
            TestReflection.Assert(
                v >= 0 && v < total && v == MathF.Floor(v),
                $"Torn survivor value {v}."
            );
        // Monotonic & contiguous among survivors (oldest dropped, newest kept).
        for (var i = 1; i < survivors.Count; i++)
            TestReflection.Assert(
                survivors[i] == survivors[i - 1] + 1,
                $"Survivors must be a contiguous ascending tail; broke at {survivors[i]}."
            );
        TestReflection.Assert(
            survivors[^1] == total - 1,
            $"Overwrite-oldest must keep the newest sample; last survivor {survivors[^1]} != {total - 1}."
        );
        // After the drain the consumer's fast-forward must account for EVERY written
        // float (skipped + read) and leave an empty live window.
        TestReflection.Assert(
            TotalRead(ring) == total,
            $"Draining must account for every written float (TotalRead {TotalRead(ring)} != {total})."
        );
        TestReflection.Assert(
            TotalWritten(ring) - TotalRead(ring) == 0,
            "Draining must leave an empty live window."
        );

        // A single oversized write keeps only the last capacity floats and never throws.
        var ring2 = New(16);
        var big = new float[40];
        for (var i = 0; i < big.Length; i++)
            big[i] = 1000 + i;
        Write(ring2, big, 0, big.Length); // oversized single write: must not throw
        var got = new float[64];
        var m = Read(ring2, got, 0, got.Length);
        TestReflection.Assert(
            m > 0 && m <= Capacity(ring2),
            "Oversized write should leave at most capacity floats."
        );
        TestReflection.Assert(
            got[m - 1] == 1000 + 39,
            "Oversized single write must retain the newest sample at the tail."
        );
        for (var i = 1; i < m; i++)
            TestReflection.Assert(
                got[i] == got[i - 1] + 1,
                "Oversized-write survivors must be a contiguous ascending tail."
            );
    }

    // Real concurrency: one producer thread writes M frames of `channels` distinct
    // increasing floats; one consumer thread drains. Assert no value is read twice
    // and reads are strictly monotonic (no torn/duplicated samples under contention).
    private static void ConcurrentProducerConsumer()
    {
        var ring = New(4096);
        const int channels = 2;
        const int frames = 100_000; // 200k floats total, fast.
        long lastSeen = -1;
        var torn = 0;
        var nonMonotonic = 0;
        var consumed = 0L;
        var done = false;

        var producer = new Thread(() =>
        {
            var frame = new float[channels];
            long value = 0;
            for (var f = 0; f < frames; f++)
            {
                for (var c = 0; c < channels; c++)
                    frame[c] = value++;
                // Keep the live window safely under capacity so this no-loss test never
                // trips the overwrite-oldest path: back off if the consumer falls behind.
                // (Real capture is lossy by design; this is test pacing only.)
                while (TotalWritten(ring) - TotalRead(ring) > Capacity(ring) - 256)
                    Thread.SpinWait(100);
                Write(ring, frame, 0, channels);
            }
        })
        {
            IsBackground = true,
            Name = "ring-producer",
        };

        var consumer = new Thread(() =>
        {
            var buf = new float[512];
            while (!Volatile.Read(ref done) || TotalWritten(ring) > TotalRead(ring))
            {
                var n = Read(ring, buf, 0, buf.Length);
                if (n == 0)
                {
                    Thread.SpinWait(50);
                    continue;
                }

                for (var i = 0; i < n; i++)
                {
                    var v = (long)buf[i];
                    if (v <= lastSeen)
                    {
                        if (v == lastSeen)
                            torn++;
                        else
                            nonMonotonic++;
                    }

                    lastSeen = v;
                    consumed++;
                }
            }
        })
        {
            IsBackground = true,
            Name = "ring-consumer",
        };

        consumer.Start();
        producer.Start();
        TestReflection.Assert(
            producer.Join(TimeSpan.FromSeconds(15)),
            "Producer thread should finish within timeout."
        );
        Volatile.Write(ref done, true);
        TestReflection.Assert(
            consumer.Join(TimeSpan.FromSeconds(15)),
            "Consumer thread should finish within timeout."
        );

        TestReflection.Assert(torn == 0, $"No sample should be read twice (torn={torn}).");
        TestReflection.Assert(
            nonMonotonic == 0,
            $"Reads must be strictly monotonic (out-of-order={nonMonotonic})."
        );
        TestReflection.Assert(
            consumed == (long)frames * channels,
            $"Bounded-rate SPSC drain should observe every produced sample exactly once ({consumed})."
        );
    }

    private static object New(int capacityFloats) =>
        Activator.CreateInstance(RingType, capacityFloats)
        ?? throw new InvalidOperationException("AudioRingBuffer should be constructible.");

    private static int Capacity(object ring) =>
        (int)TestReflection.GetProp(RingType, ring, "Capacity")!;

    private static long TotalWritten(object ring) =>
        (long)TestReflection.GetProp(RingType, ring, "TotalWritten")!;

    private static long TotalRead(object ring) =>
        (long)TestReflection.GetProp(RingType, ring, "TotalRead")!;

    private static void Write(object ring, float[] src, int offset, int count) =>
        TestReflection.InvokeManagedWrite(RingType, ring, src, offset, count);

    private static int Read(object ring, float[] dest, int offset, int max) =>
        (int)
            TestReflection.Invoke(
                RingType,
                ring,
                "Read",
                new[] { typeof(float[]), typeof(int), typeof(int) },
                new object[] { dest, offset, max }
            )!;
}

// ---------------------------------------------------------------------------
// 2) WavStreamWriter.BuildHeader: byte-exact 44-byte IEEE-float WAV header.
// ---------------------------------------------------------------------------
file static class WavHeaderTests
{
    private static readonly Type WavType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Audio.WavStreamWriter"
    );

    public static void Run()
    {
        StereoHeader();
        MonoEmptyHeader();
    }

    private static void StereoHeader()
    {
        // 48000 Hz, 2ch, dataByteLength = 8.
        var header = BuildHeader(48000, 2, 8);
        TestReflection.Assert(header.Length == 44, "WAV header must be exactly 44 bytes.");

        TestReflection.Assert(Ascii(header, 0, 4) == "RIFF", "bytes 0-3 must be 'RIFF'.");
        TestReflection.Assert(
            U32(header, 4) == 36u + 8u,
            "bytes 4-7 must be LE (36+dataByteLength)=44."
        );
        TestReflection.Assert(Ascii(header, 8, 4) == "WAVE", "bytes 8-11 must be 'WAVE'.");
        TestReflection.Assert(Ascii(header, 12, 4) == "fmt ", "bytes 12-15 must be 'fmt '.");
        TestReflection.Assert(
            U32(header, 16) == 16u,
            "bytes 16-19 must be LE 16 (fmt chunk size)."
        );
        TestReflection.Assert(
            U16(header, 20) == 3,
            "bytes 20-21 must be LE 3 (WAVE_FORMAT_IEEE_FLOAT)."
        );
        TestReflection.Assert(U16(header, 22) == 2, "bytes 22-23 must be LE 2 (channels).");
        TestReflection.Assert(
            U32(header, 24) == 48000u,
            "bytes 24-27 must be LE 48000 (sample rate)."
        );
        TestReflection.Assert(
            U32(header, 28) == 48000u * 2u * 4u,
            "bytes 28-31 must be LE byteRate=384000."
        );
        TestReflection.Assert(U16(header, 32) == 2 * 4, "bytes 32-33 must be LE blockAlign=8.");
        TestReflection.Assert(U16(header, 34) == 32, "bytes 34-35 must be LE 32 (bitsPerSample).");
        TestReflection.Assert(Ascii(header, 36, 4) == "data", "bytes 36-39 must be 'data'.");
        TestReflection.Assert(U32(header, 40) == 8u, "bytes 40-43 must be LE 8 (dataByteLength).");
    }

    private static void MonoEmptyHeader()
    {
        // 44100 Hz, 1ch, dataByteLength = 0 (placeholder header on file open).
        var header = BuildHeader(44100, 1, 0);
        TestReflection.Assert(header.Length == 44, "Mono header must be exactly 44 bytes.");
        TestReflection.Assert(U16(header, 22) == 1, "Mono header channels must be 1.");
        TestReflection.Assert(U32(header, 24) == 44100u, "Mono header sample rate must be 44100.");
        TestReflection.Assert(
            U32(header, 28) == 44100u * 1u * 4u,
            "Mono byteRate must be 44100*1*4=176400."
        );
        TestReflection.Assert(U16(header, 32) == 1 * 4, "Mono blockAlign must be 4.");
        TestReflection.Assert(U16(header, 34) == 32, "Mono bitsPerSample must be 32.");
        TestReflection.Assert(
            U32(header, 4) == 36u,
            "Empty-data riffChunkSize must be 36 (=36+0)."
        );
        TestReflection.Assert(U32(header, 40) == 0u, "Empty-data dataChunkSize must be 0.");
    }

    private static byte[] BuildHeader(int sampleRate, int channels, int dataByteLength)
    {
        var method =
            WavType.GetMethod(
                "BuildHeader",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static
            ) ?? throw new InvalidOperationException("WavStreamWriter.BuildHeader not found.");
        return (byte[])(
            method.Invoke(null, new object[] { sampleRate, channels, dataByteLength })
            ?? throw new InvalidOperationException("BuildHeader returned null.")
        );
    }

    private static string Ascii(byte[] b, int offset, int len)
    {
        var chars = new char[len];
        for (var i = 0; i < len; i++)
            chars[i] = (char)b[offset + i];
        return new string(chars);
    }

    // Decode little-endian explicitly (do not rely on host endianness).
    private static uint U32(byte[] b, int o) =>
        (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

    private static int U16(byte[] b, int o) => b[o] | (b[o + 1] << 8);
}

// ---------------------------------------------------------------------------
// 3) WallClockCfrPacer: wall-clock CFR repeat/drop counting via CfrTickResult.
// ---------------------------------------------------------------------------
file static class CfrPacerTests
{
    private static readonly Type PacerType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.WallClockCfrPacer"
    );

    public static void Run()
    {
        NoLeadingPad();
        SteadyCadenceNoRepeats();
        RepeatsWhenCaptureStalls();
        DropOnOvershootResync();
    }

    // First tick with no frame available: nothing emitted, clock not started.
    private static void NoLeadingPad()
    {
        var pacer = New(30);
        long lastEmitted = -1;
        var r = Tick(pacer, now: 0.0, hasFrame: false, latestSeq: 0, ref lastEmitted);
        TestReflection.Assert(
            r.Emit == 0 && r.Repeat == 0 && r.Dropped == 0,
            "No leading pad: empty tick before first frame."
        );

        // Still not started: even a much later no-frame tick stays empty.
        r = Tick(pacer, now: 5.0, hasFrame: false, latestSeq: 0, ref lastEmitted);
        TestReflection.Assert(
            r.Emit == 0 && r.Repeat == 0 && r.Dropped == 0,
            "No-frame ticks must not start the clock or emit."
        );
    }

    // 30 fps, clock advances exactly one frame interval per tick, a NEW seq each
    // tick: total emits == 30, repeats == 0 over the simulated second.
    private static void SteadyCadenceNoRepeats()
    {
        var pacer = New(30);
        var interval = 1.0 / 30.0;
        long lastEmitted = -1;
        long seq = 0;
        var now = 0.0;
        var totalEmit = 0;
        var totalRepeat = 0;
        var totalDropped = 0;

        for (var i = 0; i < 30; i++)
        {
            var r = Tick(pacer, now, hasFrame: true, latestSeq: seq, ref lastEmitted);
            totalEmit += r.Emit;
            totalRepeat += r.Repeat;
            totalDropped += r.Dropped;
            now += interval;
            seq++; // brand-new source frame each render tick
        }

        TestReflection.Assert(
            totalEmit == 30,
            $"Steady 30fps cadence should emit exactly 30 slots, got {totalEmit}."
        );
        TestReflection.Assert(
            totalRepeat == 0,
            $"One new frame per slot means zero repeats, got {totalRepeat}."
        );
        TestReflection.Assert(
            totalDropped == 0,
            $"Steady cadence should not drop, got {totalDropped}."
        );
    }

    // After priming one frame, advance the clock by 3 intervals while keeping
    // latestSeq CONSTANT: the extra slots repeat the last frame.
    private static void RepeatsWhenCaptureStalls()
    {
        var pacer = New(30);
        var interval = 1.0 / 30.0;
        long lastEmitted = -1;
        const long frozenSeq = 7;

        // Prime: first frame at t=0 establishes the timeline and emits slot 0 (new).
        var prime = Tick(pacer, now: 0.0, hasFrame: true, latestSeq: frozenSeq, ref lastEmitted);
        TestReflection.Assert(
            prime.Emit == 1 && prime.Repeat == 0,
            "Priming tick should emit exactly the first (new) frame."
        );

        // Advance ~3 intervals total with the SAME seq across a few ticks, staying
        // under the overshoot resync threshold so nothing is dropped.
        var totalEmit = prime.Emit;
        var totalRepeat = prime.Repeat;
        var totalDropped = prime.Dropped;
        var now = 0.0;
        for (var i = 0; i < 3; i++)
        {
            now += interval;
            var r = Tick(pacer, now, hasFrame: true, latestSeq: frozenSeq, ref lastEmitted);
            totalEmit += r.Emit;
            totalRepeat += r.Repeat;
            totalDropped += r.Dropped;
        }

        // Slots elapsed = 4 (slot0 prime + 3 more). Emit==slots; the 3 beyond the
        // first duplicate the frozen frame => RepeatCount == slots-1.
        TestReflection.Assert(
            totalEmit == 4,
            $"Four CFR slots should be emitted across the stall, got {totalEmit}."
        );
        TestReflection.Assert(
            totalRepeat == 3,
            $"Slots beyond the first must repeat the last frame: expected 3 repeats, got {totalRepeat}."
        );
        TestReflection.Assert(
            totalDropped == 0,
            $"Stall under the resync threshold should drop nothing, got {totalDropped}."
        );
    }

    // Prime, then jump the clock far past resyncOvershootIntervals*interval in a
    // single tick. EmitCount is clamped by maxEmitsPerTick=3, DroppedCount>0, and
    // the pacer resyncs so the next steady tick emits normally (0 or 1).
    private static void DropOnOvershootResync()
    {
        // maxEmitsPerTick=3, resyncOvershootIntervals=5.
        var pacer = New(30, maxEmitsPerTick: 3, resyncOvershootIntervals: 5);
        var interval = 1.0 / 30.0;
        long lastEmitted = -1;
        long seq = 0;

        // Prime at t=0.
        var prime = Tick(pacer, now: 0.0, hasFrame: true, latestSeq: seq, ref lastEmitted);
        TestReflection.Assert(prime.Emit == 1, "Priming tick should emit the first frame.");
        seq++;

        // Single huge jump: ~10 intervals past the last emit time in one tick.
        var jumpNow = 10.0 * interval;
        var r = Tick(pacer, jumpNow, hasFrame: true, latestSeq: seq, ref lastEmitted);
        TestReflection.Assert(r.Emit <= 3, $"maxEmitsPerTick clamp must hold (emit={r.Emit}).");
        TestReflection.Assert(
            r.Dropped > 0,
            $"Overshoot resync must count skipped slots as dropped (dropped={r.Dropped})."
        );

        // After resync the clock is realigned to jumpNow+interval, so a tick a hair
        // before that emits 0, and a tick one interval later emits exactly 1.
        var before = Tick(
            pacer,
            jumpNow + (interval * 0.5),
            hasFrame: true,
            latestSeq: ++seq,
            ref lastEmitted
        );
        TestReflection.Assert(
            before.Emit == 0 && before.Dropped == 0,
            $"Resync should not emit early (emit={before.Emit})."
        );

        var after = Tick(
            pacer,
            jumpNow + (interval * 1.5),
            hasFrame: true,
            latestSeq: ++seq,
            ref lastEmitted
        );
        TestReflection.Assert(
            after.Emit == 1 && after.Dropped == 0,
            $"After resync, steady cadence should resume emitting one slot (emit={after.Emit}, dropped={after.Dropped})."
        );
    }

    private static object New(int fps) =>
        Activator.CreateInstance(PacerType, fps, 3, 5)
        ?? throw new InvalidOperationException("WallClockCfrPacer should be constructible.");

    private static object New(int fps, int maxEmitsPerTick, int resyncOvershootIntervals) =>
        Activator.CreateInstance(PacerType, fps, maxEmitsPerTick, resyncOvershootIntervals)
        ?? throw new InvalidOperationException("WallClockCfrPacer should be constructible.");

    private static TickResult Tick(
        object pacer,
        double now,
        bool hasFrame,
        long latestSeq,
        ref long lastEmittedSeq
    )
    {
        var method =
            PacerType.GetMethod(
                "Tick",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            ) ?? throw new InvalidOperationException("WallClockCfrPacer.Tick not found.");

        var args = new object[] { now, hasFrame, latestSeq, lastEmittedSeq };
        var result =
            method.Invoke(pacer, args)
            ?? throw new InvalidOperationException("Tick returned null.");
        lastEmittedSeq = (long)args[3]; // ref parameter is written back into the args array

        var rt = result.GetType();
        return new TickResult(
            (int)TestReflection.GetField(rt, result, "EmitCount")!,
            (int)TestReflection.GetField(rt, result, "RepeatCount")!,
            (int)TestReflection.GetField(rt, result, "DroppedCount")!
        );
    }

    private readonly record struct TickResult(int Emit, int Repeat, int Dropped);
}

// ---------------------------------------------------------------------------
// 4) ReplayVideoFramePool: optional Rent/Return + cap micro-check.
// ---------------------------------------------------------------------------
file static class FramePoolTests
{
    private static readonly Type PoolType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoFramePool"
    );

    public static void Run()
    {
        const int frameLen = 64;
        const int maxBuffers = 3;
        var pool =
            Activator.CreateInstance(PoolType, frameLen, maxBuffers)
            ?? throw new InvalidOperationException("ReplayVideoFramePool should be constructible.");

        TestReflection.Assert(
            (int)TestReflection.GetProp(PoolType, pool, "FrameByteLength")! == frameLen,
            "FrameByteLength should round-trip the ctor value."
        );

        // Rent up to the cap: every buffer is exactly frameLen bytes.
        var rented = new List<byte[]>();
        for (var i = 0; i < maxBuffers; i++)
        {
            var buf = Rent(pool);
            TestReflection.Assert(buf != null, $"Rent #{i} under cap should allocate a buffer.");
            TestReflection.Assert(
                buf!.Length == frameLen,
                "Rented buffers must be exactly FrameByteLength."
            );
            rented.Add(buf);
        }

        // Past the cap -> null (caller drops the frame), never throws.
        TestReflection.Assert(
            Rent(pool) == null,
            "Renting past the live cap should return null, not throw."
        );

        // Return a correctly-sized buffer, then Rent reuses it (no new allocation, still under cap).
        Return(pool, rented[0]);
        var reused = Rent(pool);
        TestReflection.Assert(
            reused != null && reused!.Length == frameLen,
            "Returned buffer should be reusable."
        );
        TestReflection.Assert(
            ReferenceEquals(reused, rented[0]),
            "Rent after Return should hand back the freed buffer."
        );

        // Returning a wrong-sized buffer is dropped (never requeued) and frees a cap slot.
        Return(pool, new byte[frameLen + 1]); // wrong size: must be dropped, decrements live count
        // Because a live slot was freed by dropping the oversized buffer, a fresh Rent
        // can allocate again rather than hitting the cap.
        var afterDrop = Rent(pool);
        TestReflection.Assert(
            afterDrop != null && afterDrop!.Length == frameLen,
            "Dropping a wrong-sized buffer must free a live slot so a new exact-size buffer can be rented."
        );

        // Null return is ignored (no throw).
        Return(pool, null!);
    }

    private static byte[]? Rent(object pool) =>
        (byte[]?)
            TestReflection.Invoke(PoolType, pool, "Rent", Type.EmptyTypes, Array.Empty<object>());

    private static void Return(object pool, byte[] buffer) =>
        TestReflection.Invoke(
            PoolType,
            pool,
            "Return",
            new[] { typeof(byte[]) },
            new object[] { buffer }
        );
}

// ---------------------------------------------------------------------------
// 5) ReplayVideoAudioMuxer.IsLikelyZeroDurationOutput: the gate that stops a
//    -shortest mux of an empty WAV (exit 0, ~hundred-byte stub) from deleting the
//    good silent first-pass video. Reproduced sizes: a real 3s recording is
//    multi-MB; the empty-output stub is ~261 bytes.
// ---------------------------------------------------------------------------
file static class ZeroDurationMuxGuardTests
{
    private static readonly Type MuxerType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoAudioMuxer"
    );

    public static void Run()
    {
        // The repro: a ~261-byte empty-output stub against a multi-MB silent video
        // must be flagged as zero-duration so the muxer keeps the silent recording.
        TestReflection.Assert(
            Guard(261, 3_000_000),
            "An empty-output stub far smaller than the silent video must be flagged zero-duration."
        );

        // A real muxed file (slightly larger than the silent input, since -c:v copy
        // carries the full video payload plus an AAC track) must NOT be flagged.
        TestReflection.Assert(
            !Guard(3_100_000, 3_000_000),
            "A muxed file at least the size of the silent video must not be flagged."
        );

        // A tiny but legitimate recording: output >= silent size is always accepted.
        TestReflection.Assert(
            !Guard(5_000, 4_000),
            "A small but valid output >= silent size must not be flagged."
        );

        // An empty output (0 bytes) is always zero-duration, regardless of silent size.
        TestReflection.Assert(Guard(0, 3_000_000), "A zero-byte output must be flagged.");
        TestReflection.Assert(
            Guard(0, 0),
            "A zero-byte output must be flagged even if silent size is unknown."
        );

        // When the silent size is unknown (0), only a truly empty output is rejected;
        // a non-empty output cannot be measured against a baseline so it is accepted.
        TestReflection.Assert(
            !Guard(200, 0),
            "With unknown silent size, a non-empty output must not be flagged."
        );

        // Exactly half the silent size is the boundary: strictly-less-than-half is the
        // rejection rule, so == half is accepted and just-under-half is rejected.
        TestReflection.Assert(
            !Guard(500, 1000),
            "Output exactly half the silent size must be accepted (boundary)."
        );
        TestReflection.Assert(
            Guard(499, 1000),
            "Output just under half the silent size must be flagged."
        );
    }

    private static bool Guard(long muxedSize, long silentSize)
    {
        var method =
            MuxerType.GetMethod(
                "IsLikelyZeroDurationOutput",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static
            )
            ?? throw new InvalidOperationException(
                "ReplayVideoAudioMuxer.IsLikelyZeroDurationOutput not found."
            );
        return (bool)(
            method.Invoke(null, new object[] { muxedSize, silentSize })
            ?? throw new InvalidOperationException("IsLikelyZeroDurationOutput returned null.")
        );
    }
}

// ---------------------------------------------------------------------------
// 6) ReplayVideoAudioMuxer.BuildArguments: multi-WAV capture must mix the base
//    bus audio and SFX bus audio into one AAC input via amix.
// ---------------------------------------------------------------------------
file static class MuxerArgumentTests
{
    private static readonly Type MuxerType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoAudioMuxer"
    );

    public static void Run()
    {
        MultiWavArgumentsUseAmix();
    }

    private static void MultiWavArgumentsUseAmix()
    {
        var args = BuildArguments(
            "silent recording.mp4",
            new[] { "base audio.wav", "sfx audio.wav" },
            "final output.mp4",
            192
        );

        TestReflection.Assert(
            args.Contains("-i \"silent recording.mp4\""),
            "Mux arguments should include the quoted video input."
        );
        TestReflection.Assert(
            args.Contains("-i \"base audio.wav\"") && args.Contains("-i \"sfx audio.wav\""),
            "Mux arguments should include both quoted WAV inputs."
        );
        TestReflection.Assert(
            args.Contains("[1:a][2:a]amix=inputs=2:normalize=0[aout]"),
            "Multi-WAV mux should mix audio inputs with amix normalize=0."
        );
        TestReflection.Assert(
            args.Contains("-map 0:v:0 -map \"[aout]\""),
            "Multi-WAV mux should map the amix output as the audio stream."
        );
        TestReflection.Assert(
            args.Contains("-c:v copy -c:a aac") && args.Contains("-b:a 192k"),
            "Mux arguments should keep video copy and AAC bitrate settings."
        );
    }

    private static string BuildArguments(
        string silentVideoTempPath,
        IReadOnlyList<string> wavPaths,
        string finalPath,
        int audioBitrateKbps
    )
    {
        var method =
            MuxerType.GetMethod(
                "BuildArguments",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[]
                {
                    typeof(string),
                    typeof(IReadOnlyList<string>),
                    typeof(string),
                    typeof(int),
                },
                modifiers: null
            )
            ?? throw new InvalidOperationException(
                "ReplayVideoAudioMuxer.BuildArguments(string,IReadOnlyList<string>,string,int) not found."
            );
        return (string)(
            method.Invoke(
                null,
                new object[] { silentVideoTempPath, wavPaths, finalPath, audioBitrateKbps }
            ) ?? throw new InvalidOperationException("BuildArguments returned null.")
        );
    }
}

// ---------------------------------------------------------------------------
// 7) ReplayVideoAudioMuxer debug stem naming: successful runtime muxes delete
//    temp WAVs, so debug builds preserve stable sibling copies for listening
//    to each captured bus before amix.
// ---------------------------------------------------------------------------
file static class MuxerDebugStemTests
{
    private static readonly Type MuxerType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoAudioMuxer"
    );

    public static void Run()
    {
        TwoCapturedStemsUseStableNames();
    }

    private static void TwoCapturedStemsUseStableNames()
    {
        var targets = BuildDebugStemCopyTargets(
            @"C:\replays\battle.20260530-104759.mp4",
            new[]
            {
                @"C:\replays\battle.20260530-104759.audio.wav",
                @"C:\replays\battle.20260530-104759.sfx.audio.wav",
            }
        );

        TestReflection.Assert(targets.Count == 2, "Two captured WAVs should have two debug stems.");
        TestReflection.Assert(
            targets[0] == @"C:\replays\battle.20260530-104759.debug.audio.wav",
            $"Base-bus debug stem path was {targets[0]}."
        );
        TestReflection.Assert(
            targets[1] == @"C:\replays\battle.20260530-104759.debug.sfx.wav",
            $"SFX-bus debug stem path was {targets[1]}."
        );
    }

    private static IReadOnlyList<string> BuildDebugStemCopyTargets(
        string finalPath,
        IReadOnlyList<string> wavPaths
    )
    {
        var method =
            MuxerType.GetMethod(
                "BuildDebugStemCopyTargets",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string), typeof(IReadOnlyList<string>) },
                modifiers: null
            )
            ?? throw new InvalidOperationException(
                "ReplayVideoAudioMuxer.BuildDebugStemCopyTargets(string,IReadOnlyList<string>) not found."
            );
        return (IReadOnlyList<string>)(
            method.Invoke(null, new object[] { finalPath, wavPaths })
            ?? throw new InvalidOperationException("BuildDebugStemCopyTargets returned null.")
        );
    }
}

// ---------------------------------------------------------------------------
// 8) ReplayVideoAudioTapPlan: a SINGLE all-inclusive stem tapped at the FMOD
//    CORE master. One stem captures every audible class (music, settlement, VO,
//    and the Resonance-decoded 3D SFX) and avoids the amix double-count that two
//    overlapping parent/child taps produced.
// ---------------------------------------------------------------------------
file static class AudioTapPlanTests
{
    private static readonly Type TapPlanType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoAudioTapPlan"
    );

    public static void Run()
    {
        DerivesSingleCoreMasterWavPath();
    }

    private static void DerivesSingleCoreMasterWavPath()
    {
        var paths = DeriveAudioWavPaths(@"C:\replays\battle.20260530-105445.recording.mp4");

        var expected = new[] { @"C:\replays\battle.20260530-105445.audio.wav" };

        TestReflection.Assert(
            paths.Count == expected.Length,
            $"Expected {expected.Length} audio tap paths, got {paths.Count}."
        );
        for (var i = 0; i < expected.Length; i++)
        {
            TestReflection.Assert(
                paths[i] == expected[i],
                $"Audio tap path {i} was {paths[i]}, expected {expected[i]}."
            );
        }
    }

    private static IReadOnlyList<string> DeriveAudioWavPaths(string tempVideoPath)
    {
        var method =
            TapPlanType.GetMethod(
                "DeriveAudioWavPaths",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null
            )
            ?? throw new InvalidOperationException(
                "ReplayVideoAudioTapPlan.DeriveAudioWavPaths(string) not found."
            );
        return (IReadOnlyList<string>)(
            method.Invoke(null, new object[] { tempVideoPath })
            ?? throw new InvalidOperationException("DeriveAudioWavPaths returned null.")
        );
    }
}

// ---------------------------------------------------------------------------
// Shared reflection helpers (file-scoped types cannot see top-level locals).
// ---------------------------------------------------------------------------
file static class TestReflection
{
    public static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static Type RequireType(string fullName) =>
        Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");

    public static object? GetProp(Type type, object instance, string name)
    {
        var prop =
            type.GetProperty(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            ) ?? throw new InvalidOperationException($"Property not found: {type.FullName}.{name}");
        return prop.GetValue(instance);
    }

    public static object? GetField(Type type, object instance, string name)
    {
        var field =
            type.GetField(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            ) ?? throw new InvalidOperationException($"Field not found: {type.FullName}.{name}");
        return field.GetValue(instance);
    }

    public static object? Invoke(
        Type type,
        object instance,
        string name,
        Type[] paramTypes,
        object[] args
    )
    {
        var method =
            type.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: paramTypes,
                modifiers: null
            )
            ?? throw new InvalidOperationException(
                $"Method not found: {type.FullName}.{name}({paramTypes.Length} args)"
            );
        return method.Invoke(instance, args);
    }

    // AudioRingBuffer has two Write overloads; resolve the managed float[] one explicitly.
    public static void InvokeManagedWrite(
        Type type,
        object instance,
        float[] src,
        int offset,
        int count
    )
    {
        var method =
            type.GetMethod(
                "Write",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(float[]), typeof(int), typeof(int) },
                modifiers: null
            )
            ?? throw new InvalidOperationException(
                "AudioRingBuffer.Write(float[],int,int) not found."
            );
        method.Invoke(instance, new object[] { src, offset, count });
    }
}
