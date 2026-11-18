using System.Diagnostics.Metrics;
using System.Net;
using Moltaro.Signals.Delivery;

namespace Moltaro.Signals;

/// <summary>
/// The host of one deployment: it keeps the signals in memory, serves clients in other processes over TCP and is
/// the <see cref="ISignals"/> of code inside its own process.
/// </summary>
/// <remarks>
/// <para>
/// It is embedded in the process that owns the background work. Several hosts may live in one process; they share
/// nothing. Nothing is persisted: the signals are memory only, and a host may restart at any time, because every
/// member does one full pass after its client reconnects.
/// </para>
/// <para>
/// Every member may be called from any thread at any time. The in-process <see cref="ISignals"/> follows the rules
/// that <see cref="ISignals"/> describes.
/// </para>
/// </remarks>
public sealed partial class SignalHost : ISignals, IAsyncDisposable
{
    /// <summary>
    /// The name of the <see cref="System.Diagnostics.Metrics.Meter"/> of every host, for a listener such as
    /// OpenTelemetry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each host creates its own meter of this name, with the host as its scope, and disposes it with the host. The
    /// instruments carry no tags:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>moltaro.signals.connections</c>, an up-down counter in <c>{connection}</c>: client connections whose
    /// <c>HELLO</c> was accepted and that are open; a connection leaves it when its socket is closed.
    /// </description></item>
    /// <item><description>
    /// <c>moltaro.signals.subscriptions</c>, an up-down counter in <c>{subscription}</c>: subscriptions open on the
    /// host, in-process and remote.
    /// </description></item>
    /// <item><description>
    /// <c>moltaro.signals.sets</c>, a counter in <c>{set}</c>: sets the host applied, in-process and remote.
    /// </description></item>
    /// <item><description>
    /// <c>moltaro.signals.coalesced</c>, a counter in <c>{signal}</c>: once per (key, group), ancestors included, a
    /// set that found a signal already pending. A signal handed on is not counted here.
    /// </description></item>
    /// <item><description>
    /// <c>moltaro.signals.deliveries</c>, a counter in <c>{signal}</c>: signals handed to a member, by a set or a
    /// signal handed on to a waiting member, or a pending signal taken by a wait. <c>Joined</c> is not counted.
    /// </description></item>
    /// <item><description>
    /// <c>moltaro.signals.handed_on</c>, a counter in <c>{signal}</c>: signals handed on because a busy member left
    /// while its group on that key kept at least one member: to a waiting member, or as a pending signal. A busy
    /// member that was the last of its group on that key hands nothing on and is not counted.
    /// </description></item>
    /// </list>
    /// </remarks>
    public const string MeterName = "Moltaro.Signals";

    // The one lock of the host. It guards _delivery, the change of _disposal, and the wait fields of every
    // subscription. Under it the host runs no code of the application except, for deadlines, TimeProvider.CreateTimer
    // and ITimer.Change: the due time is at least 1 ms, so a TimeProvider does not call back inside them, and the
    // timer's own lock is taken inside this one, never the other way round. Waits are completed, measurements recorded
    // and cancellations registered after it is released.
    private readonly Lock _lock = new();

    private readonly SignalDelivery _delivery = new();

    private readonly TimeProvider _timeProvider;

    private readonly Meter _meter;

    private readonly UpDownCounter<long> _subscriptions;

    private readonly Counter<long> _sets;

    private readonly Counter<long> _coalesced;

    private readonly Counter<long> _deliveries;

    private readonly Counter<long> _handedOn;

    // Null until the host is disposed. The first DisposeAsync creates it under _lock and completes it once it has ended
    // the in-process side; a later call returns its task. State reads it without the lock.
    private volatile TaskCompletionSource? _disposal;

    /// <summary>
    /// Creates a host with its signal delivery in memory. Its in-process <see cref="ISignals"/> works from here on,
    /// before <see cref="Start"/>: the port matters only to remote clients.
    /// </summary>
    /// <param name="options">The options of the host, validated and copied: a later change of them has no effect.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="SignalHostOptions.MaxConnections"/>, <see cref="SignalHostOptions.MaxSubscriptionsPerConnection"/>
    /// or <see cref="SignalHostOptions.DeadPeerTimeout"/> is outside its range. The
    /// <see cref="ArgumentException.ParamName"/> is the name of the property.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <see cref="SignalHostOptions.ListenEndPoint"/> or <see cref="SignalHostOptions.TimeProvider"/> is
    /// <see langword="null"/>, or <see cref="SignalHostOptions.Token"/> is <see langword="null"/> or breaks the rule of
    /// the token. The <see cref="ArgumentException.ParamName"/> is the name of the property.
    /// </exception>
    public SignalHost(SignalHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.ListenEndPoint is null)
        {
            throw new ArgumentException("The address to listen on is required.", nameof(SignalHostOptions.ListenEndPoint));
        }

        SignalChecks.CheckToken(options.Token, nameof(SignalHostOptions.Token));

        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxConnections, 1, nameof(SignalHostOptions.MaxConnections));

        ArgumentOutOfRangeException.ThrowIfLessThan(
            options.MaxSubscriptionsPerConnection,
            1,
            nameof(SignalHostOptions.MaxSubscriptionsPerConnection));

        SignalChecks.CheckTimeSpan(options.DeadPeerTimeout, nameof(SignalHostOptions.DeadPeerTimeout));

        if (options.TimeProvider is null)
        {
            throw new ArgumentException("The time provider is required.", nameof(SignalHostOptions.TimeProvider));
        }

        _timeProvider = options.TimeProvider;

        _meter = new Meter(new MeterOptions(MeterName) { Scope = this });
        _subscriptions = _meter.CreateUpDownCounter<long>("moltaro.signals.subscriptions", "{subscription}");
        _sets = _meter.CreateCounter<long>("moltaro.signals.sets", "{set}");
        _coalesced = _meter.CreateCounter<long>("moltaro.signals.coalesced", "{signal}");
        _deliveries = _meter.CreateCounter<long>("moltaro.signals.deliveries", "{signal}");
        _handedOn = _meter.CreateCounter<long>("moltaro.signals.handed_on", "{signal}");
    }

    /// <summary>
    /// The address the host listens on, with its actual port; <see langword="null"/> until <see cref="Start"/> has
    /// bound the socket.
    /// </summary>
    /// <remarks>
    /// When <see cref="SignalHostOptions.ListenEndPoint"/> asked for port 0, it reports the port the system chose. It
    /// does not change after <see cref="Start"/>. Tests and samples connect clients to it.
    /// </remarks>
    public IPEndPoint? LocalEndPoint => null;

    /// <summary>
    /// <see cref="SignalConnectionStateEnum.Connected"/> from the constructor until <see cref="DisposeAsync"/>, and
    /// <see cref="SignalConnectionStateEnum.Disconnected"/> afterwards.
    /// </summary>
    public SignalConnectionStateEnum State =>
        _disposal is null ? SignalConnectionStateEnum.Connected : SignalConnectionStateEnum.Disconnected;

    /// <summary>
    /// Never raised: the in-process <see cref="ISignals"/> of a host is connected for as long as the host lives.
    /// Handlers can be attached and removed, and are never called.
    /// </summary>
    public event EventHandler<SignalConnectionChangedEventArgs>? StateChanged
    {
        // A host never raises StateChanged, so it keeps no handlers.
        add { }
        remove { }
    }

    /// <summary>
    /// Binds <see cref="SignalHostOptions.ListenEndPoint"/>, starts accepting clients and returns once the socket is
    /// bound.
    /// </summary>
    /// <remarks>
    /// Binding does not wait, so the method is synchronous. A host is started once; after a failed bind it is
    /// disposed and a new one created.
    /// </remarks>
    /// <exception cref="System.Net.Sockets.SocketException">The address cannot be bound.</exception>
    /// <exception cref="InvalidOperationException">The host was started before.</exception>
    /// <exception cref="ObjectDisposedException">The host was disposed.</exception>
    public void Start()
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Sets a key and each of its ancestors, for every (key, group) among these keys that has members on this host,
    /// in-process or remote.
    /// </summary>
    /// <param name="key">The key that is set; a name, as the remarks of <see cref="ISignals"/> define it.</param>
    /// <param name="cancellationToken">
    /// A token that is already cancelled gives a cancelled task, and nothing is set.
    /// </param>
    /// <returns>
    /// A task completed synchronously with <see cref="SignalSetStatusEnum.Applied"/> and
    /// <see cref="SignalSetResult.Groups"/>, the number of (key, group) pairs over the key and its ancestors that had at
    /// least one live member when the set was applied, each counted once; 0 means nobody listens.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A set of <c>a.b.c</c> acts as a set of <c>a.b.c</c>, <c>a.b</c> and <c>a</c>. It sets no descendant and knows
    /// no wildcard. For every (key, group) among these keys that has members: if a member of the group on that key
    /// waits, the signal is handed to exactly one such member; otherwise the signal becomes pending. So a set of
    /// <c>a.b</c> wakes one waiting member of a group on <c>a.b</c> and one on <c>a</c>. A set while a signal is
    /// pending does nothing, and coalescing stays per (key, group); sets are never accumulated, queued or replayed.
    /// </para>
    /// <para>
    /// Members of in-process and remote subscriptions are members of the same groups. The set does not run the code
    /// of the member it wakes, and returns without waiting for it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown by the call: <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown by the call: <paramref name="key"/> is not a name.</exception>
    /// <exception cref="ObjectDisposedException">Thrown by the call: the host was disposed.</exception>
    /// <exception cref="OperationCanceledException">
    /// Reported by the task: <paramref name="cancellationToken"/> was already cancelled.
    /// </exception>
    public ValueTask<SignalSetResult> SetAsync(string key, CancellationToken cancellationToken = default)
    {
        SignalChecks.CheckName(key, nameof(key));

        SetOutcome outcome;

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);

            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<SignalSetResult>(cancellationToken);
            }

            outcome = _delivery.Set(key);
        }

        Deliver(outcome.FirstWoken);
        _sets.Add(1);

        if (outcome.Coalesced > 0)
        {
            _coalesced.Add(outcome.Coalesced);
        }

        return new ValueTask<SignalSetResult>(new SignalSetResult(SignalSetStatusEnum.Applied, outcome.Groups));
    }

    /// <summary>
    /// Creates one in-process member of a group on a key and returns it at once.
    /// </summary>
    /// <param name="key">The key of the member; a name, as the remarks of <see cref="ISignals"/> define it.</param>
    /// <param name="group">The group the member joins; a name.</param>
    /// <returns>The new member; disposing it ends the membership.</returns>
    /// <remarks>
    /// The member belongs to the same group as the remote members of that key and group. Sets of the key's
    /// descendants reach it too, because a set also sets the ancestors of its key. Several subscriptions with the same
    /// key and group are several members. A new subscription is busy until its first wait, which returns
    /// <see cref="SignalWaitResultEnum.Joined"/> at once. In-process subscriptions have no limit.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown by the call: <paramref name="key"/> or <paramref name="group"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown by the call: <paramref name="key"/> or <paramref name="group"/> is not a name.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown by the call: the host was disposed.</exception>
    public ISignalSubscription Subscribe(string key, string group)
    {
        SignalChecks.CheckName(key, nameof(key));
        SignalChecks.CheckName(group, nameof(group));

        var subscription = new InProcessSubscription(this);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);

            _delivery.Open(subscription, key, group);
        }

        _subscriptions.Add(1);

        return subscription;
    }

    /// <summary>
    /// Stops listening, closes every client connection and ends the in-process side.
    /// </summary>
    /// <returns>A task that completes when the listener and every connection are closed, so the port is free.</returns>
    /// <remarks>
    /// <para>
    /// Pending in-process waits end with <see cref="ObjectDisposedException"/>, later calls throw it, and
    /// <see cref="State"/> becomes <see cref="SignalConnectionStateEnum.Disconnected"/>. Nothing is kept: the signals
    /// are memory only.
    /// </para>
    /// <para>
    /// Remote clients see their connection end, report <see cref="SignalConnectionStateEnum.Disconnected"/>,
    /// reconnect with backoff, and their members return <see cref="SignalWaitResultEnum.Joined"/> from a host that
    /// comes back. A second call waits for the first and completes with it.
    /// </para>
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource disposal;
        GroupMember? firstWaited;

        lock (_lock)
        {
            // A later call waits for the first, which may still be ending the waits or disposing the meter.
            if (_disposal is not null)
            {
                return new ValueTask(_disposal.Task);
            }

            disposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposal = disposal;
            firstWaited = _delivery.CloseAll();
        }

        for (GroupMember? member = firstWaited; member is not null;)
        {
            GroupMember? next = member.NextWoken;

            ((InProcessSubscription)member).FailWait(new ObjectDisposedException(GetType().FullName));
            member = next;
        }

        // Disposing the meter runs the MeasurementsCompleted callbacks of the application's listeners, which may throw:
        // the first call then reports that exception, and the later calls complete all the same.
        try
        {
            _meter.Dispose();
        }
        finally
        {
            disposal.SetResult();
        }

        return new ValueTask(disposal.Task);
    }

    // After the lock: completes the waits of the members that a set woke, and counts them as deliveries.
    private void Deliver(GroupMember? firstWoken)
    {
        int delivered = 0;

        for (GroupMember? member = firstWoken; member is not null;)
        {
            // Read before the wait is completed: once completed, the member may wait and be woken again, which
            // relinks it.
            GroupMember? next = member.NextWoken;

            ((InProcessSubscription)member).CompleteWait(SignalWaitResultEnum.Signaled);
            delivered++;
            member = next;
        }

        if (delivered > 0)
        {
            _deliveries.Add(delivered);
        }
    }

    // After the lock: finishes the close of an open member. A signal it handed on to a waiting member completes that
    // member's wait.
    private void FinishClose(CloseOutcome outcome)
    {
        _subscriptions.Add(-1);

        if (outcome.Receiver is not null)
        {
            ((InProcessSubscription)outcome.Receiver).CompleteWait(SignalWaitResultEnum.Signaled);
            _deliveries.Add(1);
        }

        if (outcome.Receiver is not null || outcome.Pending)
        {
            _handedOn.Add(1);
        }
    }
}
