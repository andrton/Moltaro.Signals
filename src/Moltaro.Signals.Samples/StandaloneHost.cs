using System.Net;
using System.Runtime.InteropServices;

namespace Moltaro.Signals.Samples;

// A host in a process of its own, for a deployment in which no application process should own it. RunAsync is all
// that such a program needs; "dotnet run -- host" runs it until Ctrl+C.
internal static class StandaloneHost
{
    // Runs the host until Ctrl+C, or until the token is cancelled.
    public static async Task RunAsync(IPEndPoint listenEndPoint, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Ctrl+C ends the wait below instead of the process, so that the host is disposed and closes its connections.
        using var ctrlC = PosixSignalRegistration.Create(PosixSignal.SIGINT, signal =>
        {
            signal.Cancel = true;
            stop.Cancel();
        });

        await using var host = new SignalHost(new SignalHostOptions
        {
            // An address of the internal network serves other machines; the loopback address only this one.
            ListenEndPoint = listenEndPoint,

            // Shared with every client. A real host reads it from its configuration, never from its code.
            Token = "sample-token",
        });

        host.Start();
        Console.WriteLine($"The host listens on {host.LocalEndPoint}.");

        // The cancellation is the expected end, not a failure.
        await Task.Delay(Timeout.Infinite, stop.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        Console.WriteLine("The host stops.");
    }
}
