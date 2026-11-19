namespace Moltaro.Signals;

public sealed partial class SignalClient
{
    // One member of a group on a key in a process other than the host's: the ISignalSubscription that Subscribe returns.
    // It keeps its <sid> across connections and is opened again on every connection that the host accepts. Every field
    // that is not readonly is guarded by the client's lock.
    //
    // A wait that does not return at once has a TaskCompletionSource of its own, created with
    // RunContinuationsAsynchronously, so that the code after the member's await runs on the thread pool: never on the
    // reader of the connection and never inside the call that completes the wait. The lock decides how a wait ends, from
    // a line of the host, the deadline, the token, the end of the connection or a dispose, and the wait is completed after
    // the lock, always in the Try form, so that whichever completer comes first wins.
    private sealed class RemoteSubscription(SignalClient client, long sid, string key, string group) : ISignalSubscription
    {
        private readonly SignalClient _client = client;

        private readonly string _key = key;

        private readonly string _group = group;

        // A Joined is due after Subscribe and after the end of every connection, until a wait returns it (rule 6).
        private bool _joinedDue = true;

        // The SUB of the member is written on the connection, and the host has not answered it.
        private bool _opening;

        // The host has answered the SUB on the connection: the member is open there.
        private bool _open;

        // The host's text when it refused the SUB. The member is then closed for good.
        private string? _refusal;

        private bool _disposed;

        private WaitStateEnum _wait;

        // The completion of the latest wait that did not return at once; that wait is pending until its task completes.
        // Its AsyncState is this subscription, so that the callback of a cancellation reaches the subscription from the
        // waiter it was registered for.
        private TaskCompletionSource<SignalWaitResultEnum>? _waiter;

        // The deadline of the wait of _waiter; null when it has none.
        private DateTimeOffset? _deadline;

        // Created on the first wait with a deadline and armed again for every later one, so that a wait allocates no
        // timer.
        private ITimer? _timer;

        // How the lock decided that the pending wait ends, for CompleteWait: with an error, by the token that cancelled
        // it, or else with a result.
        private Exception? _error;

        private CancellationToken _cancelledBy;

        private SignalWaitResultEnum _outcome;

        internal long Sid { get; } = sid;

        public ValueTask<SignalWaitResultEnum> WaitAsync(DateTimeOffset? until, CancellationToken cancellationToken = default)
        {
            // Read before the lock is taken, because the TimeProvider is code of the application.
            TimeSpan? dueTime = until is { } deadline ? SignalChecks.TimeUntil(deadline, _client._timeProvider.GetUtcNow()) : null;
            TaskCompletionSource<SignalWaitResultEnum> waiter;

            lock (_client._lock)
            {
                ObjectDisposedException.ThrowIf(_client._disposal is not null, _client);
                ObjectDisposedException.ThrowIf(_disposed, this);

                // With a token, a call in the pool hop between _waiter and the wait's own task completing is accepted.
                if (_waiter is { Task.IsCompleted: false })
                {
                    throw new InvalidOperationException("Another wait of this subscription has not completed.");
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return ValueTask.FromCanceled<SignalWaitResultEnum>(cancellationToken);
                }

                if (_refusal is not null)
                {
                    return ValueTask.FromException<SignalWaitResultEnum>(new InvalidOperationException(_refusal));
                }

                if (_joinedDue && _open)
                {
                    _joinedDue = false;

                    return new ValueTask<SignalWaitResultEnum>(SignalWaitResultEnum.Joined);
                }

                if (dueTime == TimeSpan.Zero)
                {
                    return new ValueTask<SignalWaitResultEnum>(SignalWaitResultEnum.DeadlineReached);
                }

                waiter = new TaskCompletionSource<SignalWaitResultEnum>(this, TaskCreationOptions.RunContinuationsAsynchronously);
                _waiter = waiter;
                _deadline = dueTime is null ? null : until;
                _error = null;
                _cancelledBy = default;

                // An open member asks the host; one that is not open waits until it is (rule 5 of WaitAsync).
                if (_open)
                {
                    _client._connection!.WriteWait(Sid);
                    _wait = WaitStateEnum.Armed;
                }
                else
                {
                    _wait = WaitStateEnum.ForOpening;
                }

                if (dueTime is { } due)
                {
                    _timer ??= CreateTimer();
                    _timer.Change(due, Timeout.InfiniteTimeSpan);
                }
            }

            if (!cancellationToken.CanBeCanceled)
            {
                return new ValueTask<SignalWaitResultEnum>(waiter.Task);
            }

            return new ValueTask<SignalWaitResultEnum>(WaitWithCancellationAsync(waiter, cancellationToken));
        }

        public void Dispose()
        {
            TaskCompletionSource<SignalWaitResultEnum>? waiter = null;
            ITimer? timer;

            lock (_client._lock)
            {
                // Taken after the client's dispose too: an armed timer would keep the subscription and the client
                // reachable until its deadline.
                timer = _timer;

                // After the client's dispose the subscription has ended with the client, so its own dispose ends nothing.
                if (!_disposed && _client._disposal is null)
                {
                    _disposed = true;
                    _client._subscriptions.Remove(Sid);

                    // The host ignores an UNSUB for a <sid> that is not open, so it may follow a SUB not answered yet.
                    if (_opening || _open)
                    {
                        _client._connection!.WriteUnsub(Sid);
                    }

                    if (_wait != WaitStateEnum.None)
                    {
                        _wait = WaitStateEnum.None;
                        waiter = _waiter;
                    }
                }
            }

            timer?.Dispose();

            // A signal the host handed to the wait meanwhile makes the member busy on the host, which hands it on when it
            // reads the UNSUB or loses the connection; it is not lost.
            waiter?.TrySetException(new ObjectDisposedException(GetType().FullName));
        }

        // Under the client's lock, on a connection the host has accepted: writes the SUB that opens the member there.
        internal void Open(Connection connection)
        {
            connection.WriteSub(Sid, _key, _group);
            _opening = true;
        }

        // Under the client's lock: OK <sid>, the answer to the SUB or to the UNWAIT of the member, of which at most one is
        // outstanding. The SUB opens the member, and a wait for that returns Joined; the UNWAIT ends the wait as the lock
        // decided before it. False when neither is outstanding. complete: the caller runs CompleteWait after the lock.
        internal bool Answer(out bool complete)
        {
            if (_opening)
            {
                _opening = false;
                _open = true;
                complete = _wait == WaitStateEnum.ForOpening;

                if (complete)
                {
                    _wait = WaitStateEnum.None;
                    _joinedDue = false;
                    _outcome = SignalWaitResultEnum.Joined;
                }

                return true;
            }

            complete = _wait == WaitStateEnum.Ending;

            if (complete)
            {
                _wait = WaitStateEnum.None;
            }

            return complete;
        }

        // Under the client's lock: SIG <sid>. An armed member is woken; a member whose wait ends with the host received
        // the signal before the host read its UNWAIT, so the wait returns Signaled, even if its token was cancelled. False
        // when the member is neither. complete: the caller runs CompleteWait after the lock.
        internal bool Signal(out bool complete)
        {
            complete = _wait == WaitStateEnum.Armed;

            if (complete)
            {
                _wait = WaitStateEnum.None;
                _outcome = SignalWaitResultEnum.Signaled;

                return true;
            }

            if (_wait != WaitStateEnum.Ending)
            {
                return false;
            }

            _outcome = SignalWaitResultEnum.Signaled;
            _cancelledBy = default;

            return true;
        }

        // Under the client's lock: ERR <sid> subscriptions, the refusal of the SUB. A wait for the opening ends with the
        // host's text, as every later one does. False when no SUB is outstanding. complete: the caller runs CompleteWait
        // after the lock.
        internal bool Refuse(string text, out bool complete)
        {
            complete = false;

            if (!_opening)
            {
                return false;
            }

            _opening = false;
            _refusal = text;

            if (_wait == WaitStateEnum.ForOpening)
            {
                _wait = WaitStateEnum.None;
                _error = new InvalidOperationException(text);
                complete = true;
            }

            return true;
        }

        // Under the client's lock, when its connection ends: the host has ended the member, and handed its signal on if it
        // was busy. A Joined is due on the next connection, and a wait that waited on the host waits for the reopening.
        // A wait that waited for the answer to its UNWAIT ends without it, as rule 5 of WaitAsync says: true, and the
        // caller runs CompleteWait after the lock.
        internal bool LoseConnection()
        {
            _opening = false;
            _open = false;
            _joinedDue = true;

            if (_wait == WaitStateEnum.Armed)
            {
                _wait = WaitStateEnum.ForOpening;
            }

            if (_wait != WaitStateEnum.Ending)
            {
                return false;
            }

            _wait = WaitStateEnum.None;

            return true;
        }

        // Under the client's lock, in its dispose: the subscription ends with the client. Returns the waiter of a pending
        // wait, which FinishWithClient ends after the lock.
        internal TaskCompletionSource<SignalWaitResultEnum>? EndWithClient()
        {
            if (_wait == WaitStateEnum.None)
            {
                return null;
            }

            _wait = WaitStateEnum.None;

            return _waiter;
        }

        // After the client's lock, in its dispose: ends the wait that EndWithClient took and disposes the timer. _timer is
        // read without the lock because no wait creates one after the client's dispose.
        internal void FinishWithClient(TaskCompletionSource<SignalWaitResultEnum>? waiter)
        {
            _timer?.Dispose();
            waiter?.TrySetException(new ObjectDisposedException(typeof(SignalClient).FullName));
        }

        // After the client's lock: completes the wait whose end the lock decided, with what it decided. _waiter and the
        // outcome are read without the lock because they cannot change meanwhile: a new wait is refused while this one is
        // pending, and nothing else changes them once the wait has no state.
        internal void CompleteWait()
        {
            TaskCompletionSource<SignalWaitResultEnum> waiter = _waiter!;

            if (_error is not null)
            {
                waiter.TrySetException(_error);
            }
            else if (_cancelledBy.CanBeCanceled)
            {
                waiter.TrySetCanceled(_cancelledBy);
            }
            else
            {
                waiter.TrySetResult(_outcome);
            }
        }

        // Registers the cancellation after the lock, because a token that is already cancelled runs the callback inside the
        // registration. The wait releases it itself once its task has completed.
        private static async Task<SignalWaitResultEnum> WaitWithCancellationAsync(
            TaskCompletionSource<SignalWaitResultEnum> waiter,
            CancellationToken cancellationToken)
        {
            CancellationTokenRegistration registration = cancellationToken.UnsafeRegister(
                static (state, token) =>
                {
                    var cancelled = (TaskCompletionSource<SignalWaitResultEnum>)state!;

                    ((RemoteSubscription)cancelled.Task.AsyncState!).EndByCancellation(cancelled, token);
                },
                waiter);

            try
            {
                return await waiter.Task.ConfigureAwait(false);
            }
            finally
            {
                registration.Unregister();
            }
        }

        // Ends the wait a cancellation was registered for, unless its end was decided before. It acts only on that wait: a
        // later wait of the subscription has a waiter of its own. An armed member is ended with the host; a member that is
        // not open is ended at once, since there is no host to ask.
        private void EndByCancellation(TaskCompletionSource<SignalWaitResultEnum> waiter, CancellationToken cancellationToken)
        {
            bool complete = false;

            lock (_client._lock)
            {
                if (_waiter != waiter)
                {
                    return;
                }

                if (_wait == WaitStateEnum.ForOpening)
                {
                    _wait = WaitStateEnum.None;
                    _cancelledBy = cancellationToken;
                    complete = true;
                }
                else if (_wait == WaitStateEnum.Armed)
                {
                    _client._connection!.WriteUnwait(Sid);
                    _wait = WaitStateEnum.Ending;
                    _cancelledBy = cancellationToken;
                }
            }

            if (complete)
            {
                CompleteWait();
            }
        }

        // The timer outlives the wait that creates it, so it does not capture the execution context of that wait.
        private ITimer CreateTimer()
        {
            using AsyncFlowControl? flow = ExecutionContext.IsFlowSuppressed() ? null : ExecutionContext.SuppressFlow();

            return _client._timeProvider.CreateTimer(
                static state => ((RemoteSubscription)state!).EndAtDeadline(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
        }

        // The timer's callback. It may fire for an earlier wait whose deadline was never reached, so it ends a wait only
        // when one is pending without a decided end and the deadline of that wait has come. An armed member is ended with
        // the host; a member that is not open is ended at once.
        private void EndAtDeadline()
        {
            DateTimeOffset now = _client._timeProvider.GetUtcNow();
            bool complete = false;

            lock (_client._lock)
            {
                if (_wait is not (WaitStateEnum.ForOpening or WaitStateEnum.Armed) || _deadline is not { } deadline)
                {
                    return;
                }

                TimeSpan? rest = SignalChecks.TimeUntil(deadline, now);

                if (rest != TimeSpan.Zero)
                {
                    // Armed for an earlier wait, or the clock was set back: the deadline of this wait has not come.
                    if (rest is { } due)
                    {
                        _timer!.Change(due, Timeout.InfiniteTimeSpan);
                    }

                    return;
                }

                _outcome = SignalWaitResultEnum.DeadlineReached;

                if (_wait == WaitStateEnum.ForOpening)
                {
                    _wait = WaitStateEnum.None;
                    complete = true;
                }
                else
                {
                    _client._connection!.WriteUnwait(Sid);
                    _wait = WaitStateEnum.Ending;
                }
            }

            if (complete)
            {
                CompleteWait();
            }
        }
    }
}
