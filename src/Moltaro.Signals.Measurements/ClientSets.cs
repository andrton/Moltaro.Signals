using System.Diagnostics;

namespace Moltaro.Signals.Measurements;

// The sets of one SignalClient over loopback, reported only (PQ2): the sets per second when several callers share the
// client, each awaiting its set before it makes the next, and the bytes allocated in the process per set, the host's
// included; with one caller, the bytes per remote SetAsync. The one member of the key is on the host and never waits,
// so every set after the first finds a signal pending, as most sets do. The rate is counted over a fixed time after a
// warm-up.
internal static class ClientSets
{
    private static readonly int[] CallerCounts = [1, 8, 64];

    private static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan Measured = TimeSpan.FromSeconds(3);

    public static async Task RunAsync()
    {
        await using SignalHost host = Loopback.StartHost();
        using ISignalSubscription busy = host.Subscribe("jobs", "workers");
        await using SignalClient client = await Loopback.ConnectAsync(host);

        foreach (int callerCount in CallerCounts)
        {
            await SetUntilAsync(client, callerCount, WarmUp);

            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            long started = Stopwatch.GetTimestamp();
            long sets = await SetUntilAsync(client, callerCount, Measured);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

            string callers = callerCount == 1 ? "1 caller" : $"{callerCount} callers";

            Console.WriteLine(
                $"{callers}: {sets / elapsed.TotalSeconds:N0} sets per second, {allocated / (double)sets:F1} bytes allocated per set (reported, not judged)");
        }
    }

    // Lets every caller set until the time is over, and returns the sets they made in it.
    private static async Task<long> SetUntilAsync(SignalClient client, int callerCount, TimeSpan duration)
    {
        long end = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        var callers = new Task<long>[callerCount];

        for (int caller = 0; caller < callerCount; caller++)
        {
            callers[caller] = Task.Run(() => CallerAsync(client, end));
        }

        long[] sets = await Task.WhenAll(callers);

        return sets.Sum();
    }

    // A set that the host did not apply would make the rate a rate of Unavailable.
    private static async Task<long> CallerAsync(SignalClient client, long end)
    {
        long sets = 0;

        while (Stopwatch.GetTimestamp() < end)
        {
            SignalSetResult result = await client.SetAsync("jobs");

            if (result.Status != SignalSetStatusEnum.Applied)
            {
                throw new InvalidOperationException("The client lost its connection during the measurement.");
            }

            sets++;
        }

        return sets;
    }
}
