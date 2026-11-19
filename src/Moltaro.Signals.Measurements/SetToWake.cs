using System.Diagnostics;

namespace Moltaro.Signals.Measurements;

// The set to wake: the time from a SetAsync to the first line of the woken member's code, which runs on the thread
// pool, and the bytes allocated per wake of a waiting member, on every thread of the process. In-process it is judged
// at p50 under 50 us; over loopback, from a client to a member on another client of a host in the same process, at p99
// under 1 ms (concept section 11). The publisher sets only once the member has asked to wait, so every set wakes it.
// Over loopback the member's WAIT and the publisher's SET travel on two connections, so the host may apply the set
// first and leave the signal pending for the WAIT; either way the set wakes the member.
internal static class SetToWake
{
    private const int InProcessWakes = 100_000;

    private const int LoopbackWakes = 50_000;

    public static async Task RunInProcessAsync()
    {
        await using SignalHost host = InProcessHost.Create();
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null);

        (double p50, double p99, double maximum, double bytesPerWake) =
            await MeasureAsync(lane, () => host.SetAsync("jobs"), InProcessWakes);

        Console.WriteLine(
            $"Set to wake over {InProcessWakes:N0} wakes: p50 {p50:F1} us, p99 {p99:F1} us, maximum {maximum:F1} us (target: p50 under 50 us)");
        Console.WriteLine($"Allocated per wake of a waiting member: {bytesPerWake:F0} bytes");
    }

    public static async Task RunOverLoopbackAsync()
    {
        await using SignalHost host = Loopback.StartHost();
        await using SignalClient publisher = await Loopback.ConnectAsync(host);
        await using SignalClient member = await Loopback.ConnectAsync(host);
        using ISignalSubscription lane = member.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null);

        (double p50, double p99, double maximum, double bytesPerWake) =
            await MeasureAsync(lane, () => publisher.SetAsync("jobs"), LoopbackWakes);

        Console.WriteLine(
            $"Set to wake over loopback, {LoopbackWakes:N0} wakes: p50 {p50:F1} us, p99 {p99:F1} us, maximum {maximum:F1} us (target: p99 under 1 ms)");
        Console.WriteLine($"Allocated per remote wake, with its set and the next wait, the host's included: {bytesPerWake:F0} bytes");
    }

    // A warm-up run, then the measured run. The latencies are in microseconds.
    private static async Task<(double P50, double P99, double Maximum, double BytesPerWake)> MeasureAsync(
        ISignalSubscription lane,
        Func<ValueTask<SignalSetResult>> set,
        int wakes)
    {
        // Allocated before the measured run, so that its allocations are those of the wakes.
        var setAt = new long[wakes];
        var wokenAt = new long[wakes];

        await WakeAsync(lane, set, setAt, wokenAt);

        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        await WakeAsync(lane, set, setAt, wokenAt);
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        long[] latencies = new long[wakes];

        for (int wake = 0; wake < wakes; wake++)
        {
            latencies[wake] = wokenAt[wake] - setAt[wake];
        }

        Array.Sort(latencies);

        return (
            Microseconds(Percentile(latencies, 0.50)),
            Microseconds(Percentile(latencies, 0.99)),
            Microseconds(latencies[^1]),
            allocated / (double)wakes);
    }

    // The member arms its wait, then lets the publisher set. The member continues on the thread pool, the publisher
    // starts on a thread of its own and waits for the member without allocating.
    private static async Task WakeAsync(
        ISignalSubscription lane,
        Func<ValueTask<SignalSetResult>> set,
        long[] setAt,
        long[] wokenAt)
    {
        using var armed = new SemaphoreSlim(0);

        Task member = Task.Run(async () =>
        {
            for (int wake = 0; wake < wokenAt.Length; wake++)
            {
                ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null);
                armed.Release();
                await wait;
                wokenAt[wake] = Stopwatch.GetTimestamp();
            }
        });

        await OwnThread.RunAsync(async () =>
        {
            for (int wake = 0; wake < setAt.Length; wake++)
            {
                armed.Wait();
                setAt[wake] = Stopwatch.GetTimestamp();
                await set();
            }
        });

        await member;
    }

    // The nearest-rank percentile of sorted values.
    private static long Percentile(long[] sorted, double fraction)
    {
        int rank = (int)Math.Ceiling(fraction * sorted.Length);

        return sorted[rank - 1];
    }

    private static double Microseconds(long ticks)
    {
        return ticks * 1_000_000.0 / Stopwatch.Frequency;
    }
}
