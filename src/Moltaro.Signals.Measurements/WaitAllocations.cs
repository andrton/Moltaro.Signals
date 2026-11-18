namespace Moltaro.Signals.Measurements;

// The bytes a wait allocates from the moment it is armed until a set wakes it: without a deadline or a token, and
// with a deadline and a cancellable token, which add the deadline's timer and the token's registration. A set on the
// same thread wakes each wait, so awaiting it completes at once.
internal static class WaitAllocations
{
    private const int Waits = 100_000;

    public static async Task RunAsync()
    {
        await using SignalHost host = InProcessHost.Create();
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");
        using var cancellation = new CancellationTokenSource();
        await lane.WaitAsync(until: null);

        // Far enough away that no deadline comes during the run.
        DateTimeOffset deadline = TimeProvider.System.GetUtcNow().AddHours(1);

        double plain = await BytesPerWaitAsync(host, lane, until: null, CancellationToken.None);
        double bounded = await BytesPerWaitAsync(host, lane, deadline, cancellation.Token);

        Console.WriteLine($"Allocated per wait without a deadline or a token: {plain:F0} bytes");
        Console.WriteLine($"Allocated per wait with a deadline and a cancellable token: {bounded:F0} bytes");
    }

    // The first run warms up and creates what a subscription keeps from one wait to the next.
    private static async Task<double> BytesPerWaitAsync(
        SignalHost host,
        ISignalSubscription lane,
        DateTimeOffset? until,
        CancellationToken cancellationToken)
    {
        await WaitAndSetAsync(host, lane, until, cancellationToken);

        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        await WaitAndSetAsync(host, lane, until, cancellationToken);
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        return allocated / (double)Waits;
    }

    private static async Task WaitAndSetAsync(
        SignalHost host,
        ISignalSubscription lane,
        DateTimeOffset? until,
        CancellationToken cancellationToken)
    {
        for (int wait = 0; wait < Waits; wait++)
        {
            ValueTask<SignalWaitResultEnum> armed = lane.WaitAsync(until, cancellationToken);
            await host.SetAsync("jobs");
            await armed;
        }
    }
}
