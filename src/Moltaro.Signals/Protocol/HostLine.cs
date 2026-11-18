namespace Moltaro.Signals.Protocol;

// One line from the host, as the remote client reads it from the receive buffer of its connection by the grammar of
// docs/protocol.md. It is read where it lies: its spans point into that buffer, and nothing is copied or allocated.
//
// Only the fields of the verb read are set; the others keep their default. The codec knows nothing of signal delivery
// or sockets.
internal readonly ref struct HostLine
{
    // The fixed sentences of the refusals, for the reason a client gives when it closes the connection, beside those of
    // LineFraming.
    private const string NotOneSpace = "The fields of a line of the host are not separated by exactly one space.";

    private const string UnknownVerb = "A line of the host has an unknown verb.";

    private const string WrongFields = "A line of the host does not have the fields of its verb.";

    private const string NotAnId = "A line of the host has an id that breaks the rule of ids.";

    private const string NotGroups = "A line of the host has a count of groups that breaks its rule.";

    private const string NotACode = "A line of the host has an unknown code, or a code that does not fit its id.";

    private const string NotAText = "A line of the host has a text that breaks the rule of texts.";

    private HostLine(string reason)
    {
        Status = LineStatusEnum.Refused;
        Error = ProtocolErrorEnum.Protocol;
        Reason = reason;
    }

    private HostLine(
        int length,
        HostVerbEnum verb,
        long id = 0,
        int? groups = null,
        ProtocolErrorEnum error = default,
        ReadOnlySpan<byte> text = default)
    {
        Status = LineStatusEnum.Complete;
        Length = length;
        Verb = verb;
        Id = id;
        Groups = groups;
        Error = error;
        Text = text;
    }

    // Whether the first line of the buffer is incomplete, complete or refused. A refused line has its Error and its
    // Reason set.
    internal LineStatusEnum Status { get; }

    // The bytes of a complete line, its \r\n included.
    internal int Length { get; }

    // The verb of a complete line.
    internal HostVerbEnum Verb { get; }

    // The id of OK and ERR, which names the request they answer: 0 for OK 0 and ERR 0, which answer the HELLO or the
    // connection as a whole; otherwise the <sid> of a SUB or an UNWAIT, or the <rid> of a SET. The <sid> of SIG.
    internal long Id { get; }

    // The <groups> of OK <rid> <groups>, the answer to a SET; null for every other line.
    internal int? Groups { get; }

    // The code of an ERR line; Protocol for a refused line.
    internal ProtocolErrorEnum Error { get; }

    // The <text> of an ERR line, as its ASCII bytes.
    internal ReadOnlySpan<byte> Text { get; }

    // Set for every refused line: the fixed sentence for a person that says why.
    internal string? Reason { get; }

    // Reads the first line of buffer where it lies, without copying or allocating.
    //
    // Incomplete when buffer holds no \n yet and fewer than ProtocolRules.LineRoom bytes. Refused with Protocol for a
    // line that breaks the grammar of docs/protocol.md or is too long, Reason holding the fixed sentence that says why.
    // Otherwise Complete, with Length counting the \r\n.
    internal static HostLine Read(ReadOnlySpan<byte> buffer)
    {
        LineStatusEnum status = LineFraming.Find(buffer, out ReadOnlySpan<byte> text, out int length, out string? reason);

        if (status == LineStatusEnum.Incomplete)
        {
            // The default line is Incomplete.
            return default;
        }

        if (status == LineStatusEnum.Refused)
        {
            return new HostLine(reason!);
        }

        ReadOnlySpan<byte> rest = text;
        ReadOnlySpan<byte> verb = LineFraming.NextField(ref rest);

        // The <text> of an ERR line may hold spaces, so ERR checks the spaces between its own fields.
        if (verb.SequenceEqual("ERR"u8))
        {
            return ReadErr(rest, length);
        }

        if (text.IndexOf("  "u8) >= 0)
        {
            return new HostLine(NotOneSpace);
        }

        // Every field is now one or more characters without a space.
        int fields = text.Count((byte)' ') + 1;

        if (verb.SequenceEqual("OK"u8))
        {
            return ReadOk(rest, fields, length);
        }

        if (verb.SequenceEqual("SIG"u8))
        {
            return ReadSig(rest, fields, length);
        }

        if (verb.SequenceEqual("PONG"u8))
        {
            return fields == 1 ? new HostLine(length, HostVerbEnum.Pong) : new HostLine(WrongFields);
        }

        return new HostLine(UnknownVerb);
    }

    // OK 0, OK <sid> or OK <rid> <groups>. The id 0 stands only in OK 0.
    private static HostLine ReadOk(ReadOnlySpan<byte> rest, int fields, int length)
    {
        if (fields is not (2 or 3))
        {
            return new HostLine(WrongFields);
        }

        ReadOnlySpan<byte> idField = LineFraming.NextField(ref rest);

        if (fields == 2 && idField.SequenceEqual("0"u8))
        {
            return new HostLine(length, HostVerbEnum.Ok);
        }

        if (!ProtocolRules.IsId(idField, out long id))
        {
            return new HostLine(NotAnId);
        }

        if (fields == 2)
        {
            return new HostLine(length, HostVerbEnum.Ok, id);
        }

        if (!TryReadGroups(rest, out int groups))
        {
            return new HostLine(NotGroups);
        }

        return new HostLine(length, HostVerbEnum.Ok, id, groups);
    }

    // ERR 0 <code> <text> for version, token, connections and protocol; ERR <sid> subscriptions <text>. The text is
    // the rest of the line.
    private static HostLine ReadErr(ReadOnlySpan<byte> rest, int length)
    {
        ReadOnlySpan<byte> idField = LineFraming.NextField(ref rest);
        ReadOnlySpan<byte> codeField = LineFraming.NextField(ref rest);

        // An empty field stands where two spaces are in a row.
        if (idField.IsEmpty || codeField.IsEmpty)
        {
            return new HostLine(NotOneSpace);
        }

        if (rest.IsEmpty)
        {
            return new HostLine(WrongFields);
        }

        long id = 0;
        bool ofConnection = idField.SequenceEqual("0"u8);

        if (!ofConnection && !ProtocolRules.IsId(idField, out id))
        {
            return new HostLine(NotAnId);
        }

        // A code of the connection stands only with the id 0, and subscriptions only with a <sid>.
        if (!TryReadCode(codeField, out ProtocolErrorEnum code) || ofConnection == (code == ProtocolErrorEnum.Subscriptions))
        {
            return new HostLine(NotACode);
        }

        // The line has no space at its end, so the text can only start with one.
        if (rest.Length > ProtocolRules.MaxTextLength || rest[0] == ' ')
        {
            return new HostLine(NotAText);
        }

        return new HostLine(length, HostVerbEnum.Err, id, error: code, text: rest);
    }

    // SIG <sid>.
    private static HostLine ReadSig(ReadOnlySpan<byte> rest, int fields, int length)
    {
        if (fields != 2)
        {
            return new HostLine(WrongFields);
        }

        if (!ProtocolRules.IsId(rest, out long sid))
        {
            return new HostLine(NotAnId);
        }

        return new HostLine(length, HostVerbEnum.Sig, sid);
    }

    // <groups>: 0, or a number of the id form at most 2^31-1, which has at most 10 digits.
    private static bool TryReadGroups(ReadOnlySpan<byte> field, out int groups)
    {
        groups = 0;

        if (field.SequenceEqual("0"u8))
        {
            return true;
        }

        if (!ProtocolRules.IsId(field, out long value) || value > int.MaxValue)
        {
            return false;
        }

        groups = (int)value;

        return true;
    }

    // The code words of docs/protocol.md, in lower case, exactly.
    private static bool TryReadCode(ReadOnlySpan<byte> field, out ProtocolErrorEnum code)
    {
        code = default;

        if (field.SequenceEqual("version"u8))
        {
            code = ProtocolErrorEnum.Version;
        }
        else if (field.SequenceEqual("token"u8))
        {
            code = ProtocolErrorEnum.Token;
        }
        else if (field.SequenceEqual("connections"u8))
        {
            code = ProtocolErrorEnum.Connections;
        }
        else if (field.SequenceEqual("protocol"u8))
        {
            code = ProtocolErrorEnum.Protocol;
        }
        else if (field.SequenceEqual("subscriptions"u8))
        {
            code = ProtocolErrorEnum.Subscriptions;
        }
        else
        {
            return false;
        }

        return true;
    }
}
