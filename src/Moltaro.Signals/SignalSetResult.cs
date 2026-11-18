namespace Moltaro.Signals;

/// <summary>
/// The result of <see cref="ISignals.SetAsync"/>: whether the host applied the set, and how many (key, group) pairs
/// listened, over the key and its ancestors.
/// </summary>
/// <remarks>
/// Only the library creates a result that claims a success. <c>default(SignalSetResult)</c> is
/// <see cref="SignalSetStatusEnum.Unavailable"/> with 0 groups, so a default never claims one. Code that tests its
/// own use of <see cref="ISignals"/> uses a <see cref="SignalHost"/> in-process, which needs no socket, instead of a
/// made-up result.
/// </remarks>
public readonly record struct SignalSetResult
{
    // The one way to a result that claims a success; only the library creates one.
    internal SignalSetResult(SignalSetStatusEnum status, int groups)
    {
        Status = status;
        Groups = groups;
    }

    /// <summary>
    /// <see cref="SignalSetStatusEnum.Applied"/> when the host applied the set,
    /// <see cref="SignalSetStatusEnum.Unavailable"/> when the host did not confirm it, as
    /// <see cref="ISignals.SetAsync"/> says.
    /// </summary>
    public SignalSetStatusEnum Status { get; }

    /// <summary>
    /// For <see cref="SignalSetStatusEnum.Applied"/>, the number of (key, group) pairs over the key and its ancestors
    /// that had at least one live member when the host applied the set, each counted once, 0 or more; 0 means nobody
    /// listens. For <see cref="SignalSetStatusEnum.Unavailable"/>, 0.
    /// </summary>
    public int Groups { get; }
}
