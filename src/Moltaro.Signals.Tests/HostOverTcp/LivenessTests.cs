using Moltaro.Signals.Tests.RawProtocol;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.HostOverTcp;

// The liveness of a connection over loopback, in raw protocol lines, with a fake clock: PING is answered, and a
// connection on which the host received nothing for DeadPeerTimeout is closed with nothing written.
public sealed class LivenessTests
{
    // Not the default, so that a deadline the host did not take from its options shows.
    private static readonly TimeSpan DeadPeerTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_PING_is_answered_with_PONG()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using RawConnection connection = await RawConnection.HelloAsync(host, cancellationToken);

        await connection.SendAsync("PING\r\n", cancellationToken);

        Assert.Equal("PONG", await connection.ReadLineAsync(cancellationToken));
    }

    // The silence is counted from the last line the host received: two PINGs, each just before DeadPeerTimeout after
    // the line before it, keep the connection open past DeadPeerTimeout after its HELLO. The only timer of the host is
    // that of the one connection.
    [Fact]
    public async Task A_connection_silent_for_DeadPeerTimeout_after_its_last_line_is_closed_with_nothing_written()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        TimeSpan justBefore = DeadPeerTimeout - TimeSpan.FromMilliseconds(1);
        var clock = new FakeClock();
        await using SignalHost host = LoopbackHost.Start(clock, options => options.DeadPeerTimeout = DeadPeerTimeout);
        using RawConnection connection = await RawConnection.HelloAsync(host, cancellationToken);

        await clock.WaitForTimerAsync(clock.GetUtcNow() + DeadPeerTimeout, cancellationToken);
        clock.Advance(justBefore);
        await connection.SendAsync("PING\r\n", cancellationToken);
        Assert.Equal("PONG", await connection.ReadLineAsync(cancellationToken));

        await clock.WaitForTimerAsync(clock.GetUtcNow() + DeadPeerTimeout, cancellationToken);
        clock.Advance(justBefore);
        await connection.SendAsync("PING\r\n", cancellationToken);
        Assert.Equal("PONG", await connection.ReadLineAsync(cancellationToken));

        await clock.WaitForTimerAsync(clock.GetUtcNow() + DeadPeerTimeout, cancellationToken);
        clock.Advance(DeadPeerTimeout);

        Assert.Null(await connection.ReadLineAsync(cancellationToken));
    }
}
