namespace Moltaro.Signals.Protocol;

// The verbs of the lines the host sends to a client, as docs/protocol.md fixes them. On the wire they are written in
// upper case, exactly.
internal enum HostVerbEnum
{
    // OK 0, the answer to an accepted HELLO; OK <sid>, the answer to SUB or to UNWAIT; or OK <rid> <groups>, the answer
    // to SET.
    Ok,

    // ERR 0 <code> <text>, a refusal of the connection, which the host closes after it; or ERR <sid> subscriptions
    // <text>, a refused SUB, with the connection kept open.
    Err,

    // SIG <sid>: a signal handed to the subscription.
    Sig,

    // PONG: the answer to PING.
    Pong,
}
