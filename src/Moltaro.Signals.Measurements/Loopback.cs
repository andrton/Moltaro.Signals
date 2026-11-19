using System.Net;

namespace Moltaro.Signals.Measurements;

// The host and the clients of the scenarios over loopback, all in this process: a host that listens on a free port of
// the loopback address, and clients of it, each returned once it is connected.
internal static class Loopback
{
    public const string Token = "measurements";

    public static SignalHost StartHost()
    {
        var options = new SignalHostOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            Token = Token,
        };

        var host = new SignalHost(options);
        host.Start();

        return host;
    }

    public static async Task<SignalClient> ConnectAsync(SignalHost host)
    {
        var options = new SignalClientOptions
        {
            HostEndPoint = host.LocalEndPoint,
            Token = Token,
        };

        var client = new SignalClient(options);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        client.StateChanged += (_, change) =>
        {
            if (change.State == SignalConnectionStateEnum.Connected)
            {
                connected.TrySetResult();
            }
        };

        client.Start();
        await connected.Task;

        return client;
    }
}
