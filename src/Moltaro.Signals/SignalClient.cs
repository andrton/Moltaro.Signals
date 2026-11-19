using System.Net;

namespace Moltaro.Signals;

/// <summary>
/// The <see cref="ISignals"/> of a process other than the host's, over one persistent TCP connection to the host.
/// </summary>
/// <remarks>
/// <para>
/// Every member may be called from any thread at any time. The client follows the rules that
/// <see cref="ISignals"/> describes.
/// </para>
/// <para>
/// <b>Liveness.</b> While connected, the client sends <c>PING</c> when it has sent nothing, or received nothing,
/// for <see cref="SignalClientOptions.HeartbeatInterval"/>, and at most one <c>PING</c> in that time; it declares
/// the connection dead when it has received nothing for <see cref="SignalClientOptions.DeadPeerTimeout"/>. A
/// connection that ends for any reason gives <see cref="SignalConnectionStateEnum.Disconnected"/> with its reason,
/// then reconnection after <see cref="SignalClientOptions.ReconnectDelay"/>. TCP keepalive is not relied on.
/// </para>
/// <para>
/// <b>Order.</b> While connected, the requests of the client reach the host in the order in which the calls were
/// made. Concurrent sets share the one connection; none waits for another.
/// </para>
/// </remarks>
public sealed partial class SignalClient : ISignals, IAsyncDisposable
{
    // The fixed sentences of the reasons of Disconnected, beside the code and text of a refusal by the host and the
    // reason of a line of the host that breaks the grammar.
    private const string ClosedByHost = "The host closed the connection.";

    private const string NoTraffic = "Nothing came from the host for DeadPeerTimeout.";

    private const string ConnectionFailed = "The connection to the host failed.";

    private const string NotOutstanding = "The host answered a request that is not outstanding.";

    // The one lock of the client. It guards _connection, _subscriptions, _lastId, _notifications, _notifying, _running,
    // _disposal, the change of _state, the wait fields of every subscription, and of every connection its output, its
    // sets, its acceptance and its end. Under it the client runs no code of the application except, for deadlines and
    // heartbeats, TimeProvider.CreateTimer with no due time, ITimer.Change with a positive one and
    // TimeProvider.GetElapsedTime, which computes from two timestamps, so a TimeProvider does not call back inside them.
    // Under it the client also appends a line to the output of its connection and starts the flush of that output, and
    // starts the delivery of notifications, each on the thread pool and never inline. The clock is read before it is
    // taken; waits and sets are completed, cancellations registered and sockets closed after it is released.
    private readonly Lock _lock = new();

    private readonly EndPoint _hostEndPoint;

    private readonly string _token;

    private readonly TimeSpan _heartbeatInterval;

    private readonly TimeSpan _deadPeerTimeout;

    private readonly TimeSpan _reconnectDelay;

    private readonly TimeSpan _maxReconnectDelay;

    private readonly TimeProvider _timeProvider;

    // Cancelled by DisposeAsync: it ends the wait for the next attempt, and the attempt that runs.
    private readonly CancellationTokenSource _stopping = new();

    // The open subscriptions by their <sid>, which each keeps across connections: those a connection opens again when
    // the host accepts it.
    private readonly Dictionary<long, RemoteSubscription> _subscriptions = [];

    // The notifications not delivered yet, in the order of the changes. While a handler blocks, this grows by one for
    // each later change; nothing else makes it grow, since every change is delivered.
    private readonly Queue<SignalConnectionChangedEventArgs> _notifications = new();

    // The one counter of the client, from which every <sid> and <rid> is taken, so that no id is used twice on any
    // connection.
    private long _lastId;

    // The attempt or the connection that runs; null between attempts and once its end has begun.
    private Connection? _connection;

    // Connected from the section that accepts a connection to the section that begins its end. Read without the lock.
    private volatile SignalConnectionStateEnum _state;

    // Whether a work item delivers the notifications now; at most one does.
    private bool _notifying;

    // The loop that runs the attempts, from Start until the client is disposed.
    private Task? _running;

    // Null until the client is disposed. The first DisposeAsync creates it under _lock and completes it once the loop of
    // the attempts has ended; a later call returns its task.
    private TaskCompletionSource? _disposal;

    /// <summary>
    /// Creates a client of a host elsewhere. It does not connect: <see cref="Start"/> does, so that handlers of
    /// <see cref="StateChanged"/> can be attached first.
    /// </summary>
    /// <param name="options">The options of the client, validated and copied: a later change of them has no effect.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A <see cref="TimeSpan"/> of the options is not greater than zero or longer than the longest timer of
    /// <see cref="TimeProvider"/>. The <see cref="ArgumentException.ParamName"/> is the name of the property.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <see cref="SignalClientOptions.HostEndPoint"/> is neither an <see cref="IPEndPoint"/> nor a
    /// <see cref="DnsEndPoint"/>; <see cref="SignalClientOptions.Token"/> is <see langword="null"/> or breaks the rule
    /// of the token; <see cref="SignalClientOptions.TimeProvider"/> is <see langword="null"/>; or
    /// <see cref="SignalClientOptions.DeadPeerTimeout"/> is not greater than
    /// <see cref="SignalClientOptions.HeartbeatInterval"/>, or <see cref="SignalClientOptions.MaxReconnectDelay"/> is
    /// less than <see cref="SignalClientOptions.ReconnectDelay"/>. The <see cref="ArgumentException.ParamName"/> is the
    /// name of the property; for two options that contradict each other, <c>DeadPeerTimeout</c> or
    /// <c>MaxReconnectDelay</c>.
    /// </exception>
    public SignalClient(SignalClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.HostEndPoint is not (IPEndPoint or DnsEndPoint))
        {
            throw new ArgumentException(
                "The host to connect to is required, as an IPEndPoint or a DnsEndPoint.",
                nameof(SignalClientOptions.HostEndPoint));
        }

        SignalChecks.CheckToken(options.Token, nameof(SignalClientOptions.Token));

        SignalChecks.CheckTimeSpan(options.HeartbeatInterval, nameof(SignalClientOptions.HeartbeatInterval));
        SignalChecks.CheckTimeSpan(options.DeadPeerTimeout, nameof(SignalClientOptions.DeadPeerTimeout));

        if (options.DeadPeerTimeout <= options.HeartbeatInterval)
        {
            throw new ArgumentException(
                "DeadPeerTimeout has to be greater than HeartbeatInterval.",
                nameof(SignalClientOptions.DeadPeerTimeout));
        }

        SignalChecks.CheckTimeSpan(options.ReconnectDelay, nameof(SignalClientOptions.ReconnectDelay));
        SignalChecks.CheckTimeSpan(options.MaxReconnectDelay, nameof(SignalClientOptions.MaxReconnectDelay));

        if (options.MaxReconnectDelay < options.ReconnectDelay)
        {
            throw new ArgumentException(
                "MaxReconnectDelay has to be at least ReconnectDelay.",
                nameof(SignalClientOptions.MaxReconnectDelay));
        }

        if (options.TimeProvider is null)
        {
            throw new ArgumentException("The time provider is required.", nameof(SignalClientOptions.TimeProvider));
        }

        // An IPEndPoint can be changed after it was given, so the client keeps a copy; a name is resolved again for every
        // attempt by the connect.
        _hostEndPoint = options.HostEndPoint is IPEndPoint address
            ? new IPEndPoint(address.Address, address.Port)
            : options.HostEndPoint;
        _token = options.Token!;
        _heartbeatInterval = options.HeartbeatInterval;
        _deadPeerTimeout = options.DeadPeerTimeout;
        _reconnectDelay = options.ReconnectDelay;
        _maxReconnectDelay = options.MaxReconnectDelay;
        _timeProvider = options.TimeProvider;
    }

    /// <summary>
    /// <see cref="SignalConnectionStateEnum.Connected"/> once the host accepted the <c>HELLO</c> of the client, and
    /// <see cref="SignalConnectionStateEnum.Disconnected"/> otherwise: before <see cref="Start"/>, while the client
    /// tries to connect, and after <see cref="DisposeAsync"/>. Answered from local memory.
    /// </summary>
    /// <remarks>
    /// A connection whose host died is reported connected only until the dead peer is detected, at most
    /// <see cref="SignalClientOptions.DeadPeerTimeout"/>.
    /// </remarks>
    public SignalConnectionStateEnum State => _state;

    /// <summary>
    /// Raised with <see cref="SignalConnectionStateEnum.Connected"/> when the host accepts the <c>HELLO</c> of the
    /// client, and with <see cref="SignalConnectionStateEnum.Disconnected"/> when a connection ends or the host
    /// refuses an attempt with its code.
    /// </summary>
    /// <remarks>
    /// An attempt that ends without the host's answer to its <c>HELLO</c> raises nothing: one that reaches no host,
    /// gets no answer within <see cref="SignalClientOptions.DeadPeerTimeout"/>, or sees a plain end of the
    /// connection. Neither does a dispose. The handlers run by the rules that
    /// <see cref="ISignals.StateChanged"/> describes: on a thread-pool thread, one at a time in the order of the
    /// changes, never inside a call of the application and never under a lock of the library; a dispose does not
    /// wait for them.
    /// </remarks>
    public event EventHandler<SignalConnectionChangedEventArgs>? StateChanged;

    /// <summary>
    /// Begins connecting in the background and returns at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first attempt is immediate. An attempt connects, sends <c>HELLO</c> with the protocol version and the
    /// token, and ends within <see cref="SignalClientOptions.DeadPeerTimeout"/>. When the host accepts, the client
    /// sends the reopening of every open subscription and only then reports
    /// <see cref="SignalConnectionStateEnum.Connected"/>, so a request made after the change to <c>Connected</c>
    /// reaches the host after those subscriptions; each subscription is open, and its wait returns
    /// <see cref="SignalWaitResultEnum.Joined"/>, once the host answered it.
    /// </para>
    /// <para>
    /// So <see cref="State"/> is <see cref="SignalConnectionStateEnum.Connected"/> before any
    /// <see cref="SignalWaitResultEnum.Joined"/> of that connection: the client sets it in the same step in which it
    /// writes the reopenings, and reads the host's answers to them only after that step. The notification of
    /// <c>Connected</c> is not ordered against a <c>Joined</c>: its handler runs on the thread pool, apart from any
    /// wait, and <see cref="State"/> may have moved on by the time either is seen.
    /// </para>
    /// <para>
    /// When the host refuses or no host answers, the client stays <see cref="SignalConnectionStateEnum.Disconnected"/>
    /// and tries again with backoff: after <see cref="SignalClientOptions.ReconnectDelay"/>, doubled after each failed
    /// attempt up to <see cref="SignalClientOptions.MaxReconnectDelay"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The client was started before.</exception>
    /// <exception cref="ObjectDisposedException">The client was disposed.</exception>
    public void Start()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);

            if (_running is not null)
            {
                throw new InvalidOperationException("The client was started before.");
            }

            // The loop outlives the call, so it does not capture the execution context of the caller; nor does anything
            // it starts. It runs on the thread pool, so Start returns at once.
            using AsyncFlowControl? flow = ExecutionContext.IsFlowSuppressed() ? null : ExecutionContext.SuppressFlow();

            _running = Task.Run(RunAttemptsAsync);
        }
    }

    /// <summary>
    /// Sets a key and each of its ancestors on the host, for every (key, group) among these keys that has members
    /// there.
    /// </summary>
    /// <param name="key">The key that is set; a name, as the remarks of <see cref="ISignals"/> define it.</param>
    /// <param name="cancellationToken">
    /// Ends the wait for the host's answer. A token that is already cancelled gives a cancelled task, and nothing is
    /// sent.
    /// </param>
    /// <returns>
    /// <see cref="SignalSetStatusEnum.Applied"/> with <see cref="SignalSetResult.Groups"/>, the number of (key, group)
    /// pairs over the key and its ancestors that had at least one live member when the host applied the set, each
    /// counted once; 0 means nobody listens.
    /// <see cref="SignalSetStatusEnum.Unavailable"/> when the host did not confirm the set: the client was not
    /// connected, or its connection was lost before the host's answer. In the second case the set may have taken
    /// effect; repeating it is harmless.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A set of <c>a.b.c</c> acts as a set of <c>a.b.c</c>, <c>a.b</c> and <c>a</c>, as
    /// <see cref="ISignals.SetAsync"/> says: one waiting member of a group is woken on each of these keys. It sets no
    /// descendant and knows no wildcard; coalescing stays per (key, group).
    /// </para>
    /// <para>
    /// While the client is not connected, the call completes synchronously with
    /// <see cref="SignalSetStatusEnum.Unavailable"/>: no network, no queue, no retry, and the set never reaches the
    /// host later. While it is connected, it sends the set and waits for the host's answer, which carries the group
    /// count; that wait ends at the latest when the connection is declared dead, within
    /// <see cref="SignalClientOptions.DeadPeerTimeout"/>. A set never waits for a connection to come.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown by the call: <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown by the call: <paramref name="key"/> is not a name.</exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown by the call when the client was disposed. Reported by the task when the client is disposed while the
    /// call waits for the host's answer.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Reported by the task: <paramref name="cancellationToken"/> was cancelled. The set may have reached the host.
    /// </exception>
    public ValueTask<SignalSetResult> SetAsync(string key, CancellationToken cancellationToken = default)
    {
        SignalChecks.CheckName(key, nameof(key));

        TaskCompletionSource<SignalSetResult> answer;

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);

            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<SignalSetResult>(cancellationToken);
            }

            if (_state != SignalConnectionStateEnum.Connected)
            {
                return new ValueTask<SignalSetResult>(new SignalSetResult(SignalSetStatusEnum.Unavailable, 0));
            }

            answer = _connection!.WriteSet(key);
        }

        if (!cancellationToken.CanBeCanceled)
        {
            return new ValueTask<SignalSetResult>(answer.Task);
        }

        return new ValueTask<SignalSetResult>(WaitWithCancellationAsync(answer, cancellationToken));
    }

    /// <summary>
    /// Creates one member of a group on a key and returns it at once, without waiting for the host.
    /// </summary>
    /// <param name="key">The key of the member; a name, as the remarks of <see cref="ISignals"/> define it.</param>
    /// <param name="group">The group the member joins; a name.</param>
    /// <returns>The new member; disposing it ends the membership.</returns>
    /// <remarks>
    /// It answers from local memory and never fails because the host is unreachable: the subscription is opened on
    /// the host while the client is connected, and opened again after every reconnect. A refusal by the host shows
    /// in <see cref="ISignalSubscription.WaitAsync"/>. A new subscription is busy until its first wait, which returns
    /// <see cref="SignalWaitResultEnum.Joined"/> once the host has opened it. Sets of the key's descendants reach it
    /// too, because a set also sets the ancestors of its key. Several subscriptions with the same key and group are
    /// several members.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown by the call: <paramref name="key"/> or <paramref name="group"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown by the call: <paramref name="key"/> or <paramref name="group"/> is not a name.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown by the call: the client was disposed.</exception>
    public ISignalSubscription Subscribe(string key, string group)
    {
        SignalChecks.CheckName(key, nameof(key));
        SignalChecks.CheckName(group, nameof(group));

        RemoteSubscription subscription;

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);

            subscription = new RemoteSubscription(this, ++_lastId, key, group);
            _subscriptions.Add(subscription.Sid, subscription);

            // While no connection is accepted, the next one opens it when the host accepts it.
            if (_state == SignalConnectionStateEnum.Connected)
            {
                subscription.Open(_connection!);
            }
        }

        return subscription;
    }

    /// <summary>
    /// Stops reconnecting and closes the connection; the host then ends the members of this client and hands on the
    /// signals of those that were busy.
    /// </summary>
    /// <returns>
    /// A task that completes when the connection is closed and the background work of the client has ended, except a
    /// notification handler that is running, which it does not wait for.
    /// </returns>
    /// <remarks>
    /// Pending sets and waits end with <see cref="ObjectDisposedException"/>, later calls throw it, and
    /// <see cref="State"/> becomes <see cref="SignalConnectionStateEnum.Disconnected"/>. A dispose raises no
    /// notification. A second call waits for the first and completes with it.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource disposal;
        RemoteSubscription[] subscriptions;
        TaskCompletionSource<SignalWaitResultEnum>?[] waiters;
        Connection? connection;
        bool endBegun;
        Task running;

        lock (_lock)
        {
            // A later call waits for the first, which may still wait for the loop of the attempts.
            if (_disposal is not null)
            {
                return new ValueTask(_disposal.Task);
            }

            disposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposal = disposal;
            _state = SignalConnectionStateEnum.Disconnected;

            // After DisposeAsync has been called, no notification starts.
            _notifications.Clear();

            // The subscriptions end with the client, before its connection ends, so that the end does not wait for a
            // next connection for them.
            subscriptions = [.. _subscriptions.Values];
            waiters = new TaskCompletionSource<SignalWaitResultEnum>?[subscriptions.Length];
            _subscriptions.Clear();

            for (int index = 0; index < subscriptions.Length; index++)
            {
                waiters[index] = subscriptions[index].EndWithClient();
            }

            connection = _connection;
            endBegun = connection is not null && connection.BeginEnd(reason: null);
            running = _running ?? Task.CompletedTask;
        }

        // Ends the wait for the next attempt, or the attempt that runs.
        _stopping.Cancel();

        if (endBegun)
        {
            connection!.FinishEnd();
        }

        for (int index = 0; index < subscriptions.Length; index++)
        {
            subscriptions[index].FinishWithClient(waiters[index]);
        }

        // A client that was never started has nothing to wait for, so its dispose completes in the call.
        if (running.IsCompleted)
        {
            disposal.SetResult();

            return new ValueTask(disposal.Task);
        }

        return new ValueTask(FinishDisposeAsync(running, disposal));
    }

    // Registers the cancellation after the lock, because a token that is already cancelled runs the callback inside the
    // registration. The set releases it itself once its task has completed; its answer, if it comes, is then ignored.
    private static async Task<SignalSetResult> WaitWithCancellationAsync(
        TaskCompletionSource<SignalSetResult> answer,
        CancellationToken cancellationToken)
    {
        CancellationTokenRegistration registration = cancellationToken.UnsafeRegister(
            static (state, token) => ((TaskCompletionSource<SignalSetResult>)state!).TrySetCanceled(token),
            answer);

        try
        {
            return await answer.Task.ConfigureAwait(false);
        }
        finally
        {
            registration.Unregister();
        }
    }

    // Runs the attempts, one at a time, until the client is disposed. The first is at once. After a connection the host
    // accepted, the next attempt comes after ReconnectDelay; after an attempt that failed, after the last delay doubled,
    // up to MaxReconnectDelay. Every wait goes through the TimeProvider.
    private async Task RunAttemptsAsync()
    {
        TimeSpan delay = _reconnectDelay;

        while (true)
        {
            var connection = new Connection(this);
            bool stopped;

            lock (_lock)
            {
                stopped = _disposal is not null;

                if (stopped)
                {
                    connection.BeginEnd(reason: null);
                }
                else
                {
                    _connection = connection;
                }
            }

            if (stopped)
            {
                // Closes the socket and the timer that the connection was made with.
                connection.FinishEnd();

                return;
            }

            if (await connection.RunAsync().ConfigureAwait(false))
            {
                delay = _reconnectDelay;
            }

            try
            {
                await Task.Delay(delay, _timeProvider, _stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _maxReconnectDelay.Ticks));
        }
    }

    // Under the lock, in the section of the change it reports: queues a notification, and starts its delivery on the
    // thread pool when none runs. A dispose raises none, and none starts after it.
    private void Notify(SignalConnectionStateEnum state, string? reason)
    {
        if (_disposal is not null)
        {
            return;
        }

        _notifications.Enqueue(new SignalConnectionChangedEventArgs(state, reason));

        if (!_notifying)
        {
            _notifying = true;

            // A work item, not a task, so that an exception of a handler is an unhandled exception of the process.
            ThreadPool.UnsafeQueueUserWorkItem(static client => client.DeliverNotifications(), this, preferLocal: false);
        }
    }

    // Delivers the notifications one at a time, in the order of the changes, outside the lock, and ends when none is
    // left or the client is disposed. A handler that blocks holds up only the later notifications.
    private void DeliverNotifications()
    {
        while (true)
        {
            SignalConnectionChangedEventArgs? change;

            lock (_lock)
            {
                if (_disposal is not null || !_notifications.TryDequeue(out change))
                {
                    _notifying = false;

                    return;
                }
            }

            StateChanged?.Invoke(this, change);
        }
    }

    // After the loop of the attempts has ended. A failure of the loop still completes the dispose.
    private static async Task FinishDisposeAsync(Task running, TaskCompletionSource disposal)
    {
        try
        {
            await running.ConfigureAwait(false);
        }
        finally
        {
            disposal.SetResult();
        }
    }
}
