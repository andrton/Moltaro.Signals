namespace Moltaro.Signals.Measurements;

// The memory the host retains per (key, group) and per subscription, and that it stays flat over a million sets:
// memory grows with keys x groups and subscriptions only (concept section 11). Each (key, group) is a key of its own
// with one group; the subscriptions do not wait, so every set after the first on a key finds a signal pending.
internal static class RetainedMemory
{
    private const int Pairs = 10_000;

    private const int Sets = 1_000_000;

    public static async Task RunAsync()
    {
        await using SignalHost host = InProcessHost.Create();

        // Created before the first figure, so that the figures are what the host retains.
        string[] keys = [.. Enumerable.Range(0, Pairs).Select(pair => $"key{pair}")];
        var firstMembers = new ISignalSubscription[Pairs];
        var secondMembers = new ISignalSubscription[Pairs];

        long empty = Retained();

        for (int pair = 0; pair < Pairs; pair++)
        {
            firstMembers[pair] = host.Subscribe(keys[pair], "workers");
        }

        long withFirstMembers = Retained();

        for (int pair = 0; pair < Pairs; pair++)
        {
            secondMembers[pair] = host.Subscribe(keys[pair], "workers");
        }

        long withSecondMembers = Retained();

        for (int set = 0; set < Sets; set++)
        {
            await host.SetAsync(keys[set % Pairs]);
        }

        long afterSets = Retained();

        double perSubscription = (withSecondMembers - withFirstMembers) / (double)Pairs;
        double perPair = ((withFirstMembers - empty) / (double)Pairs) - perSubscription;

        Console.WriteLine($"Retained per (key, group), without its subscriptions: {perPair:F0} bytes");
        Console.WriteLine($"Retained per subscription: {perSubscription:F0} bytes");
        Console.WriteLine($"Change over {Sets:N0} sets: {afterSets - withSecondMembers:N0} bytes in total");

        foreach (ISignalSubscription member in firstMembers.Concat(secondMembers))
        {
            member.Dispose();
        }
    }

    private static long Retained()
    {
        return GC.GetTotalMemory(forceFullCollection: true);
    }
}
