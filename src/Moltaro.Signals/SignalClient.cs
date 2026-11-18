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
public sealed class SignalClient : ISignals, IAsyncDisposable
{
    private volatile bool _disposed;

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
    public SignalConnectionStateEnum State => SignalConnectionStateEnum.Disconnected;

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
    public event EventHandler<SignalConnectionChangedEventArgs>? StateChanged
    {
        add { }
        remove { }
    }

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
    /// When the host refuses or no host answers, the client stays <see cref="SignalConnectionStateEnum.Disconnected"/>
    /// and tries again with backoff: after <see cref="SignalClientOptions.ReconnectDelay"/>, doubled after each failed
    /// attempt up to <see cref="SignalClientOptions.MaxReconnectDelay"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The client was started before.</exception>
    /// <exception cref="ObjectDisposedException">The client was disposed.</exception>
    public void Start()
    {
        throw new NotSupportedException();
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        throw new NotSupportedException();
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        throw new NotSupportedException();
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
        _disposed = true;

        return ValueTask.CompletedTask;
    }
}
