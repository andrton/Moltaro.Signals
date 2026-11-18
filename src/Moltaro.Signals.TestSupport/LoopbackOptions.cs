using System.Net;

namespace Moltaro.Signals.TestSupport;

// Options that pass every check, for tests whose subject is not the options: a host on a free port of the loopback
// address, and clients that send the same token. Each call returns a new object, so a test may change it.
public static class LoopbackOptions
{
    public const string Token = "test-token";

    public static SignalHostOptions Host()
    {
        return new SignalHostOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            Token = Token,
        };
    }

    public static SignalClientOptions Client(EndPoint hostEndPoint)
    {
        return new SignalClientOptions
        {
            HostEndPoint = hostEndPoint,
            Token = Token,
        };
    }
}
