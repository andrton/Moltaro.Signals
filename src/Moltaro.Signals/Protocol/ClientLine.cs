using System.Text;

namespace Moltaro.Signals.Protocol;

// One line from a client, as the host reads it from the receive buffer of a connection by the grammar of
// docs/protocol.md. It is read where it lies: its spans point into that buffer, and nothing is copied or allocated.
//
// Only the fields of the verb read are set; the others keep their default. The codec knows nothing of signal delivery
// or sockets.
internal readonly ref struct ClientLine
{
    // The most bytes of a line with its \r\n: a buffer that holds this many with no \n holds a line that is too long.
    private const int LongestLine = ProtocolRules.MaxLineLength + 2;

    // The fixed sentences of the refusals, written as the <text> of the host's ERR 0 line. None repeats what the client
    // sent.
    private const string TooLong = "A line is longer than 282 characters.";

    private const string NoLineEnd = "A line does not end with CR LF.";

    private const string OutsideAscii = "A line holds a character outside U+0020 to U+007E.";

    private const string NotOneSpace = "A line is empty, or its fields are not separated by exactly one space.";

    private const string UnknownVerb = "A line has an unknown verb.";

    private const string WrongFields = "A line does not have the fields of its verb.";

    private const string NotAnId = "A number is not 1 to 19 digits without a leading zero, at most 2^63-1.";

    private const string NotAName = "A name breaks the name rule.";

    private const string NotAToken = "A token breaks the token rule.";

    private const string OtherVersion = "The host speaks version 1 of the protocol.";

    private ClientLine(ProtocolErrorEnum error, string reason)
    {
        Status = LineStatusEnum.Refused;
        Error = error;
        Reason = reason;
    }

    private ClientLine(
        int length,
        ClientVerbEnum verb,
        long id = 0,
        ReadOnlySpan<byte> key = default,
        ReadOnlySpan<byte> group = default,
        ReadOnlySpan<byte> token = default)
    {
        Status = LineStatusEnum.Complete;
        Length = length;
        Verb = verb;
        Id = id;
        Key = key;
        Group = group;
        Token = token;
    }

    // Whether the first line of the buffer is incomplete, complete or refused. A refused line, with Protocol or with
    // Version, has its Error and its Reason set.
    internal LineStatusEnum Status { get; }

    // The bytes of a complete line, its \r\n included.
    internal int Length { get; }

    // The verb of a complete line.
    internal ClientVerbEnum Verb { get; }

    // The <sid> of SUB, WAIT, UNWAIT and UNSUB; the <rid> of SET.
    internal long Id { get; }

    // The <key> of SUB and SET, as its ASCII bytes. The host widens it onto the stack for SignalDelivery.Set, and makes
    // the key string that SignalDelivery.Open takes for a SUB.
    internal ReadOnlySpan<byte> Key { get; }

    // The <group> of SUB, as its ASCII bytes. The host makes the group string that SignalDelivery.Open takes from it.
    internal ReadOnlySpan<byte> Group { get; }

    // The <token> of HELLO, as its ASCII bytes. Comparing it with the host's token is the host's.
    internal ReadOnlySpan<byte> Token { get; }

    // The code of a refused line: Protocol, or Version for a HELLO of another version.
    internal ProtocolErrorEnum Error { get; }

    // Set for every refused line, with Protocol and with Version alike: the fixed sentence for a person that says why,
    // which the host writes as the <text> of its ERR 0 line. At most ProtocolRules.MaxTextLength characters; it never
    // repeats what the client sent.
    internal string? Reason { get; }

    // Reads the first line of buffer where it lies, without copying or allocating.
    //
    // Incomplete when buffer holds no \n yet and fewer than ProtocolRules.MaxLineLength + 2 bytes. Refused with
    // Protocol for a line that breaks the grammar of docs/protocol.md or is too long, and with Version for a HELLO of
    // another version, Reason holding in both cases the fixed sentence the host writes as <text>. Otherwise Complete,
    // with Length counting the \r\n.
    //
    // A name is checked by widening its bytes onto the stack and calling ProtocolRules.IsName.
    //
    // What depends on the state of a connection is the host's, not Read's: a line before HELLO, a SUB for a <sid> that
    // is open, a WAIT for an armed member, the limits and the token. So is answering protocol to a Version refusal of a
    // line after HELLO.
    internal static ClientLine Read(ReadOnlySpan<byte> buffer)
    {
        int lineFeed = buffer[..Math.Min(buffer.Length, LongestLine)].IndexOf((byte)'\n');

        if (lineFeed < 0)
        {
            // The default line is Incomplete.
            return buffer.Length < LongestLine ? default : Refuse(TooLong);
        }

        if (lineFeed == 0 || buffer[lineFeed - 1] != '\r')
        {
            return Refuse(NoLineEnd);
        }

        ReadOnlySpan<byte> text = buffer[..(lineFeed - 1)];
        int length = lineFeed + 1;

        if (text.ContainsAnyExceptInRange((byte)' ', (byte)'~'))
        {
            return Refuse(OutsideAscii);
        }

        // The rules of Lines hold for the whole line before any field is read, so that a HELLO of another version
        // that breaks them is protocol, not version.
        if (text.IsEmpty || text[0] == ' ' || text[^1] == ' ' || text.IndexOf("  "u8) >= 0)
        {
            return Refuse(NotOneSpace);
        }

        // Every field is now one or more characters without a space.
        int fields = text.Count((byte)' ') + 1;
        ReadOnlySpan<byte> rest = text;
        ReadOnlySpan<byte> verb = NextField(ref rest);

        if (verb.SequenceEqual("HELLO"u8))
        {
            return ReadHello(rest, fields, length);
        }

        if (verb.SequenceEqual("SUB"u8))
        {
            return ReadSub(rest, fields, length);
        }

        if (verb.SequenceEqual("WAIT"u8))
        {
            return ReadSid(ClientVerbEnum.Wait, rest, fields, length);
        }

        if (verb.SequenceEqual("UNWAIT"u8))
        {
            return ReadSid(ClientVerbEnum.Unwait, rest, fields, length);
        }

        if (verb.SequenceEqual("UNSUB"u8))
        {
            return ReadSid(ClientVerbEnum.Unsub, rest, fields, length);
        }

        if (verb.SequenceEqual("SET"u8))
        {
            return ReadSet(rest, fields, length);
        }

        if (verb.SequenceEqual("PING"u8))
        {
            return fields == 1 ? new ClientLine(length, ClientVerbEnum.Ping) : Refuse(WrongFields);
        }

        return Refuse(UnknownVerb);
    }

    // HELLO <version> <token>. A version of the id form other than 1 is refused with Version whatever follows it, so
    // that a client of another version learns why.
    private static ClientLine ReadHello(ReadOnlySpan<byte> rest, int fields, int length)
    {
        if (fields < 2)
        {
            return Refuse(WrongFields);
        }

        if (!TryReadId(NextField(ref rest), out long version))
        {
            return Refuse(NotAnId);
        }

        if (version != ProtocolRules.Version)
        {
            return new ClientLine(ProtocolErrorEnum.Version, OtherVersion);
        }

        if (fields != 3)
        {
            return Refuse(WrongFields);
        }

        ReadOnlySpan<byte> token = rest;

        if (!IsToken(token))
        {
            return Refuse(NotAToken);
        }

        return new ClientLine(length, ClientVerbEnum.Hello, token: token);
    }

    // SUB <sid> <key> <group>.
    private static ClientLine ReadSub(ReadOnlySpan<byte> rest, int fields, int length)
    {
        if (fields != 4)
        {
            return Refuse(WrongFields);
        }

        if (!TryReadId(NextField(ref rest), out long sid))
        {
            return Refuse(NotAnId);
        }

        ReadOnlySpan<byte> key = NextField(ref rest);
        ReadOnlySpan<byte> group = rest;

        if (!IsName(key) || !IsName(group))
        {
            return Refuse(NotAName);
        }

        return new ClientLine(length, ClientVerbEnum.Sub, sid, key, group);
    }

    // WAIT <sid>, UNWAIT <sid> and UNSUB <sid>.
    private static ClientLine ReadSid(ClientVerbEnum verb, ReadOnlySpan<byte> rest, int fields, int length)
    {
        if (fields != 2)
        {
            return Refuse(WrongFields);
        }

        if (!TryReadId(rest, out long sid))
        {
            return Refuse(NotAnId);
        }

        return new ClientLine(length, verb, sid);
    }

    // SET <rid> <key>.
    private static ClientLine ReadSet(ReadOnlySpan<byte> rest, int fields, int length)
    {
        if (fields != 3)
        {
            return Refuse(WrongFields);
        }

        if (!TryReadId(NextField(ref rest), out long rid))
        {
            return Refuse(NotAnId);
        }

        ReadOnlySpan<byte> key = rest;

        if (!IsName(key))
        {
            return Refuse(NotAName);
        }

        return new ClientLine(length, ClientVerbEnum.Set, rid, key);
    }

    private static ClientLine Refuse(string reason)
    {
        return new ClientLine(ProtocolErrorEnum.Protocol, reason);
    }

    // Takes the field at the start of rest, which holds no space at its start and no two spaces in a row, and leaves
    // rest after the space that follows it.
    private static ReadOnlySpan<byte> NextField(scoped ref ReadOnlySpan<byte> rest)
    {
        int space = rest.IndexOf((byte)' ');

        if (space < 0)
        {
            ReadOnlySpan<byte> last = rest;
            rest = default;

            return last;
        }

        ReadOnlySpan<byte> field = rest[..space];
        rest = rest[(space + 1)..];

        return field;
    }

    // An id of a client line: 1 to MaxIdLength digits with no leading zero, from 1 to 2^63-1. A 0 is the host's.
    private static bool TryReadId(ReadOnlySpan<byte> field, out long id)
    {
        id = 0;

        if (field.Length is 0 or > ProtocolRules.MaxIdLength || field[0] == '0')
        {
            return false;
        }

        // 19 digits stay below 2^64, so the value cannot overflow before it is compared.
        ulong value = 0;

        foreach (byte digit in field)
        {
            if (!char.IsAsciiDigit((char)digit))
            {
                return false;
            }

            value = (value * 10) + (ulong)(digit - '0');
        }

        if (value > long.MaxValue)
        {
            return false;
        }

        id = (long)value;

        return true;
    }

    // The bytes of a field are ASCII already, so widening them gives the characters the rule of names is stated for.
    private static bool IsName(ReadOnlySpan<byte> field)
    {
        if (field.Length > ProtocolRules.MaxNameLength)
        {
            return false;
        }

        Span<char> name = stackalloc char[ProtocolRules.MaxNameLength];
        int length = Encoding.ASCII.GetChars(field, name);

        return ProtocolRules.IsName(name[..length]);
    }

    private static bool IsToken(ReadOnlySpan<byte> field)
    {
        if (field.Length > ProtocolRules.MaxTokenLength)
        {
            return false;
        }

        Span<char> token = stackalloc char[ProtocolRules.MaxTokenLength];
        int length = Encoding.ASCII.GetChars(field, token);

        return ProtocolRules.IsToken(token[..length]);
    }
}
