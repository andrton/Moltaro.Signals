using System.Buffers.Text;
using System.Diagnostics;
using System.Text;

namespace Moltaro.Signals.Protocol;

// Writes the lines of docs/protocol.md: those the host sends to a client, and those the remote client sends to the
// host. Each method writes one line with its \r\n into destination, which has room for ProtocolRules.LineRoom bytes,
// and returns its length in bytes. It allocates nothing. The codec knows nothing of signal delivery or sockets.
internal static class LineFormatter
{
    // OK <id>: OK 0, the answer to an accepted HELLO, or OK <sid>, the answer to SUB or to UNWAIT.
    internal static int Ok(Span<byte> destination, long id)
    {
        int length = Word(destination, 0, "OK "u8);
        length = Number(destination, length, id);

        return End(destination, length);
    }

    // OK <rid> <groups>, the answer to SET: groups is the number of (key, group) pairs over the key and its ancestors
    // that have a member, each counted once.
    internal static int Ok(Span<byte> destination, long rid, int groups)
    {
        int length = Word(destination, 0, "OK "u8);
        length = Number(destination, length, rid);
        length = Word(destination, length, " "u8);
        length = Number(destination, length, groups);

        return End(destination, length);
    }

    // ERR <id> <code> <text>: ERR 0 for a refusal of the connection, or ERR <sid> subscriptions for a refused SUB. The
    // code is written as its code word of docs/protocol.md; text is a fixed sentence of at most
    // ProtocolRules.MaxTextLength characters, which never repeats what the client sent.
    internal static int Error(Span<byte> destination, long id, ProtocolErrorEnum code, string text)
    {
        int length = Word(destination, 0, "ERR "u8);
        length = Number(destination, length, id);
        length = Word(destination, length, " "u8);
        length = Word(destination, length, CodeWord(code));
        length = Word(destination, length, " "u8);
        length = Text(destination, length, text);

        return End(destination, length);
    }

    // SIG <sid>: a signal handed to the subscription.
    internal static int Signal(Span<byte> destination, long sid)
    {
        int length = Word(destination, 0, "SIG "u8);
        length = Number(destination, length, sid);

        return End(destination, length);
    }

    // PONG: the answer to PING.
    internal static int Pong(Span<byte> destination)
    {
        return End(destination, Word(destination, 0, "PONG"u8));
    }

    // HELLO <version> <token>: the first line of a connection, with ProtocolRules.Version.
    internal static int Hello(Span<byte> destination, string token)
    {
        int length = Word(destination, 0, "HELLO "u8);
        length = Number(destination, length, ProtocolRules.Version);
        length = Word(destination, length, " "u8);
        length = Text(destination, length, token);

        return End(destination, length);
    }

    // SUB <sid> <key> <group>: opens the subscription <sid>.
    internal static int Sub(Span<byte> destination, long sid, string key, string group)
    {
        int length = Word(destination, 0, "SUB "u8);
        length = Number(destination, length, sid);
        length = Word(destination, length, " "u8);
        length = Text(destination, length, key);
        length = Word(destination, length, " "u8);
        length = Text(destination, length, group);

        return End(destination, length);
    }

    // WAIT <sid>: arms the member.
    internal static int Wait(Span<byte> destination, long sid)
    {
        int length = Word(destination, 0, "WAIT "u8);
        length = Number(destination, length, sid);

        return End(destination, length);
    }

    // UNWAIT <sid>: ends the wait of the member.
    internal static int Unwait(Span<byte> destination, long sid)
    {
        int length = Word(destination, 0, "UNWAIT "u8);
        length = Number(destination, length, sid);

        return End(destination, length);
    }

    // UNSUB <sid>: closes the subscription.
    internal static int Unsub(Span<byte> destination, long sid)
    {
        int length = Word(destination, 0, "UNSUB "u8);
        length = Number(destination, length, sid);

        return End(destination, length);
    }

    // SET <rid> <key>: sets the key and each of its ancestors.
    internal static int Set(Span<byte> destination, long rid, ReadOnlySpan<char> key)
    {
        int length = Word(destination, 0, "SET "u8);
        length = Number(destination, length, rid);
        length = Word(destination, length, " "u8);
        length = Text(destination, length, key);

        return End(destination, length);
    }

    // PING: the heartbeat of the client.
    internal static int Ping(Span<byte> destination)
    {
        return End(destination, Word(destination, 0, "PING"u8));
    }

    // The code words of docs/protocol.md, in lower case.
    private static ReadOnlySpan<byte> CodeWord(ProtocolErrorEnum code)
    {
        return code switch
        {
            ProtocolErrorEnum.Version => "version"u8,
            ProtocolErrorEnum.Token => "token"u8,
            ProtocolErrorEnum.Connections => "connections"u8,
            ProtocolErrorEnum.Protocol => "protocol"u8,
            ProtocolErrorEnum.Subscriptions => "subscriptions"u8,
            _ => throw new UnreachableException(),
        };
    }

    // Each step below writes at length and returns the length after what it wrote.
    private static int Word(Span<byte> destination, int length, ReadOnlySpan<byte> word)
    {
        word.CopyTo(destination[length..]);

        return length + word.Length;
    }

    private static int Number(Span<byte> destination, int length, long value)
    {
        Utf8Formatter.TryFormat(value, destination[length..], out int written);

        return length + written;
    }

    // A name, a token or a text, whose characters are ASCII by their rules.
    private static int Text(Span<byte> destination, int length, ReadOnlySpan<char> text)
    {
        return length + Encoding.ASCII.GetBytes(text, destination[length..]);
    }

    private static int End(Span<byte> destination, int length)
    {
        return Word(destination, length, "\r\n"u8);
    }
}
