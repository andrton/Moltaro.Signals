using System.Net;

namespace Moltaro.Signals.Samples;

// The process that owns the background work embeds the host. Two lanes of its worker compete in the group "workers"
// on the key "jobs", and a publisher of the same process stores jobs and sets the key. Nothing here needs a socket.
internal static class InProcessLanes
{
    private const int JobCount = 3;

    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        var options = new SignalHostOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            Token = "sample-token",
        };

        await using var host = new SignalHost(options);
        var store = new JobStore();

        // Only so that the sample ends: counts the jobs the lanes have processed.
        using var processed = new SemaphoreSlim(0);
        using var stopLanes = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Console.WriteLine("Two lanes join the group \"workers\" on the key \"jobs\".");
        using ISignalSubscription lane1 = host.Subscribe("jobs", "workers");
        using ISignalSubscription lane2 = host.Subscribe("jobs", "workers");

        Task[] lanes =
        [
            RunLaneAsync("Lane 1", lane1, store, processed, stopLanes.Token),
            RunLaneAsync("Lane 2", lane2, store, processed, stopLanes.Token),
        ];

        for (int number = 1; number <= JobCount; number++)
        {
            // After the transaction that stored the job has committed, never inside it.
            store.Add($"job {number}");
            SignalSetResult result = await host.SetAsync("jobs", cancellationToken);
            Console.WriteLine($"The publisher stored job {number} and set \"jobs\": {result.Status}, {result.Groups} group(s).");
        }

        for (int job = 0; job < JobCount; job++)
        {
            await processed.WaitAsync(cancellationToken);
        }

        await stopLanes.CancelAsync();
        await Task.WhenAll(lanes);
    }

    // The consumer loop of one lane, until the token is cancelled.
    private static async Task RunLaneAsync(
        string name,
        ISignalSubscription lane,
        JobStore store,
        SemaphoreSlim processed,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                SignalWaitResultEnum result = await lane.WaitAsync(until: null, cancellationToken);

                // Every result is followed by one full pass over the storage.
                while (store.TryClaim(out string? job))
                {
                    Console.WriteLine($"{name} ({result}) processes {job}.");
                    processed.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
