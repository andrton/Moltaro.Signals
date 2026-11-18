using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.HostOverTcp;

// Protocol violations over loopback, in raw protocol lines: the host answers ERR 0 protocol and closes the connection,
// and the client reads the ERR line, then the end of the connection. Each test sends the violation as its last bytes,
// so that the host has read all the client sent when it closes, and the close cannot turn into a reset that discards
// the ERR line.
public sealed class ViolationTests
{
    public static TheoryData<string> BrokenLines =>
    [
        // An unknown verb.
        "PUB 1 jobs\r\n",

        // A name longer than 128 characters.
        $"SET 1 {new string('k', 129)}\r\n",

        // A line longer than 282 characters: 284 bytes with no \n are enough to refuse it.
        $"SET 1 {new string('k', 278)}",

        // A second HELLO, of the version of the host or of another: after HELLO either is protocol.
        $"HELLO 1 {LoopbackOptions.Token}\r\n",
        $"HELLO 2 {LoopbackOptions.Token}\r\n",
    ];

    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(BrokenLines))]
    public async Task A_line_that_breaks_the_grammar_after_HELLO_closes_the_connection_with_protocol(string text)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using RawConnection connection = await RawConnection.HelloAsync(host, cancellationToken);

        await connection.SendAsync(text, cancellationToken);

        Assert.StartsWith("ERR 0 protocol ", await connection.ReadLineAsync(cancellationToken));
        Assert.Null(await connection.ReadLineAsync(cancellationToken));
    }

    // The first line of a connection is its HELLO; a PING in its place is no exception.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_line_before_HELLO_closes_the_connection_with_protocol()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using RawConnection connection = await RawConnection.ConnectAsync(host, cancellationToken);

        await connection.SendAsync("PING\r\n", cancellationToken);

        Assert.StartsWith("ERR 0 protocol ", await connection.ReadLineAsync(cancellationToken));
        Assert.Null(await connection.ReadLineAsync(cancellationToken));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_SUB_for_a_sid_that_is_open_closes_the_connection_with_protocol()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using RawConnection connection = await RawConnection.HelloAsync(host, cancellationToken);

        await connection.SendAsync("SUB 1 jobs workers\r\nSUB 1 mail senders\r\n", cancellationToken);

        Assert.Equal("OK 1", await connection.ReadLineAsync(cancellationToken));
        Assert.StartsWith("ERR 0 protocol ", await connection.ReadLineAsync(cancellationToken));
        Assert.Null(await connection.ReadLineAsync(cancellationToken));
    }

    // The first WAIT ends the busy state of the new member and arms it; nothing is pending, so it stays armed.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_WAIT_for_an_armed_member_closes_the_connection_with_protocol()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using RawConnection connection = await RawConnection.HelloAsync(host, cancellationToken);

        await connection.SendAsync("SUB 1 jobs workers\r\nWAIT 1\r\nWAIT 1\r\n", cancellationToken);

        Assert.Equal("OK 1", await connection.ReadLineAsync(cancellationToken));
        Assert.StartsWith("ERR 0 protocol ", await connection.ReadLineAsync(cancellationToken));
        Assert.Null(await connection.ReadLineAsync(cancellationToken));
    }
}
