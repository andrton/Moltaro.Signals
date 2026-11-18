using System.Diagnostics;

namespace Moltaro.Signals.Measurements;

// What the ancestors of a key cost a set (decision O7): sets of a key of three segments whose only members are on its
// first segment, against sets of a key of one segment with a member of its own. The members are busy, so every set
// after the first finds a signal pending, as most sets do.
internal static class AncestorSets
{
    private const int Sets = 2_000_000;

    public static async Task RunAsync()
    {
        await using SignalHost host = InProcessHost.Create();
        using ISignalSubscription onJobs = host.Subscribe("jobs", "workers");
        using ISignalSubscription onEntity = host.Subscribe("entity", "cache");

        await ReportAsync(host, "jobs", "One segment, a member on the key");
        await ReportAsync(host, "entity.contacts.42", "Three segments, members on the first only");
    }

    // The first run warms up.
    private static async Task ReportAsync(SignalHost host, string key, string label)
    {
        await SetManyAsync(host, key);

        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        long started = Stopwatch.GetTimestamp();
        await SetManyAsync(host, key);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        Console.WriteLine(
            $"{label}: {elapsed.TotalNanoseconds / Sets:F0} ns per set, {allocated / (double)Sets:F1} bytes allocated per set");
    }

    private static async Task SetManyAsync(SignalHost host, string key)
    {
        for (int set = 0; set < Sets; set++)
        {
            await host.SetAsync(key);
        }
    }
}
