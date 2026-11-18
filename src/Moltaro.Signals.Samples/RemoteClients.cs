using System.Net;

namespace Moltaro.Signals.Samples;

// A host and two clients over loopback: a worker with one lane and a publisher. In a deployment each lives in a
// process of its own; here they share one, so that the sample runs on its own. The host restarts in the middle: the
// publisher's set meanwhile answers Unavailable, and the lane does one full pass with Joined once its client has
// connected to the new host.
internal static class RemoteClients
{
    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("The host starts on a free port of the loopback address.");
        await using var firstHost = new SignalHost(HostOptions(new IPEndPoint(IPAddress.Loopback, 0)));
        firstHost.Start();
        IPEndPoint endPoint = firstHost.LocalEndPoint!;
        Console.WriteLine($"The host listens on {endPoint}.");

        await using var worker = new SignalClient(ClientOptions(endPoint));
        await using var publisher = new SignalClient(ClientOptions(endPoint));
        worker.Start();
        publisher.Start();

        using ISignalSubscription lane = worker.Subscribe("jobs", "workers");
        Console.WriteLine($"The worker's lane: {await lane.WaitAsync(until: null, cancellationToken)}.");

        await WhenConnectedAsync(publisher, cancellationToken);
        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null, cancellationToken);
        SignalSetResult result = await publisher.SetAsync("jobs", cancellationToken);
        Console.WriteLine($"The publisher sets \"jobs\": {result.Status}, {result.Groups} group(s).");
        Console.WriteLine($"The worker's lane: {await wait}.");

        Console.WriteLine("The host restarts on the same port.");
        wait = lane.WaitAsync(until: null, cancellationToken);
        await firstHost.DisposeAsync();

        result = await publisher.SetAsync("jobs", cancellationToken);
        Console.WriteLine($"While no host listens, the publisher sets \"jobs\": {result.Status}.");

        await using var secondHost = new SignalHost(HostOptions(endPoint));
        secondHost.Start();
        Console.WriteLine($"The worker's lane: {await wait}.");

        await WhenConnectedAsync(publisher, cancellationToken);
        result = await publisher.SetAsync("jobs", cancellationToken);
        Console.WriteLine($"The publisher sets \"jobs\": {result.Status}, {result.Groups} group(s).");
    }

    private static SignalHostOptions HostOptions(IPEndPoint listenEndPoint)
    {
        return new SignalHostOptions
        {
            ListenEndPoint = listenEndPoint,
            Token = "sample-token",
        };
    }

    private static SignalClientOptions ClientOptions(IPEndPoint hostEndPoint)
    {
        return new SignalClientOptions
        {
            HostEndPoint = hostEndPoint,
            Token = "sample-token",
        };
    }

    // Completes once the client is connected. The handler is attached before State is read, so no change is missed.
    private static async Task WhenConnectedAsync(SignalClient client, CancellationToken cancellationToken)
    {
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnStateChanged(object? sender, SignalConnectionChangedEventArgs change)
        {
            if (change.State == SignalConnectionStateEnum.Connected)
            {
                connected.TrySetResult();
            }
        }

        client.StateChanged += OnStateChanged;

        try
        {
            if (client.State != SignalConnectionStateEnum.Connected)
            {
                await connected.Task.WaitAsync(cancellationToken);
            }
        }
        finally
        {
            client.StateChanged -= OnStateChanged;
        }
    }
}
