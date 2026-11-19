using System.Net;
using Moltaro.Signals.Tests.RawProtocol;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.HostOverTcp;

// The end of a connection over loopback, in raw protocol lines: when the host is disposed, when the client closes it,
// and when the host refuses it. A member is busy once its SUB is answered, until its WAIT; a busy member whose
// connection ends hands its signal on, here to an in-process member of its group that waits.
public sealed class ConnectionEndTests
{
    // One connection has an armed member, which the dispose does not answer; the other has none.
    [Fact]
    public async Task Disposing_the_host_closes_every_connection_with_nothing_more_written_and_frees_its_port()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SignalHost host = LoopbackHost.Start(new FakeClock());
        IPEndPoint endPoint = host.LocalEndPoint!;
        using RawConnection waiting = await RawConnection.HelloAsync(host, cancellationToken);
        using RawConnection idle = await RawConnection.HelloAsync(host, cancellationToken);
        await waiting.SendAsync("SUB 1 jobs workers\r\nWAIT 1\r\nPING\r\n", cancellationToken);
        Assert.Equal("OK 1", await waiting.ReadLineAsync(cancellationToken));
        Assert.Equal("PONG", await waiting.ReadLineAsync(cancellationToken));

        await host.DisposeAsync();
        await using SignalHost next = LoopbackHost.Start(new FakeClock(), options => options.ListenEndPoint = endPoint);

        Assert.Equal(endPoint, next.LocalEndPoint);
        Assert.Null(await waiting.ReadLineAsync(cancellationToken));
        Assert.Null(await idle.ReadLineAsync(cancellationToken));
    }

    [Fact]
    public async Task A_busy_member_whose_client_closes_the_connection_hands_its_signal_to_an_in_process_member_that_waits()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using ISignalSubscription staying = host.Subscribe("jobs", "workers");
        await staying.WaitAsync(until: null, cancellationToken);
        ValueTask<SignalWaitResultEnum> wait = staying.WaitAsync(until: null, cancellationToken);
        RawConnection leaving = await RawConnection.HelloAsync(host, cancellationToken);
        await leaving.SendAsync("SUB 1 jobs workers\r\n", cancellationToken);
        Assert.Equal("OK 1", await leaving.ReadLineAsync(cancellationToken));

        leaving.Dispose();

        Assert.Equal(SignalWaitResultEnum.Signaled, await wait);
    }

    // The ERR line comes first; then the host removes the members of the connection, the busy one on jobs handing its
    // signal on, before it closes the connection. Once the client has read the end, the member on mail is gone.
    [Fact]
    public async Task The_members_of_a_connection_refused_with_protocol_are_removed_before_it_is_closed()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using ISignalSubscription staying = host.Subscribe("jobs", "workers");
        await staying.WaitAsync(until: null, cancellationToken);
        ValueTask<SignalWaitResultEnum> wait = staying.WaitAsync(until: null, cancellationToken);
        using RawConnection refused = await RawConnection.HelloAsync(host, cancellationToken);
        await refused.SendAsync("SUB 1 jobs workers\r\nSUB 2 mail senders\r\n", cancellationToken);
        Assert.Equal("OK 1", await refused.ReadLineAsync(cancellationToken));
        Assert.Equal("OK 2", await refused.ReadLineAsync(cancellationToken));

        await refused.SendAsync("SUB 3 bad..key workers\r\n", cancellationToken);

        Assert.StartsWith("ERR 0 protocol ", await refused.ReadLineAsync(cancellationToken));
        Assert.Null(await refused.ReadLineAsync(cancellationToken));
        Assert.Equal(0, (await host.SetAsync("mail", cancellationToken)).Groups);
        Assert.Equal(SignalWaitResultEnum.Signaled, await wait);
    }
}
