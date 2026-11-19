using System.Net;
using Moltaro.Signals.Samples;

// "dotnet run -- host" runs the standalone host until Ctrl+C. Without arguments every sample runs once, in order, and
// says where it stops while a member it uses is not implemented yet.
if (args is ["host"])
{
    await RunSampleAsync(
        "A standalone host, until Ctrl+C",
        cancellationToken => StandaloneHost.RunAsync(new IPEndPoint(IPAddress.Loopback, 7420), cancellationToken),
        CancellationToken.None);

    return;
}

// In the run of every sample the standalone host stops as soon as it listens.
await RunSampleAsync(
    "A standalone host",
    cancellationToken => StandaloneHost.RunAsync(new IPEndPoint(IPAddress.Loopback, 0), cancellationToken),
    new CancellationToken(canceled: true));

await RunSampleAsync(
    "Two lanes of one group and a publisher in the host process",
    InProcessLanes.RunAsync,
    CancellationToken.None);

await RunSampleAsync(
    "A host with remote clients over loopback, and a host restart",
    RemoteClients.RunAsync,
    CancellationToken.None);

await RunSampleAsync(
    "A generic host with AddSignalHost and a worker",
    GenericHostWithHost.RunAsync,
    CancellationToken.None);

await RunSampleAsync(
    "A generic host with AddSignalClient and a worker",
    GenericHostWithClient.RunAsync,
    CancellationToken.None);

static async Task RunSampleAsync(string title, Func<CancellationToken, Task> sample, CancellationToken cancellationToken)
{
    Console.WriteLine();
    Console.WriteLine($"== {title}");

    try
    {
        await sample(cancellationToken);
    }
    catch (NotSupportedException)
    {
        // The contract exists before its implementation: a member that is not implemented yet refuses. The last line
        // the sample wrote says how far it came.
        Console.WriteLine("Stops here: not implemented yet.");
    }
}
