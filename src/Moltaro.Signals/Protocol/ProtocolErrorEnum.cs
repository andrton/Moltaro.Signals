namespace Moltaro.Signals.Protocol;

// The codes of the ERR lines of docs/protocol.md. Each is written on the wire as its code word, in lower case.
internal enum ProtocolErrorEnum
{
    // version: a HELLO with a protocol version the host does not speak. The host closes the connection.
    Version,

    // token: a HELLO with another token. The host closes the connection.
    Token,

    // connections: a connection accepted while the host holds its limit of connections, answered at once, before its
    // HELLO is read. The host closes the connection.
    Connections,

    // protocol: a line that breaks the grammar or is too long, a line before HELLO, a second HELLO, a SUB for a <sid>
    // that is open, a WAIT for an armed member, a name that breaks the name rule, or no HELLO within the host's
    // dead-peer time. The host closes the connection, with the text as the reason. A correct client never causes it.
    Protocol,

    // subscriptions: a SUB beyond the host's limit of subscriptions per connection. The connection stays open.
    Subscriptions,
}
