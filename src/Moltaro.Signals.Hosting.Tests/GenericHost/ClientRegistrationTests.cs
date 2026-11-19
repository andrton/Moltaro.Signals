using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Hosting.Tests.GenericHost;

// AddSignalClient in a container and in a generic host, against a host of this process over loopback: what it
// registers, when the client connects, when it is disposed, and its log lines.
public sealed class ClientRegistrationTests
{
    private const string ClientCategory = "Moltaro.Signals.SignalClient";

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task ISignals_resolves_to_the_registered_client()
    {
        var services = new ServiceCollection();
        services.AddSignalClient(options =>
        {
            options.HostEndPoint = new IPEndPoint(IPAddress.Loopback, 5000);
            options.Token = LoopbackOptions.Token;
        });
        await using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<SignalClient>(), provider.GetRequiredService<ISignals>());
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Configure_runs_when_the_client_is_first_resolved_and_wrong_options_throw_there()
    {
        var services = new ServiceCollection();
        bool configured = false;

        // Leaves HostEndPoint unset, which the constructor of the client refuses.
        services.AddSignalClient(options =>
        {
            configured = true;
            options.Token = LoopbackOptions.Token;
        });
        bool configuredByTheCall = configured;
        await using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Throws<ArgumentException>("HostEndPoint", () => provider.GetRequiredService<SignalClient>());
        Assert.False(configuredByTheCall);
        Assert.True(configured);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_client_connects_when_the_application_starts()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var signalHost = new SignalHost(LoopbackOptions.Host());
        signalHost.Start();
        using IHost app = CreateBuilder(signalHost.LocalEndPoint!, LoopbackOptions.Token).Build();

        await app.StartAsync(cancellationToken);

        using ISignalSubscription lane = app.Services.GetRequiredService<ISignals>().Subscribe("jobs", "workers");
        Assert.Equal(SignalWaitResultEnum.Joined, await lane.WaitAsync(until: null, cancellationToken));

        await app.StopAsync(cancellationToken);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_client_outlives_the_hosted_services_and_is_disposed_with_the_container()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var signalHost = new SignalHost(LoopbackOptions.Host());
        signalHost.Start();
        IHost app = CreateBuilder(signalHost.LocalEndPoint!, LoopbackOptions.Token).Build();
        await app.StartAsync(cancellationToken);
        SignalClient client = app.Services.GetRequiredService<SignalClient>();
        using ISignalSubscription lane = client.Subscribe("jobs", "workers");

        // Joined comes once the client is connected.
        await lane.WaitAsync(until: null, cancellationToken);

        await app.StopAsync(cancellationToken);
        SignalConnectionStateEnum afterStop = client.State;
        app.Dispose();

        Assert.Equal(SignalConnectionStateEnum.Connected, afterStop);
        Assert.Throws<ObjectDisposedException>(() => client.Subscribe("jobs", "workers"));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_client_writes_an_Information_line_with_HostEndPoint_when_it_connects()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var signalHost = new SignalHost(LoopbackOptions.Host());
        signalHost.Start();
        var recorder = new LogRecorder();
        HostApplicationBuilder builder = CreateBuilder(signalHost.LocalEndPoint!, LoopbackOptions.Token);
        builder.Logging.AddProvider(recorder);
        using IHost app = builder.Build();

        await app.StartAsync(cancellationToken);
        LogLine line = await recorder.WaitForLineAsync(ClientCategory, LogLevel.Information, cancellationToken);

        Assert.Equal(signalHost.LocalEndPoint!.ToString(), Assert.Contains("HostEndPoint", line.Values)?.ToString());

        await app.StopAsync(cancellationToken);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_client_writes_a_Warning_line_with_HostEndPoint_and_Reason_when_its_connection_ends()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var signalHost = new SignalHost(LoopbackOptions.Host());
        signalHost.Start();
        IPEndPoint hostEndPoint = signalHost.LocalEndPoint!;
        var recorder = new LogRecorder();
        HostApplicationBuilder builder = CreateBuilder(hostEndPoint, LoopbackOptions.Token);
        builder.Logging.AddProvider(recorder);
        using IHost app = builder.Build();
        await app.StartAsync(cancellationToken);
        await recorder.WaitForLineAsync(ClientCategory, LogLevel.Information, cancellationToken);

        await signalHost.DisposeAsync();
        LogLine line = await recorder.WaitForLineAsync(ClientCategory, LogLevel.Warning, cancellationToken);

        Assert.Equal(hostEndPoint.ToString(), Assert.Contains("HostEndPoint", line.Values)?.ToString());
        Assert.False(string.IsNullOrEmpty(Assert.Contains("Reason", line.Values)?.ToString()));

        await app.StopAsync(cancellationToken);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_refused_attempt_is_written_as_a_Warning_line_whose_Reason_begins_with_the_code()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var signalHost = new SignalHost(LoopbackOptions.Host());
        signalHost.Start();
        var recorder = new LogRecorder();
        HostApplicationBuilder builder = CreateBuilder(signalHost.LocalEndPoint!, "wrong-token");
        builder.Logging.AddProvider(recorder);
        using IHost app = builder.Build();

        await app.StartAsync(cancellationToken);
        LogLine line = await recorder.WaitForLineAsync(ClientCategory, LogLevel.Warning, cancellationToken);

        Assert.Equal(signalHost.LocalEndPoint!.ToString(), Assert.Contains("HostEndPoint", line.Values)?.ToString());
        Assert.StartsWith("token", Assert.Contains("Reason", line.Values)?.ToString());

        await app.StopAsync(cancellationToken);
    }

    // A generic host with nothing but AddSignalClient: no configuration sources and no log providers of its own.
    private static HostApplicationBuilder CreateBuilder(IPEndPoint hostEndPoint, string token)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(settings: null);

        builder.Services.AddSignalClient(options =>
        {
            options.HostEndPoint = hostEndPoint;
            options.Token = token;
        });

        return builder;
    }
}
