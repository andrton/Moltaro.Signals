namespace Moltaro.Signals.Protocol;

// What reading the first line of a buffer found: ClientLine.Read and HostLine.Read report it.
internal enum LineStatusEnum
{
    // The buffer holds no \n yet and fewer than ProtocolRules.MaxLineLength + 2 bytes: the line has not arrived in
    // full.
    Incomplete,

    // The line keeps the grammar of docs/protocol.md. Its length counts the \r\n, and the fields of its verb are set.
    Complete,

    // The line breaks the grammar of docs/protocol.md or is too long, or, read by the host, is a HELLO of another
    // version. The error of the line names the code.
    Refused,
}
