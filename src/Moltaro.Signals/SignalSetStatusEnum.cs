namespace Moltaro.Signals;

/// <summary>
/// Whether the host applied a set, as <see cref="SignalSetResult.Status"/> reports it.
/// </summary>
public enum SignalSetStatusEnum
{
    /// <summary>
    /// The host did not confirm the set: the client was not connected, or its connection was lost before the host's
    /// answer. In the second case the set may have taken effect; repeating it is harmless, because a set is
    /// idempotent. It is the status of <c>default(SignalSetResult)</c>.
    /// </summary>
    Unavailable = 0,

    /// <summary>
    /// The host applied the set; <see cref="SignalSetResult.Groups"/> says how many (key, group) pairs, over the key
    /// and its ancestors, had a live member.
    /// </summary>
    Applied = 1,
}
