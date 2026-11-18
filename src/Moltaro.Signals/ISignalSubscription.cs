namespace Moltaro.Signals;

/// <summary>
/// One member of a group on one key, created by <see cref="ISignals.Subscribe"/>.
/// </summary>
/// <remarks>
/// <para>
/// Only one <see cref="WaitAsync"/> of a subscription may be pending at a time. Otherwise its members may be called
/// from any thread at any time.
/// </para>
/// <para>
/// <see cref="IDisposable.Dispose"/> ends the membership and returns at once, without waiting for the host. If the
/// member is busy, the host hands its signal on: in-process at once, remotely when it receives the unsubscription or
/// loses the connection. A pending <see cref="WaitAsync"/> ends with <see cref="ObjectDisposedException"/>; a
/// signal the host handed to it at that moment makes the member busy on the host, so the host hands it on; it is
/// not lost. Calling it again does nothing. Disposing the <see cref="SignalHost"/> or <see cref="SignalClient"/>
/// that created the subscription ends it in the same way; its own dispose then does nothing.
/// </para>
/// </remarks>
public interface ISignalSubscription : IDisposable
{
    /// <summary>
    /// Returns when the member should do one pass, and says why.
    /// </summary>
    /// <param name="until">
    /// The member's own deadline, an instant compared with the <see cref="TimeProvider"/> of the object that created
    /// the subscription. <see langword="null"/> means no deadline, and so does a deadline further away than the
    /// longest timer of <see cref="TimeProvider"/>, about 49.7 days.
    /// </param>
    /// <param name="cancellationToken">
    /// Ends the wait, unless the host had already handed a signal to it. A token that is already cancelled gives a
    /// cancelled task, and the member stays as it was.
    /// </param>
    /// <returns>
    /// <see cref="SignalWaitResultEnum.Joined"/>, <see cref="SignalWaitResultEnum.Signaled"/> or
    /// <see cref="SignalWaitResultEnum.DeadlineReached"/>, by the rules below. Every result is followed by one full
    /// pass of the application.
    /// </returns>
    /// <remarks>
    /// <para>The rules, in this order:</para>
    /// <list type="number">
    /// <item><description>
    /// <b>Joined.</b> If a <see cref="SignalWaitResultEnum.Joined"/> is due and the subscription is open on a host,
    /// the call returns <c>Joined</c> at once. A <c>Joined</c> is due after <see cref="ISignals.Subscribe"/> and after
    /// every reopening on a new connection, until a wait returns it. A remote subscription is open only once the host
    /// has answered its opening; until then the call waits as rule 5 says. It does not return <c>Joined</c> before
    /// that answer, because only an open member makes a set during the pass raise the event; a set to a group
    /// without members is not remembered.
    /// </description></item>
    /// <item><description>
    /// <b>A deadline already passed.</b> The call returns <see cref="SignalWaitResultEnum.DeadlineReached"/> at once,
    /// without asking the host, and changes nothing.
    /// </description></item>
    /// <item><description>
    /// <b>Signaled.</b> Otherwise the call asks the host. If the group's event is raised, it is reset and the call
    /// returns <see cref="SignalWaitResultEnum.Signaled"/>; otherwise the member waits until a signal is handed to
    /// it.
    /// </description></item>
    /// <item><description>
    /// <b>Deadline and cancellation.</b> When <paramref name="until"/> is reached or the token is cancelled, the
    /// wait is ended with the host: atomically in-process, and remotely by asking the host and waiting for its
    /// answer. If the host had already handed a signal, the call returns <see cref="SignalWaitResultEnum.Signaled"/>,
    /// even though cancellation was requested. Otherwise it returns <see cref="SignalWaitResultEnum.DeadlineReached"/>,
    /// or ends with <see cref="OperationCanceledException"/>.
    /// </description></item>
    /// <item><description>
    /// <b>Not connected.</b> While the client is not connected, or the subscription is not open yet, the call waits
    /// until the subscription is open on a host and then returns <see cref="SignalWaitResultEnum.Joined"/>. Its
    /// deadline ends it at once with <see cref="SignalWaitResultEnum.DeadlineReached"/>, its token at once with
    /// <see cref="OperationCanceledException"/>; there is no host to ask. A connection lost while the member waits
    /// leads here: the host ended the member, and handed its signal on if the member was busy.
    /// </description></item>
    /// </list>
    /// <para>
    /// After <c>Joined</c> or <c>Signaled</c> the member is busy until its next wait: a set meanwhile raises the
    /// group's event, so the next call returns at once, and leaving meanwhile hands the signal on. After a wait that
    /// ended under rule 4 without a signal, the member holds no signal: a set raises the group's event, and leaving
    /// hands nothing on. A call that ends under rule 2 or 5, or whose token was already cancelled, leaves the member
    /// as it was.
    /// </para>
    /// <para>
    /// Handing a signal on is handled as a set of that one (key, group), without its ancestors: a member of the
    /// group on that key that waits receives it; otherwise the event becomes raised; when the leaving member was the
    /// last of its group on that key, nothing is kept. So an event is never raised while a member of its group
    /// waits. The instrument <c>moltaro.signals.sets</c> does not count a signal handed on.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown by the call when another wait of this subscription has not completed; that one is not affected.
    /// Reported by the task, with the host's text, when the host refused to open the subscription because its
    /// connection already had as many open as <see cref="SignalHostOptions.MaxSubscriptionsPerConnection"/> allows.
    /// A refused subscription is closed for good: every later wait reports the same exception through its task, and
    /// it is not opened again after a reconnect.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown by the call when the subscription or the object that created it was disposed. Reported by the task
    /// when either is disposed while the call waits.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Reported by the task when <paramref name="cancellationToken"/> was cancelled and no signal was handed to the
    /// wait.
    /// </exception>
    ValueTask<SignalWaitResultEnum> WaitAsync(DateTimeOffset? until, CancellationToken cancellationToken = default);
}
