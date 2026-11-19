using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Hosting.Tests.GenericHost;

// AddSignalHost in a container and in a generic host: what it registers, when the port is bound, when the host is
// disposed, and its log line.
public sealed class HostRegistrationTests
{
    private static IPEndPoint AnyLoopbackPort => new(IPAddress.Loopback, 0);

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task ISignals_resolves_to_the_registered_host()
    {
        var services = new ServiceCollection();
        services.AddSignalHost(options =>
        {
            options.ListenEndPoint = AnyLoopbackPort;
            options.Token = LoopbackOptions.Token;
        });
        await using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<SignalHost>(), provider.GetRequiredService<ISignals>());
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Configure_runs_when_the_host_is_first_resolved_and_wrong_options_throw_there()
    {
        var services = new ServiceCollection();
        bool configured = false;

        // Leaves ListenEndPoint unset, which the constructor of the host refuses.
        services.AddSignalHost(options =>
        {
            configured = true;
            options.Token = LoopbackOptions.Token;
        });
        bool configuredByTheCall = configured;
        await using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Throws<ArgumentException>("ListenEndPoint", () => provider.GetRequiredService<SignalHost>());
        Assert.False(configuredByTheCall);
        Assert.True(configured);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_hosted_service_registered_after_AddSignalHost_finds_the_port_bound_when_it_starts()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        HostApplicationBuilder builder = CreateBuilder(AnyLoopbackPort);
        builder.Services.AddSingleton<EndPointProbe>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<EndPointProbe>());
        using IHost app = builder.Build();

        await app.StartAsync(cancellationToken);

        Assert.NotNull(app.Services.GetRequiredService<EndPointProbe>().LocalEndPointAtStart);

        await app.StopAsync(cancellationToken);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_port_that_cannot_be_bound_fails_the_start_of_the_application()
    {
        using var occupant = new TcpListener(IPAddress.Loopback, 0);
        occupant.Start();
        using IHost app = CreateBuilder((IPEndPoint)occupant.LocalEndpoint).Build();

        await Assert.ThrowsAsync<SocketException>(() => app.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_host_outlives_the_hosted_services_and_is_disposed_with_the_container()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IHost app = CreateBuilder(AnyLoopbackPort).Build();
        await app.StartAsync(cancellationToken);
        SignalHost host = app.Services.GetRequiredService<SignalHost>();

        await app.StopAsync(cancellationToken);
        SignalConnectionStateEnum afterStop = host.State;
        app.Dispose();

        Assert.Equal(SignalConnectionStateEnum.Connected, afterStop);
        Assert.Equal(SignalConnectionStateEnum.Disconnected, host.State);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_host_writes_an_Information_line_with_LocalEndPoint_when_it_listens()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var recorder = new LogRecorder();
        HostApplicationBuilder builder = CreateBuilder(AnyLoopbackPort);
        builder.Logging.AddProvider(recorder);
        using IHost app = builder.Build();

        await app.StartAsync(cancellationToken);
        LogLine line = await recorder.WaitForLineAsync("Moltaro.Signals.SignalHost", LogLevel.Information, cancellationToken);

        IPEndPoint localEndPoint = app.Services.GetRequiredService<SignalHost>().LocalEndPoint!;
        Assert.Equal(localEndPoint.ToString(), Assert.Contains("LocalEndPoint", line.Values)?.ToString());

        await app.StopAsync(cancellationToken);
    }

    // A generic host with nothing but AddSignalHost: no configuration sources and no log providers of its own.
    private static HostApplicationBuilder CreateBuilder(IPEndPoint listenEndPoint)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(settings: null);

        builder.Services.AddSignalHost(options =>
        {
            options.ListenEndPoint = listenEndPoint;
            options.Token = LoopbackOptions.Token;
        });

        return builder;
    }
}
