using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Hosting.Tests.GenericHost;

// AddSignalHost and AddSignalClient in one container, which serves one ISignals.
public sealed class HostAndClientRegistrationTests
{
    [Fact]
    public async Task Registering_both_leaves_ISignals_resolving_to_the_one_registered_last()
    {
        // The second call is chained on the collection the first returned, as an application writes it.
        var hostThenClient = new ServiceCollection();
        hostThenClient.AddSignalHost(ConfigureHost).AddSignalClient(ConfigureClient);
        var clientThenHost = new ServiceCollection();
        clientThenHost.AddSignalClient(ConfigureClient).AddSignalHost(ConfigureHost);
        await using ServiceProvider clientLast = hostThenClient.BuildServiceProvider();
        await using ServiceProvider hostLast = clientThenHost.BuildServiceProvider();

        Assert.IsType<SignalClient>(clientLast.GetRequiredService<ISignals>());
        Assert.IsType<SignalHost>(hostLast.GetRequiredService<ISignals>());
    }

    private static void ConfigureHost(SignalHostOptions options)
    {
        options.ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        options.Token = LoopbackOptions.Token;
    }

    private static void ConfigureClient(SignalClientOptions options)
    {
        options.HostEndPoint = new IPEndPoint(IPAddress.Loopback, 5000);
        options.Token = LoopbackOptions.Token;
    }
}
