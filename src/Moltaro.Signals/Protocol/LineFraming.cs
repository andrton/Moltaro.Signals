namespace Moltaro.Signals.Protocol;

// The rules of Lines of docs/protocol.md that the lines of both sides share, for ClientLine.Read and HostLine.Read:
// where the first line of a buffer ends, and how its fields follow one another. Each reader keeps its own verbs and
// fields. It reads where the bytes lie and allocates nothing.
internal static class LineFraming
{
    // The fixed sentences of the refusals, which never repeat what was sent.
    private const string TooLong = "A line is longer than 282 characters.";

    private const string NoLineEnd = "A line does not end with CR LF.";

    private const string OutsideAscii = "A line holds a character outside U+0020 to U+007E.";

    private const string SpaceAtAnEnd = "A line is empty, or has a space at its start or end.";

    // Finds the first line of buffer. Incomplete when buffer holds no \n yet and fewer than ProtocolRules.LineRoom bytes.
    // Refused, with reason, when the line is too long, does not end with \r\n, holds a character outside U+0020 to
    // U+007E, is empty or has a space at either end. Otherwise Complete: text is the line without its \r\n, and length
    // counts the \r\n.
    internal static LineStatusEnum Find(
        ReadOnlySpan<byte> buffer,
        out ReadOnlySpan<byte> text,
        out int length,
        out string? reason)
    {
        text = default;
        length = 0;
        reason = null;

        int lineFeed = buffer[..Math.Min(buffer.Length, ProtocolRules.LineRoom)].IndexOf((byte)'\n');

        if (lineFeed < 0)
        {
            if (buffer.Length < ProtocolRules.LineRoom)
            {
                return LineStatusEnum.Incomplete;
            }

            reason = TooLong;
        }
        else if (lineFeed == 0 || buffer[lineFeed - 1] != '\r')
        {
            reason = NoLineEnd;
        }
        else
        {
            text = buffer[..(lineFeed - 1)];
            length = lineFeed + 1;

            if (text.ContainsAnyExceptInRange((byte)' ', (byte)'~'))
            {
                reason = OutsideAscii;
            }
            else if (text.IsEmpty || text[0] == ' ' || text[^1] == ' ')
            {
                reason = SpaceAtAnEnd;
            }
        }

        return reason is null ? LineStatusEnum.Complete : LineStatusEnum.Refused;
    }

    // Takes the field at the start of rest and leaves rest after the space that follows it. The field is empty when
    // rest is empty or starts with a space, where two spaces stand in a row.
    internal static ReadOnlySpan<byte> NextField(scoped ref ReadOnlySpan<byte> rest)
    {
        int space = rest.IndexOf((byte)' ');

        if (space < 0)
        {
            ReadOnlySpan<byte> last = rest;
            rest = default;

            return last;
        }

        ReadOnlySpan<byte> field = rest[..space];
        rest = rest[(space + 1)..];

        return field;
    }
}
