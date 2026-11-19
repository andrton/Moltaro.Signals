using Moltaro.Signals.Tests.RawProtocol;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.HostOverTcp;

// The start of a connection over loopback, in raw protocol lines: a HELLO accepted, or the connection refused with its
// code. A refused client reads the ERR 0 line, then the end of the connection.
public sealed class HelloTests
{
    // Not the default, so that a deadline the host did not take from its options shows.
    private static readonly TimeSpan DeadPeerTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_HELLO_of_version_1_with_the_token_of_the_host_is_answered_with_OK_0()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using RawConnection connection = await RawConnection.ConnectAsync(host, cancellationToken);

        await connection.SendAsync($"HELLO 1 {LoopbackOptions.Token}\r\n", cancellationToken);

        Assert.Equal("OK 0", await connection.ReadLineAsync(cancellationToken));
    }

    [Fact]
    public async Task A_HELLO_of_another_version_is_refused_with_version()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using RawConnection connection = await RawConnection.ConnectAsync(host, cancellationToken);

        await connection.SendAsync($"HELLO 2 {LoopbackOptions.Token}\r\n", cancellationToken);

        Assert.StartsWith("ERR 0 version ", await connection.ReadLineAsync(cancellationToken));
        Assert.Null(await connection.ReadLineAsync(cancellationToken));
    }

    // The token is compared exactly: a part of it, more than it, or another case is another token.
    [Theory]
    [InlineData("test-toke")]
    [InlineData("test-tokenx")]
    [InlineData("TEST-TOKEN")]
    public async Task A_HELLO_with_another_token_is_refused_with_token(string token)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using RawConnection connection = await RawConnection.ConnectAsync(host, cancellationToken);

        await connection.SendAsync($"HELLO 1 {token}\r\n", cancellationToken);

        Assert.StartsWith("ERR 0 token ", await connection.ReadLineAsync(cancellationToken));
        Assert.Null(await connection.ReadLineAsync(cancellationToken));
    }

    // The client beyond the limit sends nothing before it reads: the host answers it before reading its HELLO, and a
    // HELLO the host had received but not read could turn the close into a reset that discards the ERR line.
    [Fact]
    public async Task A_connection_beyond_MaxConnections_is_answered_with_connections_at_once()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock(), options => options.MaxConnections = 1);
        using RawConnection admitted = await RawConnection.HelloAsync(host, cancellationToken);

        using RawConnection beyond = await RawConnection.ConnectAsync(host, cancellationToken);

        Assert.StartsWith("ERR 0 connections ", await beyond.ReadLineAsync(cancellationToken));
        Assert.Null(await beyond.ReadLineAsync(cancellationToken));
    }

    // The deadline of the HELLO counts from the accept. Here and in the next test the only timer of the host is that of
    // the one connection.
    [Fact]
    public async Task A_HELLO_just_before_DeadPeerTimeout_after_the_accept_is_answered_with_OK_0()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        await using SignalHost host = LoopbackHost.Start(clock, options => options.DeadPeerTimeout = DeadPeerTimeout);
        using RawConnection connection = await RawConnection.ConnectAsync(host, cancellationToken);

        await clock.WaitForTimerAsync(clock.GetUtcNow() + DeadPeerTimeout, cancellationToken);
        clock.Advance(DeadPeerTimeout - TimeSpan.FromMilliseconds(1));
        await connection.SendAsync($"HELLO 1 {LoopbackOptions.Token}\r\n", cancellationToken);

        Assert.Equal("OK 0", await connection.ReadLineAsync(cancellationToken));
    }

    [Fact]
    public async Task A_connection_that_sends_no_HELLO_within_DeadPeerTimeout_is_refused_with_protocol()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        await using SignalHost host = LoopbackHost.Start(clock, options => options.DeadPeerTimeout = DeadPeerTimeout);
        using RawConnection connection = await RawConnection.ConnectAsync(host, cancellationToken);

        await clock.WaitForTimerAsync(clock.GetUtcNow() + DeadPeerTimeout, cancellationToken);
        clock.Advance(DeadPeerTimeout);

        Assert.StartsWith("ERR 0 protocol ", await connection.ReadLineAsync(cancellationToken));
        Assert.Null(await connection.ReadLineAsync(cancellationToken));
    }
}
