namespace Moltaro.Signals.Protocol;

// The rules of the protocol that the host, its clients and the codec share, as docs/protocol.md fixes them: its version,
// the rules of names and of the token, and the bounds of an id, a text and a line, so that every side checks the same
// limits. It knows nothing of signal delivery or sockets. Plain comments, not XML documentation, keep these internal
// rules out of the documentation file of the package.
internal static class ProtocolRules
{
    internal const int MaxNameLength = 128;

    internal const int MaxTokenLength = 256;

    // The version of the protocol, which a client sends in its HELLO. A host speaks this one version and refuses
    // another with the code version.
    internal const int Version = 1;

    // The most digits of a <sid>, a <rid> or a <version>: 1 to 19 digits with no leading zero, at most 2^63-1.
    internal const int MaxIdLength = 19;

    // The most characters of the <text> of an ERR line: a fixed sentence for a person, which never repeats what the
    // client sent.
    internal const int MaxTextLength = 128;

    // The most characters of a line before its \r\n: the length of the longest HELLO, with a version of MaxIdLength
    // digits and a token of MaxTokenLength characters.
    internal const int MaxLineLength = 282;

    // The most bytes of a line with its \r\n: the room for one line, and the bytes after which a buffer with no \n holds
    // a line that is too long.
    internal const int LineRoom = MaxLineLength + 2;

    // True for 1 to MaxNameLength characters, each one of A-Z a-z 0-9 . _ : -, where '.' separates segments and no
    // segment is empty.
    internal static bool IsName(ReadOnlySpan<char> value)
    {
        if (value.Length is 0 or > MaxNameLength)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or ':' or '-'))
            {
                return false;
            }
        }

        // A '.' at either end, or two in a row, would leave a segment empty.
        return value[0] != '.' && value[^1] != '.' && !value.Contains("..", StringComparison.Ordinal);
    }

    // True for 1 to MaxTokenLength characters, each from U+0021 to U+007E: printable ASCII without the space.
    internal static bool IsToken(ReadOnlySpan<char> value)
    {
        return value.Length is > 0 and <= MaxTokenLength && !value.ContainsAnyExceptInRange('!', '~');
    }

    // True for an id as its ASCII bytes: 1 to MaxIdLength digits with no leading zero, from 1 to 2^63-1, given in id.
    // A 0 is no id: the host writes it only for the connection itself.
    internal static bool IsId(ReadOnlySpan<byte> value, out long id)
    {
        id = 0;

        if (value.Length is 0 or > MaxIdLength || value[0] == '0')
        {
            return false;
        }

        // MaxIdLength digits stay below 2^64, so the value cannot overflow before it is compared.
        ulong number = 0;

        foreach (byte digit in value)
        {
            if (!char.IsAsciiDigit((char)digit))
            {
                return false;
            }

            number = (number * 10) + (ulong)(digit - '0');
        }

        if (number > long.MaxValue)
        {
            return false;
        }

        id = (long)number;

        return true;
    }
}
