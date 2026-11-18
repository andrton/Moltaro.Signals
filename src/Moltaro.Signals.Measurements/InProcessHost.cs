using System.Net;

namespace Moltaro.Signals.Measurements;

// The host whose in-process ISignals a scenario measures. It is never started: signal delivery needs no port.
internal static class InProcessHost
{
    public static SignalHost Create()
    {
        var options = new SignalHostOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            Token = "measurements",
        };

        return new SignalHost(options);
    }
}
