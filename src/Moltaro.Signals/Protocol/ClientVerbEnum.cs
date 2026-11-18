namespace Moltaro.Signals.Protocol;

// The verbs of the lines a client sends to the host, as docs/protocol.md fixes them. On the wire they are written in
// upper case, exactly.
internal enum ClientVerbEnum
{
    // HELLO <version> <token>: the first line of a connection. Answered with OK 0, or with ERR 0 version or ERR 0
    // token, after which the host closes the connection.
    Hello,

    // SUB <sid> <key> <group>: opens the subscription <sid>, one member of the group on the key, busy at once, since
    // joining counts as a signal. Answered with OK <sid>, or with ERR <sid> subscriptions beyond the host's limit.
    Sub,

    // WAIT <sid>: arms the member, which ends its busy state. Answered only by a SIG <sid>, at once if a signal is
    // pending.
    Wait,

    // UNWAIT <sid>: ends the wait of the member; an armed member becomes idle, a busy or idle one stays as it is.
    // Answered with OK <sid>, after any SIG <sid> the host wrote before it.
    Unwait,

    // UNSUB <sid>: closes the subscription; a busy member hands its signal on. Not answered.
    Unsub,

    // SET <rid> <key>: sets the key and each of its ancestors. Answered with OK <rid> <groups>.
    Set,

    // PING: the heartbeat of the client. Answered with PONG.
    Ping,
}
