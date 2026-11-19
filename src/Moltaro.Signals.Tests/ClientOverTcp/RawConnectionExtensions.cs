using System.Globalization;
using Moltaro.Signals.Tests.HostOverTcp;

namespace Moltaro.Signals.Tests.ClientOverTcp;

// Reading the lines of a client whose ids the client chooses.
internal static class RawConnectionExtensions
{
    // Reads the next line of the client, which has to be the verb, an id and then the rest, such as "SET", an id and
    // "jobs"; returns the id.
    public static async Task<long> ReadIdAsync(
        this RawConnection connection,
        string verb,
        string rest,
        CancellationToken cancellationToken)
    {
        string? line = await connection.ReadLineAsync(cancellationToken);

        Assert.NotNull(line);
        Assert.StartsWith($"{verb} ", line);
        Assert.EndsWith($" {rest}", line);

        string id = line[(verb.Length + 1)..^(rest.Length + 1)];

        return long.Parse(id, NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
