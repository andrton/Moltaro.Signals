using System.Net;

namespace Moltaro.Signals.Samples;

// A host in a process of its own, for a deployment in which no application process should own it. "dotnet run --
// host" runs it until Ctrl+C.
internal static class StandaloneHost
{
    // Runs the host until the token is cancelled.
    public static async Task RunAsync(IPEndPoint listenEndPoint, CancellationToken cancellationToken)
    {
        var options = new SignalHostOptions
        {
            // An address of the internal network serves other machines; the loopback address only this one.
            ListenEndPoint = listenEndPoint,

            // Shared with every client. A real host reads it from its configuration, never from its code.
            Token = "sample-token",
        };

        await using var host = new SignalHost(options);

        Console.WriteLine($"The host starts on {listenEndPoint}.");
        host.Start();
        Console.WriteLine($"The host listens on {host.LocalEndPoint}.");

        // The cancellation is the expected end, not a failure.
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        Console.WriteLine("The host stops.");
    }
}
