namespace Moltaro.Signals;

/// <summary>
/// The arguments of <see cref="ISignals.StateChanged"/>: the state that a change of the connection reports, and
/// why the connection ended.
/// </summary>
/// <remarks>
/// The arguments carry the state of the change they report; by the time a handler runs,
/// <see cref="ISignals.State"/> may have moved on. Only the library creates them.
/// </remarks>
public sealed class SignalConnectionChangedEventArgs : EventArgs
{
    internal SignalConnectionChangedEventArgs(SignalConnectionStateEnum state, string? reason)
    {
        State = state;
        Reason = reason;
    }

    /// <summary>
    /// The state this notification reports.
    /// </summary>
    public SignalConnectionStateEnum State { get; }

    /// <summary>
    /// <see langword="null"/> for <see cref="SignalConnectionStateEnum.Connected"/>. For
    /// <see cref="SignalConnectionStateEnum.Disconnected"/>, why: when the host refused the attempt, its code
    /// (<c>version</c>, <c>token</c>, <c>connections</c> or <c>protocol</c>), then its text; otherwise a sentence
    /// such as "closed by the host" or "no traffic for DeadPeerTimeout".
    /// </summary>
    /// <remarks>
    /// The code at the start is part of the contract; the text after it is not.
    /// </remarks>
    public string? Reason { get; }
}
