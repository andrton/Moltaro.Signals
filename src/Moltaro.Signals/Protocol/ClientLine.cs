namespace Moltaro.Signals.Protocol;

// One line from a client, as the host reads it from the receive buffer of a connection by the grammar of
// docs/protocol.md. It is read where it lies: its spans point into that buffer, and nothing is copied or allocated.
//
// Only the fields of the verb read are set; the others keep their default. The codec knows nothing of signal delivery
// or sockets.
internal readonly ref struct ClientLine
{
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
        throw new NotSupportedException();
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
}
