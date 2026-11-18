using System.Text;
using Moltaro.Signals.Protocol;

namespace Moltaro.Signals.Tests.Protocol;

// How the host reads the first line of a buffer that a client sent, by the rules of docs/protocol.md: every line form
// a client sends, and every refusal with its code. A line given as text has one byte per character (Latin-1), so that
// a test can hold a byte outside ASCII.
public sealed class ClientLineTests
{
    // A buffer of 283 bytes with no \n yet may still become a line of 282 characters and its \r\n.
    public static TheoryData<string> IncompleteLines =>
    [
        "",
        "PING\r",
        "SET 2 jo",
        "PUB 1",
        $"SET 1 {new string('a', 277)}",
    ];

    public static TheoryData<string> LinesRefusedWithProtocol =>
    [
        // Lines: ASCII from U+0020 to U+007E, ended by \r\n, at most 282 characters before it.
        "PING\n",
        "PI\rNG\r\n",
        "WAIT\t1\r\n",
        "SET 2 jobs\u007F\r\n",
        "HELLO 1 s3cret\u0080\r\n",
        $"SET 1 {new string('a', 278)}",
        $"HELLO 1000000000000000000 {new string('t', 257)}\r\n",

        // Lines: fields separated by exactly one space, none at either end; verbs case-sensitive.
        "\r\n",
        "WAIT  1\r\n",
        " PING\r\n",
        "PING \r\n",
        "ping\r\n",
        "PUB 1 jobs\r\n",

        // The fields of each verb, no more and no fewer.
        "HELLO 1\r\n",
        "HELLO 1 s3cret extra\r\n",
        "SUB 1 jobs\r\n",
        "SUB 1 jobs workers extra\r\n",
        "WAIT\r\n",
        "UNSUB 1 2\r\n",
        "SET 2\r\n",
        "SET 2 jobs extra\r\n",
        "PING 1\r\n",

        // An id: 1 to 19 digits, no leading zero, at most 2^63-1, never 0.
        "WAIT 0\r\n",
        "WAIT 01\r\n",
        "WAIT 1a\r\n",
        "WAIT 9223372036854775808\r\n",
        "WAIT 10000000000000000000\r\n",
        "SET 0 jobs\r\n",

        // A version has the form of an id; another version outside the rules of Lines is no version refusal.
        "HELLO 01 s3cret\r\n",
        "HELLO 2  s3cret\r\n",

        // A name: 1 to 128 characters from A-Z a-z 0-9 . _ : -, with no empty segment.
        "SET 2 bad..key\r\n",
        "SET 2 jobs/mail\r\n",
        $"SET 2 {new string('k', 129)}\r\n",
        "SUB 1 bad..key workers\r\n",
        "SUB 1 jobs bad..group\r\n",
        $"SUB 1 jobs {new string('g', 129)}\r\n",

        // A token: 1 to 256 characters.
        $"HELLO 1 {new string('t', 257)}\r\n",
    ];

    // A first line of HELLO, a space and a version of the id form other than 1, whatever else it holds within the
    // rules of Lines. The longest is the longest line, 282 characters.
    public static TheoryData<string> LinesRefusedWithVersion =>
    [
        "HELLO 2 s3cret\r\n",
        "HELLO 2\r\n",
        "HELLO 2 a b\r\n",
        $"HELLO 1000000000000000000 {new string('t', 256)}\r\n",
    ];

    public static TheoryData<string> Tokens =>
    [
        "!",
        "~",
        new string('t', 256),
    ];

    [Fact]
    public void HELLO_of_version_1_is_read_with_its_token()
    {
        ClientLine line = ClientLine.Read("HELLO 1 s3cret\r\n"u8);

        Assert.Equal(LineStatusEnum.Complete, line.Status);
        Assert.Equal(16, line.Length);
        Assert.Equal(ClientVerbEnum.Hello, line.Verb);
        Assert.Equal("s3cret", Encoding.ASCII.GetString(line.Token));
    }

    [Theory]
    [MemberData(nameof(Tokens))]
    public void A_token_of_1_to_256_characters_from_U0021_to_U007E_is_read(string token)
    {
        ClientLine line = ClientLine.Read(Encoding.ASCII.GetBytes($"HELLO 1 {token}\r\n"));

        Assert.Equal(LineStatusEnum.Complete, line.Status);
        Assert.Equal(token, Encoding.ASCII.GetString(line.Token));
    }

    [Fact]
    public void SUB_is_read_with_its_sid_key_and_group()
    {
        ClientLine line = ClientLine.Read("SUB 1 jobs workers\r\n"u8);

        Assert.Equal(LineStatusEnum.Complete, line.Status);
        Assert.Equal(20, line.Length);
        Assert.Equal(ClientVerbEnum.Sub, line.Verb);
        Assert.Equal(1, line.Id);
        Assert.Equal("jobs", Encoding.ASCII.GetString(line.Key));
        Assert.Equal("workers", Encoding.ASCII.GetString(line.Group));
    }

    // The longest line a client sends that the host accepts: 281 characters.
    [Fact]
    public void The_longest_SUB_is_read_with_a_sid_of_2_63_minus_1_and_names_of_128_characters()
    {
        string key = new('k', 128);
        string group = new('g', 128);

        ClientLine line = ClientLine.Read(Encoding.ASCII.GetBytes($"SUB 9223372036854775807 {key} {group}\r\n"));

        Assert.Equal(LineStatusEnum.Complete, line.Status);
        Assert.Equal(283, line.Length);
        Assert.Equal(long.MaxValue, line.Id);
        Assert.Equal(key, Encoding.ASCII.GetString(line.Key));
        Assert.Equal(group, Encoding.ASCII.GetString(line.Group));
    }

    [Theory]
    [InlineData("WAIT")]
    [InlineData("UNWAIT")]
    [InlineData("UNSUB")]
    public void WAIT_UNWAIT_and_UNSUB_are_read_with_their_sid(string verb)
    {
        string text = $"{verb} 7\r\n";

        ClientLine line = ClientLine.Read(Encoding.ASCII.GetBytes(text));

        Assert.Equal(LineStatusEnum.Complete, line.Status);
        Assert.Equal(text.Length, line.Length);
        Assert.Equal(Enum.Parse<ClientVerbEnum>(verb, ignoreCase: true), line.Verb);
        Assert.Equal(7, line.Id);
    }

    [Fact]
    public void SET_is_read_with_its_rid_and_key()
    {
        ClientLine line = ClientLine.Read("SET 2 jobs.mail\r\n"u8);

        Assert.Equal(LineStatusEnum.Complete, line.Status);
        Assert.Equal(17, line.Length);
        Assert.Equal(ClientVerbEnum.Set, line.Verb);
        Assert.Equal(2, line.Id);
        Assert.Equal("jobs.mail", Encoding.ASCII.GetString(line.Key));
    }

    [Fact]
    public void PING_is_read()
    {
        ClientLine line = ClientLine.Read("PING\r\n"u8);

        Assert.Equal(LineStatusEnum.Complete, line.Status);
        Assert.Equal(6, line.Length);
        Assert.Equal(ClientVerbEnum.Ping, line.Verb);
    }

    [Fact]
    public void Only_the_first_line_of_the_buffer_is_read()
    {
        ClientLine line = ClientLine.Read("WAIT 1\r\nPI"u8);

        Assert.Equal(LineStatusEnum.Complete, line.Status);
        Assert.Equal(8, line.Length);
        Assert.Equal(ClientVerbEnum.Wait, line.Verb);
        Assert.Equal(1, line.Id);
    }

    [Theory]
    [MemberData(nameof(IncompleteLines))]
    public void A_buffer_with_no_line_end_and_fewer_than_284_bytes_is_incomplete(string text)
    {
        ClientLine line = ClientLine.Read(Encoding.Latin1.GetBytes(text));

        Assert.Equal(LineStatusEnum.Incomplete, line.Status);
    }

    [Theory]
    [MemberData(nameof(LinesRefusedWithProtocol))]
    public void A_line_that_breaks_the_grammar_is_refused_with_protocol_and_a_reason(string text)
    {
        ClientLine line = ClientLine.Read(Encoding.Latin1.GetBytes(text));

        Assert.Equal(LineStatusEnum.Refused, line.Status);
        Assert.Equal(ProtocolErrorEnum.Protocol, line.Error);
        AssertIsText(line.Reason);
    }

    [Theory]
    [MemberData(nameof(LinesRefusedWithVersion))]
    public void A_HELLO_of_another_version_is_refused_with_version_and_a_reason(string text)
    {
        ClientLine line = ClientLine.Read(Encoding.Latin1.GetBytes(text));

        Assert.Equal(LineStatusEnum.Refused, line.Status);
        Assert.Equal(ProtocolErrorEnum.Version, line.Error);
        AssertIsText(line.Reason);
    }

    // The reason is the <text> of the host's ERR line, which never repeats what the client sent.
    [Fact]
    public void The_reason_of_a_refusal_is_a_fixed_sentence_that_never_repeats_the_line()
    {
        string? first = ClientLine.Read("SET 2 secret..one\r\n"u8).Reason;
        string? second = ClientLine.Read("SET 2 secret..two\r\n"u8).Reason;
        string? version = ClientLine.Read("HELLO 2 secret\r\n"u8).Reason;

        Assert.NotNull(first);
        Assert.NotNull(version);
        Assert.Equal(first, second);
        Assert.DoesNotContain("secret", first, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", version, StringComparison.Ordinal);
    }

    // The form of <text>: 1 to 128 characters from U+0020 to U+007E.
    private static void AssertIsText(string? reason)
    {
        Assert.NotNull(reason);
        Assert.InRange(reason.Length, 1, 128);
        Assert.All(reason, character => Assert.InRange(character, ' ', '~'));
    }
}
