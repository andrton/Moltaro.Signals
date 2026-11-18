using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.HostOverTcp;

// A started host on a free port of the loopback address, whose time moves only when a test advances its clock: no
// timer of the host fires by itself, so no connection is closed for silence or for a missing HELLO while a test runs.
internal static class LoopbackHost
{
    public static SignalHost Start(FakeClock clock, Action<SignalHostOptions>? configure = null)
    {
        SignalHostOptions options = LoopbackOptions.Host();
        options.TimeProvider = clock;
        configure?.Invoke(options);

        var host = new SignalHost(options);
        host.Start();

        return host;
    }
}
