namespace Moltaro.Signals.Protocol;

// The rules of names and of the token, as the protocol fixes them, so that the host and its clients check the same
// limits; the bound on the length of a line follows from them. It knows nothing of events or sockets. Plain comments,
// not XML documentation, keep these internal rules out of the documentation file of the package.
internal static class ProtocolRules
{
    internal const int MaxNameLength = 128;

    internal const int MaxTokenLength = 256;

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
}
