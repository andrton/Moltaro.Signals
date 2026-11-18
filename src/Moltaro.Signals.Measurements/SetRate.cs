using System.Diagnostics;

namespace Moltaro.Signals.Measurements;

// Sets per second in-process, most of them coalesced, as the owner answered PQ2. Each publishing thread calls SetAsync
// on ten single-segment keys of its own, each with one group whose member waits again only after a hundred sets on its
// key, so that it wakes for at most one set in a hundred. One thread is judged against 100,000 sets per second;
// several threads, setting on one host at the same time, are reported only. The rate is counted over a fixed time
// after a warm-up.
internal static class SetRate
{
    private const int KeysPerThread = 10;

    private const int SetsPerPass = 100;

    private static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan Measured = TimeSpan.FromSeconds(3);

    public static async Task RunOneThreadAsync()
    {
        double rate = await MeasureAsync(threadCount: 1);

        Console.WriteLine($"1 thread: {rate:N0} sets per second (target: at least 100,000)");
    }

    public static async Task RunSeveralThreadsAsync()
    {
        int[] threadCounts = [2, 4, 8];

        foreach (int threadCount in threadCounts)
        {
            double rate = await MeasureAsync(threadCount);

            Console.WriteLine(
                $"{threadCount} threads: {rate:N0} sets per second in total, on {Environment.ProcessorCount} logical processors");
        }
    }

    private static async Task<double> MeasureAsync(int threadCount)
    {
        await using SignalHost host = InProcessHost.Create();
        using var stopMembers = new CancellationTokenSource();
        var keys = new string[threadCount][];
        var nextPasses = new SemaphoreSlim[threadCount][];
        var lanes = new List<ISignalSubscription>();
        var members = new List<Task>();

        for (int thread = 0; thread < threadCount; thread++)
        {
            keys[thread] = new string[KeysPerThread];
            nextPasses[thread] = new SemaphoreSlim[KeysPerThread];

            for (int index = 0; index < KeysPerThread; index++)
            {
                string key = $"thread{thread}key{index}";
                ISignalSubscription lane = host.Subscribe(key, "workers");
                var nextPass = new SemaphoreSlim(0);

                keys[thread][index] = key;
                nextPasses[thread][index] = nextPass;
                lanes.Add(lane);
                members.Add(PassesAsync(lane, nextPass, stopMembers.Token));
            }
        }

        long warmUpEnd = Stopwatch.GetTimestamp() + Ticks(WarmUp);
        long measureEnd = warmUpEnd + Ticks(Measured);
        var measuredSets = new long[threadCount];
        var publishers = new Task[threadCount];

        for (int thread = 0; thread < threadCount; thread++)
        {
            int own = thread;

            publishers[own] = OwnThread.RunAsync(async () =>
                measuredSets[own] = await PublishAsync(host, keys[own], nextPasses[own], warmUpEnd, measureEnd));
        }

        await Task.WhenAll(publishers);
        await stopMembers.CancelAsync();
        await Task.WhenAll(members);

        foreach (ISignalSubscription lane in lanes)
        {
            lane.Dispose();
        }

        foreach (SemaphoreSlim nextPass in nextPasses.SelectMany(passes => passes))
        {
            nextPass.Dispose();
        }

        return measuredSets.Sum() / Measured.TotalSeconds;
    }

    // Sets the keys in turn until the measured time is over, and returns the sets made in it. After every hundred sets
    // on each key it lets the members that have finished their pass wait again.
    private static async Task<long> PublishAsync(
        SignalHost host,
        string[] keys,
        SemaphoreSlim[] nextPasses,
        long warmUpEnd,
        long measureEnd)
    {
        long measuredSets = 0;
        int rounds = 0;

        while (true)
        {
            long now = Stopwatch.GetTimestamp();

            if (now >= measureEnd)
            {
                return measuredSets;
            }

            foreach (string key in keys)
            {
                await host.SetAsync(key);
            }

            if (now >= warmUpEnd)
            {
                measuredSets += keys.Length;
            }

            rounds++;

            if (rounds == SetsPerPass)
            {
                rounds = 0;

                foreach (SemaphoreSlim nextPass in nextPasses)
                {
                    // A member that is still in its pass is not given a second one.
                    if (nextPass.CurrentCount == 0)
                    {
                        nextPass.Release();
                    }
                }
            }
        }
    }

    // A member: one pass for each signal, then a wait for the publisher's permission to wait again.
    private static async Task PassesAsync(ISignalSubscription lane, SemaphoreSlim nextPass, CancellationToken stop)
    {
        try
        {
            while (true)
            {
                await lane.WaitAsync(until: null, stop);
                await nextPass.WaitAsync(stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    private static long Ticks(TimeSpan duration)
    {
        return (long)(duration.TotalSeconds * Stopwatch.Frequency);
    }
}
