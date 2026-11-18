using System.Text;
using Moltaro.Signals.Protocol;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.Protocol;

// How the host and the remote client write their lines, by the rules of docs/protocol.md: every line form, with its
// \r\n, into a destination with room for the longest line, 282 characters and its \r\n.
public sealed class LineFormatterTests
{
    [Fact(Skip = SkipReason.NotImplemented)]
    public void OK_0_OK_sid_and_OK_rid_groups_are_written()
    {
        Assert.Equal("OK 0\r\n", Written(destination => LineFormatter.Ok(destination, 0)));
        Assert.Equal("OK 7\r\n", Written(destination => LineFormatter.Ok(destination, 7)));
        Assert.Equal("OK 2 1\r\n", Written(destination => LineFormatter.Ok(destination, 2, 1)));
        Assert.Equal("OK 2 0\r\n", Written(destination => LineFormatter.Ok(destination, 2, 0)));
        Assert.Equal(
            "OK 9223372036854775807 2147483647\r\n",
            Written(destination => LineFormatter.Ok(destination, long.MaxValue, int.MaxValue)));
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData(0L, "version")]
    [InlineData(0L, "token")]
    [InlineData(0L, "connections")]
    [InlineData(0L, "protocol")]
    [InlineData(3L, "subscriptions")]
    public void ERR_is_written_with_the_code_word_of_its_code_and_its_text(long id, string code)
    {
        ProtocolErrorEnum error = Enum.Parse<ProtocolErrorEnum>(code, ignoreCase: true);

        string line = Written(destination => LineFormatter.Error(destination, id, error, "The host refuses."));

        Assert.Equal($"ERR {id} {code} The host refuses.\r\n", line);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void SIG_and_PONG_are_written()
    {
        Assert.Equal("SIG 1\r\n", Written(destination => LineFormatter.Signal(destination, 1)));
        Assert.Equal("PONG\r\n", Written(LineFormatter.Pong));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void The_lines_of_a_client_are_written()
    {
        Assert.Equal("HELLO 1 s3cret\r\n", Written(destination => LineFormatter.Hello(destination, "s3cret")));
        Assert.Equal("SUB 1 jobs workers\r\n", Written(destination => LineFormatter.Sub(destination, 1, "jobs", "workers")));
        Assert.Equal("WAIT 1\r\n", Written(destination => LineFormatter.Wait(destination, 1)));
        Assert.Equal("UNWAIT 1\r\n", Written(destination => LineFormatter.Unwait(destination, 1)));
        Assert.Equal("UNSUB 1\r\n", Written(destination => LineFormatter.Unsub(destination, 1)));
        Assert.Equal("SET 2 jobs.mail\r\n", Written(destination => LineFormatter.Set(destination, 2, "jobs.mail")));
        Assert.Equal("PING\r\n", Written(LineFormatter.Ping));
    }

    // The longest SUB, of 281 characters, is the longest line either side writes.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void The_longest_lines_fit_the_room_of_the_longest_line()
    {
        string name = new('n', 128);
        string token = new('~', 256);
        string text = new('x', 128);

        Assert.Equal(
            $"SUB 9223372036854775807 {name} {name}\r\n",
            Written(destination => LineFormatter.Sub(destination, long.MaxValue, name, name)));
        Assert.Equal(
            $"SET 9223372036854775807 {name}\r\n",
            Written(destination => LineFormatter.Set(destination, long.MaxValue, name)));
        Assert.Equal($"HELLO 1 {token}\r\n", Written(destination => LineFormatter.Hello(destination, token)));
        Assert.Equal(
            $"ERR 9223372036854775807 subscriptions {text}\r\n",
            Written(destination => LineFormatter.Error(destination, long.MaxValue, ProtocolErrorEnum.Subscriptions, text)));
    }

    // Writes one line into a destination of 284 bytes, the room the formatter is given, and returns what the returned
    // length covers.
    private static string Written(Func<Span<byte>, int> write)
    {
        var destination = new byte[284];

        int length = write(destination);

        return Encoding.ASCII.GetString(destination, 0, length);
    }
}
