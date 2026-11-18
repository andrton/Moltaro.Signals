using System.Diagnostics;

namespace Moltaro.Signals.Measurements;

// The in-process set to wake: the time from a SetAsync to the first line of the woken member's code, which runs on the
// thread pool, judged at p50 under 50 us (PQ2); and the bytes allocated per wake of a waiting member, on every thread.
// The publisher sets only once the member waits, so every set wakes it.
internal static class SetToWake
{
    private const int Wakes = 100_000;

    public static async Task RunAsync()
    {
        await using SignalHost host = InProcessHost.Create();
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null);

        // Allocated before the measured run, so that its allocations are those of the wakes.
        var setAt = new long[Wakes];
        var wokenAt = new long[Wakes];

        await WakeAsync(host, lane, setAt, wokenAt);

        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        await WakeAsync(host, lane, setAt, wokenAt);
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        long[] latencies = new long[Wakes];

        for (int wake = 0; wake < Wakes; wake++)
        {
            latencies[wake] = wokenAt[wake] - setAt[wake];
        }

        Array.Sort(latencies);

        double p50 = Microseconds(Percentile(latencies, 0.50));
        double p99 = Microseconds(Percentile(latencies, 0.99));
        double maximum = Microseconds(latencies[^1]);

        Console.WriteLine(
            $"Set to wake over {Wakes:N0} wakes: p50 {p50:F1} us, p99 {p99:F1} us, maximum {maximum:F1} us (target: p50 under 50 us)");
        Console.WriteLine($"Allocated per wake of a waiting member: {allocated / (double)Wakes:F0} bytes");
    }

    // The member arms its wait, then lets the publisher set. The member continues on the thread pool, the publisher
    // runs on a thread of its own.
    private static async Task WakeAsync(SignalHost host, ISignalSubscription lane, long[] setAt, long[] wokenAt)
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
                await host.SetAsync("jobs");
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
