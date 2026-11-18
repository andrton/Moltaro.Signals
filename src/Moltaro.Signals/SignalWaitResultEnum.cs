namespace Moltaro.Signals;

/// <summary>
/// Why <see cref="ISignalSubscription.WaitAsync"/> returned.
/// </summary>
/// <remarks>
/// Every result is followed by one full pass of the application; the result only says why. The value 0 is never
/// returned.
/// </remarks>
public enum SignalWaitResultEnum
{
    /// <summary>
    /// The first pass after subscribing, or after the subscription was opened again on a new connection.
    /// </summary>
    Joined = 1,

    /// <summary>
    /// A signal was handed to the member, or its wait found the group's event raised and reset it.
    /// </summary>
    Signaled = 2,

    /// <summary>
    /// The deadline of the wait came first.
    /// </summary>
    DeadlineReached = 3,
}
