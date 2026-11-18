using System.Net;
using System.Net.Sockets;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.Listening;

// Start, LocalEndPoint and the port of a host, over loopback.
public sealed class ListeningTests
{
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Start_binds_a_port_the_system_chose_for_port_0_and_LocalEndPoint_reports_it()
    {
        await using var host = new SignalHost(LoopbackOptions.Host());
        IPEndPoint? beforeStart = host.LocalEndPoint;

        host.Start();

        Assert.Null(beforeStart);
        Assert.NotNull(host.LocalEndPoint);
        Assert.Equal(IPAddress.Loopback, host.LocalEndPoint.Address);
        Assert.NotEqual(0, host.LocalEndPoint.Port);

        using var client = new TcpClient();
        await client.ConnectAsync(host.LocalEndPoint, TestContext.Current.CancellationToken);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_second_Start_is_refused()
    {
        await using var host = new SignalHost(LoopbackOptions.Host());
        host.Start();

        Assert.Throws<InvalidOperationException>(host.Start);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Start_after_the_host_was_disposed_is_refused()
    {
        var host = new SignalHost(LoopbackOptions.Host());
        await host.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(host.Start);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Start_reports_an_address_that_cannot_be_bound_with_SocketException()
    {
        using var occupant = new TcpListener(IPAddress.Loopback, 0);
        occupant.Start();
        SignalHostOptions options = LoopbackOptions.Host();
        options.ListenEndPoint = (IPEndPoint)occupant.LocalEndpoint;
        await using var host = new SignalHost(options);

        Assert.Throws<SocketException>(host.Start);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Disposing_the_host_frees_its_port()
    {
        var first = new SignalHost(LoopbackOptions.Host());
        first.Start();
        IPEndPoint endPoint = first.LocalEndPoint!;

        await first.DisposeAsync();

        SignalHostOptions options = LoopbackOptions.Host();
        options.ListenEndPoint = endPoint;
        await using var second = new SignalHost(options);
        second.Start();

        Assert.Equal(endPoint, second.LocalEndPoint);
    }
}
