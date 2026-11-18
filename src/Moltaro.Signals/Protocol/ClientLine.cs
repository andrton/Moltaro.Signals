using System.Text;

namespace Moltaro.Signals.Protocol;

// One line from a client, as the host reads it from the receive buffer of a connection by the grammar of
// docs/protocol.md. It is read where it lies: its spans point into that buffer, and nothing is copied or allocated.
//
// Only the fields of the verb read are set; the others keep their default. The codec knows nothing of signal delivery
// or sockets.
internal readonly ref struct ClientLine
{
    // The fixed sentences of the refusals, written as the <text> of the host's ERR 0 line, beside those of LineFraming.
    // None repeats what the client sent.
    private const string NotOneSpace = "The fields of a line are not separated by exactly one space.";

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
    // Incomplete when buffer holds no \n yet and fewer than ProtocolRules.LineRoom bytes. Refused with Protocol for a
    // line that breaks the grammar of docs/protocol.md or is too long, and with Version for a HELLO of another version,
    // Reason holding in both cases the fixed sentence the host writes as <text>. Otherwise Complete, with Length
    // counting the \r\n.
    //
    // A name is checked by widening its bytes onto the stack and calling ProtocolRules.IsName.
    //
    // What depends on the state of a connection is the host's, not Read's: a line before HELLO, a SUB for a <sid> that
    // is open, a WAIT for an armed member, the limits and the token. So is answering protocol to a Version refusal of a
    // line after HELLO.
    internal static ClientLine Read(ReadOnlySpan<byte> buffer)
    {
        LineStatusEnum status = LineFraming.Find(buffer, out ReadOnlySpan<byte> text, out int length, out string? reason);

        if (status == LineStatusEnum.Incomplete)
        {
            // The default line is Incomplete.
            return default;
        }

        if (status == LineStatusEnum.Refused)
        {
            return Refuse(reason!);
        }

        // The rules of Lines hold for the whole line before any field is read, so that a HELLO of another version
        // that breaks them is protocol, not version.
        if (text.IndexOf("  "u8) >= 0)
        {
            return Refuse(NotOneSpace);
        }

        // Every field is now one or more characters without a space.
        int fields = text.Count((byte)' ') + 1;
        ReadOnlySpan<byte> rest = text;
        ReadOnlySpan<byte> verb = LineFraming.NextField(ref rest);

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

        if (!ProtocolRules.IsId(LineFraming.NextField(ref rest), out long version))
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

        if (!ProtocolRules.IsId(LineFraming.NextField(ref rest), out long sid))
        {
            return Refuse(NotAnId);
        }

        ReadOnlySpan<byte> key = LineFraming.NextField(ref rest);
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

        if (!ProtocolRules.IsId(rest, out long sid))
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

        if (!ProtocolRules.IsId(LineFraming.NextField(ref rest), out long rid))
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
