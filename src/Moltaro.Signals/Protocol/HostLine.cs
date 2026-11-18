namespace Moltaro.Signals.Protocol;

// One line from the host, as the remote client reads it from the receive buffer of its connection by the grammar of
// docs/protocol.md. It is read where it lies: its spans point into that buffer, and nothing is copied or allocated.
//
// Only the fields of the verb read are set; the others keep their default. The codec knows nothing of signal delivery
// or sockets.
internal readonly ref struct HostLine
{
    // Reads the first line of buffer where it lies, without copying or allocating.
    //
    // Incomplete when buffer holds no \n yet and fewer than ProtocolRules.MaxLineLength + 2 bytes. Refused with
    // Protocol for a line that breaks the grammar of docs/protocol.md or is too long, Reason holding the fixed
    // sentence that says why. Otherwise Complete, with Length counting the \r\n.
    internal static HostLine Read(ReadOnlySpan<byte> buffer)
    {
        throw new NotSupportedException();
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
}
