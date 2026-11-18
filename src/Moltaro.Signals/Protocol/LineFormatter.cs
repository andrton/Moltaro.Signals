namespace Moltaro.Signals.Protocol;

// Writes the lines of docs/protocol.md: those the host sends to a client, and those the remote client sends to the
// host. Each method writes one line with its \r\n into destination, which has room for ProtocolRules.MaxLineLength + 2
// bytes, and returns its length in bytes. It allocates nothing. The codec knows nothing of signal delivery or sockets.
internal static class LineFormatter
{
    // OK <id>: OK 0, the answer to an accepted HELLO, or OK <sid>, the answer to SUB or to UNWAIT.
    internal static int Ok(Span<byte> destination, long id)
    {
        throw new NotSupportedException();
    }

    // OK <rid> <groups>, the answer to SET: groups is the number of (key, group) pairs over the key and its ancestors
    // that have a member, each counted once.
    internal static int Ok(Span<byte> destination, long rid, int groups)
    {
        throw new NotSupportedException();
    }

    // ERR <id> <code> <text>: ERR 0 for a refusal of the connection, or ERR <sid> subscriptions for a refused SUB. The
    // code is written as its code word of docs/protocol.md; text is a fixed sentence of at most
    // ProtocolRules.MaxTextLength characters, which never repeats what the client sent.
    internal static int Error(Span<byte> destination, long id, ProtocolErrorEnum code, string text)
    {
        throw new NotSupportedException();
    }

    // SIG <sid>: a signal handed to the subscription.
    internal static int Signal(Span<byte> destination, long sid)
    {
        throw new NotSupportedException();
    }

    // PONG: the answer to PING.
    internal static int Pong(Span<byte> destination)
    {
        throw new NotSupportedException();
    }

    // HELLO <version> <token>: the first line of a connection, with ProtocolRules.Version.
    internal static int Hello(Span<byte> destination, string token)
    {
        throw new NotSupportedException();
    }

    // SUB <sid> <key> <group>: opens the subscription <sid>.
    internal static int Sub(Span<byte> destination, long sid, string key, string group)
    {
        throw new NotSupportedException();
    }

    // WAIT <sid>: arms the member.
    internal static int Wait(Span<byte> destination, long sid)
    {
        throw new NotSupportedException();
    }

    // UNWAIT <sid>: ends the wait of the member.
    internal static int Unwait(Span<byte> destination, long sid)
    {
        throw new NotSupportedException();
    }

    // UNSUB <sid>: closes the subscription.
    internal static int Unsub(Span<byte> destination, long sid)
    {
        throw new NotSupportedException();
    }

    // SET <rid> <key>: sets the key and each of its ancestors.
    internal static int Set(Span<byte> destination, long rid, ReadOnlySpan<char> key)
    {
        throw new NotSupportedException();
    }

    // PING: the heartbeat of the client.
    internal static int Ping(Span<byte> destination)
    {
        throw new NotSupportedException();
    }
}
