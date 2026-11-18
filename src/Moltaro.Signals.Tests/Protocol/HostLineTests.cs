using System.Text;
using Moltaro.Signals.Protocol;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.Protocol;

// How the remote client reads the first line of a buffer that the host sent, by the rules of docs/protocol.md: every
// line form the host sends, and every refusal. A line given as text has one byte per character (Latin-1), so that a
// test can hold a byte outside ASCII.
public sealed class HostLineTests
{
    // A buffer of 283 bytes with no \n yet may still become a line of 282 characters and its \r\n.
    public static TheoryData<string> IncompleteLines =>
    [
        "",
        "OK 2 1\r",
        "SIG",
        $"ERR 0 protocol {new string('x', 268)}",
    ];

    public static TheoryData<string> LinesRefusedWithProtocol =>
    [
        // Lines: ASCII from U+0020 to U+007E, ended by \r\n, at most 282 characters before it; fields separated by
        // exactly one space, none at either end; verbs case-sensitive.
        "PONG\n",
        "SIG 1\u0080\r\n",
        $"ERR 0 protocol {new string('x', 269)}",
        "\r\n",
        "SIG  1\r\n",
        "PONG \r\n",
        "pong\r\n",
        "PING\r\n",

        // OK 0, OK <sid> or OK <rid> <groups>; <groups> is 0 or 1 to 10 digits with no leading zero, at most 2^31-1.
        "OK\r\n",
        "OK 01\r\n",
        "OK 9223372036854775808\r\n",
        "OK 0 1\r\n",
        "OK 2 01\r\n",
        "OK 2 -1\r\n",
        "OK 2 2147483648\r\n",
        "OK 2 1 1\r\n",

        // ERR 0 with version, token, connections or protocol; ERR <sid> with subscriptions; a <text> of 1 to 128
        // characters.
        "ERR 0 version\r\n",
        "ERR 0 refused The host refuses.\r\n",
        "ERR 0 Version The host refuses.\r\n",
        "ERR 0 subscriptions The host refuses.\r\n",
        "ERR 3 token The host refuses.\r\n",
        $"ERR 0 protocol {new string('x', 129)}\r\n",

        // SIG <sid> and PONG.
        "SIG 0\r\n",
        "SIG\r\n",
        "SIG 1 2\r\n",
        "PONG 1\r\n",
    ];

    public static TheoryData<string> Texts =>
    [
        "x",
        "A name breaks the name rule.",
        new string('x', 128),
    ];

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("OK 0\r\n", 0L)]
    [InlineData("OK 7\r\n", 7L)]
    [InlineData("OK 9223372036854775807\r\n", long.MaxValue)]
    public void OK_0_and_OK_sid_are_read_with_their_id_and_no_groups(string text, long id)
    {
        HostLine line = HostLine.Read(Encoding.ASCII.GetBytes(text));

        Assert.Equal(LineStatusEnum.Complete, line.Status);
        Assert.Equal(text.Length, line.Length);
        Assert.Equal(HostVerbEnum.Ok, line.Verb);
        Assert.Equal(id, line.Id);
        Assert.Null(line.Groups);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("2147483647", int.MaxValue)]
    public void OK_rid_groups_is_read_with_its_rid_and_groups(string groupsText, int groups)
    {
        HostLine line = HostLine.Read(Encoding.ASCII.GetBytes($"OK 2 {groupsText}\r\n"));

        Assert.Equal(LineStatusEnum.Complete, line.Status);
        Assert.Equal(HostVerbEnum.Ok, line.Verb);
        Assert.Equal(2, line.Id);
        Assert.Equal(groups, line.Groups);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("version")]
    [InlineData("token")]
    [InlineData("connections")]
    [InlineData("protocol")]
    public void ERR_0_is_read_with_the_code_of_a_refused_connection_and_its_text(string code)
    {
        HostLine line = HostLine.Read(Encoding.ASCII.GetBytes($"ERR 0 {code} The host refuses.\r\n"));

        Assert.Equal(LineStatusEnum.Complete, line.Status);
        Assert.Equal(HostVerbEnum.Err, line.Verb);
        Assert.Equal(0, line.Id);
        Assert.Equal(Enum.Parse<ProtocolErrorEnum>(code, ignoreCase: true), line.Error);
        Assert.Equal("The host refuses.", Encoding.ASCII.GetString(line.Text));
        Assert.Null(line.Groups);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void ERR_sid_subscriptions_is_read_with_its_sid_and_text()
    {
        HostLine line = HostLine.Read("ERR 3 subscriptions Too many subscriptions.\r\n"u8);

        Assert.Equal(LineStatusEnum.Complete, line.Status);
        Assert.Equal(HostVerbEnum.Err, line.Verb);
        Assert.Equal(3, line.Id);
        Assert.Equal(ProtocolErrorEnum.Subscriptions, line.Error);
        Assert.Equal("Too many subscriptions.", Encoding.ASCII.GetString(line.Text));
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(Texts))]
    public void A_text_of_1_to_128_characters_with_spaces_is_read_to_the_end_of_the_line(string text)
    {
        HostLine line = HostLine.Read(Encoding.ASCII.GetBytes($"ERR 0 protocol {text}\r\n"));

        Assert.Equal(LineStatusEnum.Complete, line.Status);
        Assert.Equal(text, Encoding.ASCII.GetString(line.Text));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void SIG_is_read_with_its_sid()
    {
        HostLine line = HostLine.Read("SIG 1\r\n"u8);

        Assert.Equal(LineStatusEnum.Complete, line.Status);
        Assert.Equal(7, line.Length);
        Assert.Equal(HostVerbEnum.Sig, line.Verb);
        Assert.Equal(1, line.Id);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void PONG_is_read()
    {
        HostLine line = HostLine.Read("PONG\r\n"u8);

        Assert.Equal(LineStatusEnum.Complete, line.Status);
        Assert.Equal(6, line.Length);
        Assert.Equal(HostVerbEnum.Pong, line.Verb);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void Only_the_first_line_of_the_buffer_is_read()
    {
        HostLine line = HostLine.Read("SIG 1\r\nPO"u8);

        Assert.Equal(LineStatusEnum.Complete, line.Status);
        Assert.Equal(7, line.Length);
        Assert.Equal(HostVerbEnum.Sig, line.Verb);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(IncompleteLines))]
    public void A_buffer_with_no_line_end_and_fewer_than_284_bytes_is_incomplete(string text)
    {
        HostLine line = HostLine.Read(Encoding.Latin1.GetBytes(text));

        Assert.Equal(LineStatusEnum.Incomplete, line.Status);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(LinesRefusedWithProtocol))]
    public void A_line_that_breaks_the_grammar_is_refused_with_protocol_and_a_reason(string text)
    {
        HostLine line = HostLine.Read(Encoding.Latin1.GetBytes(text));

        Assert.Equal(LineStatusEnum.Refused, line.Status);
        Assert.Equal(ProtocolErrorEnum.Protocol, line.Error);
        Assert.False(string.IsNullOrEmpty(line.Reason));
    }
}
