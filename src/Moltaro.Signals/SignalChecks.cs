using Moltaro.Signals.Protocol;

namespace Moltaro.Signals;

// The argument checks that the public members of the host and of the client share, so that each lives once. Each
// check reports the ParamName it is given.
internal static class SignalChecks
{
    // The longest timer of TimeProvider: the most a TimeSpan option may be, and the bound beyond which a deadline of
    // WaitAsync counts as none.
    internal static readonly TimeSpan LongestTimer = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    internal static void CheckName(string? name, string paramName)
    {
        ArgumentNullException.ThrowIfNull(name, paramName);

        if (!ProtocolRules.IsName(name))
        {
            throw new ArgumentException(
                $"A name is 1 to {ProtocolRules.MaxNameLength} characters, each one of A-Z a-z 0-9 . _ : -, and no segment between the dots is empty.",
                paramName);
        }
    }

    // A token left null is a required property left null, so it gives ArgumentException as a broken token does.
    internal static void CheckToken(string? token, string paramName)
    {
        if (token is null || !ProtocolRules.IsToken(token))
        {
            throw new ArgumentException(
                $"The token is required: 1 to {ProtocolRules.MaxTokenLength} characters, each from U+0021 to U+007E.",
                paramName);
        }
    }

    internal static void CheckTimeSpan(TimeSpan value, string paramName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero, paramName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, LongestTimer, paramName);
    }
}
