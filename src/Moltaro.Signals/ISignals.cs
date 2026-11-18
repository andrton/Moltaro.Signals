namespace Moltaro.Signals;

/// <summary>
/// Remote signals with consumer groups: the one interface of code inside the host process and of code in other
/// processes, with the same semantics.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SignalHost"/> implements it for code inside the host process, without a socket;
/// <see cref="SignalClient"/> for code in other processes, over one TCP connection. Application code does not
/// branch on where the host is. A signal carries no data, has no history and is not persisted: it says "look now",
/// coalesces per (key, group), and wakes one waiting member of each group on each key it sets. Receivers read the
/// facts from their own storage.
/// </para>
/// <para>
/// The rules act on one (key, group) at a time: members of one group on a key and on an ancestor of it are separate
/// members with separate pending signals, so a set of <c>a.b</c> wakes one waiting member of group <c>g</c> on
/// <c>a.b</c> and one on <c>a</c>.
/// </para>
/// <para>
/// It is not disposable: whoever created the object disposes it, a container included.
/// </para>
/// <para>
/// Every member may be called from any thread at any time, except where a member says otherwise. An argument
/// error, <see cref="ObjectDisposedException"/> after a dispose and <see cref="InvalidOperationException"/> for a
/// misuse are thrown by the call itself, before anything happens. An outcome or a failure of the operation is
/// reported by the returned task. A token that is already cancelled when a method is called gives a cancelled task,
/// and the method changes nothing. Expected outcomes are results, not exceptions:
/// <see cref="SignalSetStatusEnum.Unavailable"/>, zero groups, <see cref="SignalWaitResultEnum.Joined"/>,
/// <see cref="SignalWaitResultEnum.DeadlineReached"/>. A returned <see cref="ValueTask"/> is awaited once, as any
/// <see cref="ValueTask"/>.
/// </para>
/// <para>
/// Application code that continues after awaiting a task of this library never runs inside another call of the
/// library, and never on the thread that reads a connection. A set that wakes a waiting member does not run that
/// member's code: the member continues on the thread pool, and the set returns without waiting for it. A remote
/// member's pass, however long, never holds up the replies, heartbeats and signals of its client's connection. A
/// task that completes synchronously, such as an in-process set, returns to its own caller as any call does.
/// </para>
/// <para>
/// A name is a key or a group, by one rule for both: 1 to 128 characters, each one of <c>A–Z a–z 0–9 . _ : -</c>,
/// where <c>.</c> separates segments and no segment is empty (not <c>a..b</c>, <c>.a</c> or <c>a.</c>). Names are
/// compared exactly, case included. The segments of a key make its ancestors: <c>a.b</c> and <c>a</c> are the
/// ancestors of <c>a.b.c</c>. The segments of a group mean nothing. The protocol fixes the limits of names and of
/// the token, so the host and its clients check the same ones.
/// </para>
/// </remarks>
public interface ISignals
{
    /// <summary>
    /// Whether this object can reach its host, answered from local memory.
    /// </summary>
    /// <remarks>
    /// For a client, <see cref="SignalConnectionStateEnum.Connected"/> once the host accepted its <c>HELLO</c>, and
    /// <see cref="SignalConnectionStateEnum.Disconnected"/> otherwise, while it tries to connect. For the in-process
    /// <see cref="ISignals"/> of a host, <see cref="SignalConnectionStateEnum.Connected"/> from its construction until
    /// it is disposed, and <see cref="SignalConnectionStateEnum.Disconnected"/> afterwards. A connection whose peer
    /// died is reported connected only until the dead peer is detected, at most
    /// <see cref="SignalClientOptions.DeadPeerTimeout"/>.
    /// </remarks>
    SignalConnectionStateEnum State { get; }

    /// <summary>
    /// Raised by a client when its connection changes: with <see cref="SignalConnectionStateEnum.Connected"/> when
    /// the host accepts its <c>HELLO</c>, and with <see cref="SignalConnectionStateEnum.Disconnected"/> when a
    /// connection ends or the host refuses an attempt with its code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An attempt that ends without the host's answer to its <c>HELLO</c> raises nothing: one that reaches no host,
    /// gets no answer within <see cref="SignalClientOptions.DeadPeerTimeout"/>, or sees a plain end of the
    /// connection. A dispose raises nothing, and the in-process <see cref="ISignals"/> of a host never raises it.
    /// </para>
    /// <para>
    /// A handler runs on a thread-pool thread, never synchronously inside a call that an application made to this
    /// library, and never while the library holds a lock. The notifications of one object are delivered one at a
    /// time, in the order of the changes. A handler that blocks delays only the later notifications of that object;
    /// heartbeats, sets and waits go on.
    /// </para>
    /// <para>
    /// The arguments carry the state of the change they report; by the time a handler runs, <see cref="State"/> may
    /// have moved on. A handler that needs the state from the start attaches itself, then reads <see cref="State"/>;
    /// the object is started after both.
    /// </para>
    /// <para>
    /// An exception thrown by a handler is not caught by the library: it is an unhandled exception of the process.
    /// </para>
    /// <para>
    /// After the object's dispose has been called, no notification starts; one that is already running may finish.
    /// A dispose does not wait for a handler that is running: a handler may dispose the object that raised it, and
    /// a handler that blocks does not hold up a dispose.
    /// </para>
    /// </remarks>
    event EventHandler<SignalConnectionChangedEventArgs>? StateChanged;

    /// <summary>
    /// Sets a key and each of its ancestors, for every (key, group) among these keys that has members on the host.
    /// </summary>
    /// <param name="key">The key that is set; a name, as the remarks of <see cref="ISignals"/> define it.</param>
    /// <param name="cancellationToken">
    /// Ends the wait for the host's answer. A token that is already cancelled gives a cancelled task, and nothing is
    /// set.
    /// </param>
    /// <returns>
    /// <see cref="SignalSetStatusEnum.Applied"/> with <see cref="SignalSetResult.Groups"/>, the number of (key, group)
    /// pairs over the key and its ancestors that had at least one live member when the host applied the set, each
    /// counted once; 0 means nobody listens.
    /// <see cref="SignalSetStatusEnum.Unavailable"/> when the set was not confirmed by the host: the client was not
    /// connected, or its connection was lost before the host's answer. In the second case the set may have taken
    /// effect; repeating it is harmless.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A set of <c>a.b.c</c> acts as a set of <c>a.b.c</c>, <c>a.b</c> and <c>a</c>. It sets no descendant and knows
    /// no wildcard. For every (key, group) among these keys that has members: if a member of the group on that key
    /// waits, the signal is handed to exactly one such member; otherwise the signal becomes pending. A set while a
    /// signal is pending does nothing, and coalescing stays per (key, group); sets are never accumulated, queued or
    /// replayed. A group with no members on a key keeps no state there: the set is not remembered for it.
    /// </para>
    /// <para>
    /// In-process, and on a client that is not connected, the call completes synchronously: no network, no queue,
    /// no retry. On a connected client it sends the set and waits for the host's answer, which carries the group
    /// count; that wait ends at the latest when the connection is declared dead, within
    /// <see cref="SignalClientOptions.DeadPeerTimeout"/>. A set never waits for a connection to come.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown by the call: <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown by the call: <paramref name="key"/> is not a name.</exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown by the call when the object was disposed. Reported by the task when the client is disposed while the
    /// call waits for the host's answer.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Reported by the task: <paramref name="cancellationToken"/> was cancelled. The set may have reached the host.
    /// </exception>
    ValueTask<SignalSetResult> SetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates one member of a group on a key and returns it at once.
    /// </summary>
    /// <param name="key">The key of the member; a name, as the remarks of <see cref="ISignals"/> define it.</param>
    /// <param name="group">The group the member joins; a name.</param>
    /// <returns>The new member; disposing it ends the membership.</returns>
    /// <remarks>
    /// <para>
    /// Several subscriptions with the same key and group are several members, for example the lanes of one worker.
    /// The members of one group on one key share each signal; every group gets its own. Sets of the key's descendants
    /// reach the member too, because a set also sets the ancestors of its key.
    /// </para>
    /// <para>
    /// It answers from local memory and never fails because a host is unreachable: a remote subscription is opened
    /// on the host while the client is connected, and opened again after every reconnect; a refusal by the host
    /// shows in <see cref="ISignalSubscription.WaitAsync"/>. A new subscription is busy until its first wait, which
    /// returns <see cref="SignalWaitResultEnum.Joined"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown by the call: <paramref name="key"/> or <paramref name="group"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown by the call: <paramref name="key"/> or <paramref name="group"/> is not a name.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown by the call: the object was disposed.</exception>
    ISignalSubscription Subscribe(string key, string group);
}
