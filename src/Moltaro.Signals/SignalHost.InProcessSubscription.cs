using Moltaro.Signals.Delivery;

namespace Moltaro.Signals;

public sealed partial class SignalHost
{
    // One in-process member of a group on a key: the ISignalSubscription that Subscribe returns, and the member that the
    // host's signal delivery holds. Every field except _host is guarded by the host's lock.
    //
    // A wait that does not return at once has a TaskCompletionSource of its own, created with
    // RunContinuationsAsynchronously, so that the code after the member's await runs on the thread pool and never inside
    // the call that completes the wait. Delivery decides under the lock which path ends a wait: a set, a signal handed
    // on, the deadline, the cancellation or a dispose. The wait is completed after the lock, always in the Try form, so
    // that whichever completer comes first wins.
    private sealed class InProcessSubscription(SignalHost host) : GroupMember, ISignalSubscription
    {
        private readonly SignalHost _host = host;

        // A Joined is due after Subscribe, until a wait returns it (rule 6).
        private bool _joinedDue = true;

        private bool _disposed;

        // The completion of the latest wait that did not return at once; that wait is pending until its task completes.
        // Its AsyncState is this subscription, so that the callback of a cancellation reaches the subscription from the
        // waiter it was registered for.
        private TaskCompletionSource<SignalWaitResultEnum>? _waiter;

        // The deadline of the wait of _waiter; null when it has none.
        private DateTimeOffset? _deadline;

        // Created on the first wait with a deadline and armed again for every later one, so that a wait allocates no
        // timer.
        private ITimer? _timer;

        public ValueTask<SignalWaitResultEnum> WaitAsync(DateTimeOffset? until, CancellationToken cancellationToken = default)
        {
            // Read before the lock is taken, because the TimeProvider is code of the application.
            TimeSpan? dueTime = until is { } deadline ? TimeUntil(deadline, _host._timeProvider.GetUtcNow()) : null;
            TaskCompletionSource<SignalWaitResultEnum>? waiter = null;

            lock (_host._lock)
            {
                ObjectDisposedException.ThrowIf(_host._disposal is not null, _host);
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

                if (_joinedDue)
                {
                    _joinedDue = false;

                    return new ValueTask<SignalWaitResultEnum>(SignalWaitResultEnum.Joined);
                }

                if (dueTime == TimeSpan.Zero)
                {
                    return new ValueTask<SignalWaitResultEnum>(SignalWaitResultEnum.DeadlineReached);
                }

                if (!_host._delivery.Arm(this))
                {
                    waiter = new TaskCompletionSource<SignalWaitResultEnum>(
                        this,
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _waiter = waiter;
                    _deadline = dueTime is null ? null : until;

                    if (dueTime is { } due)
                    {
                        _timer ??= CreateTimer();
                        _timer.Change(due, Timeout.InfiniteTimeSpan);
                    }
                }
            }

            // Arm found a signal pending, and the member took it.
            if (waiter is null)
            {
                _host._deliveries.Add(1);

                return new ValueTask<SignalWaitResultEnum>(SignalWaitResultEnum.Signaled);
            }

            if (!cancellationToken.CanBeCanceled)
            {
                return new ValueTask<SignalWaitResultEnum>(waiter.Task);
            }

            return new ValueTask<SignalWaitResultEnum>(WaitWithCancellationAsync(waiter, cancellationToken));
        }

        public void Dispose()
        {
            CloseOutcome outcome = default;
            TaskCompletionSource<SignalWaitResultEnum>? waiter = null;
            ITimer? timer;
            bool closing;

            lock (_host._lock)
            {
                // Taken after the host's dispose too: an armed timer would keep the subscription and the host
                // reachable until its deadline.
                timer = _timer;

                // After the host's dispose the subscription has ended with the host, so its own dispose ends nothing.
                closing = !_disposed && _host._disposal is null;

                if (closing)
                {
                    _disposed = true;
                    outcome = _host._delivery.Close(this);
                    waiter = _waiter;
                }
            }

            timer?.Dispose();

            if (!closing)
            {
                return;
            }

            // A wait that has not completed ends here, also one that a set has woken but whose completion has not run
            // yet: Close found the member busy and handed its signal on, so the signal is not lost.
            waiter?.TrySetException(new ObjectDisposedException(GetType().FullName));
            _host.FinishClose(outcome);
        }

        // After the host's lock: completes the wait that delivery took out of Waiting for this member. _waiter is read
        // without the lock because it cannot change meanwhile: a new wait is refused while this one is pending.
        internal void CompleteWait(SignalWaitResultEnum result)
        {
            _waiter!.TrySetResult(result);
        }

        // After the host's lock: ends the wait of this member, which waited when the host was disposed; read as in
        // CompleteWait.
        internal void FailWait(Exception error)
        {
            _waiter!.TrySetException(error);
        }

        // The due time of a timer for a deadline, rounded up to whole milliseconds, because a timer counts whole
        // milliseconds and would otherwise fire before the deadline. Zero when the deadline has come; null when it is
        // further away than the longest timer, where it counts as none.
        private static TimeSpan? TimeUntil(DateTimeOffset deadline, DateTimeOffset now)
        {
            long ticks = (deadline - now).Ticks;

            if (ticks <= 0)
            {
                return TimeSpan.Zero;
            }

            TimeSpan due = TimeSpan.FromMilliseconds((ticks + TimeSpan.TicksPerMillisecond - 1) / TimeSpan.TicksPerMillisecond);

            return due <= SignalChecks.LongestTimer ? due : null;
        }

        // Registers the cancellation after Arm and outside the lock, because a token that is already cancelled runs the
        // callback inside the registration. The wait releases it itself once its task has completed.
        private static async Task<SignalWaitResultEnum> WaitWithCancellationAsync(
            TaskCompletionSource<SignalWaitResultEnum> waiter,
            CancellationToken cancellationToken)
        {
            CancellationTokenRegistration registration = cancellationToken.UnsafeRegister(
                static (state, token) =>
                {
                    var cancelled = (TaskCompletionSource<SignalWaitResultEnum>)state!;

                    ((InProcessSubscription)cancelled.Task.AsyncState!).EndByCancellation(cancelled, token);
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

        // Ends the wait a cancellation was registered for, unless a signal came first. It acts only on that wait: a
        // later wait of the subscription has a waiter of its own.
        private void EndByCancellation(TaskCompletionSource<SignalWaitResultEnum> waiter, CancellationToken cancellationToken)
        {
            lock (_host._lock)
            {
                if (_waiter != waiter || !_host._delivery.EndWait(this))
                {
                    return;
                }
            }

            waiter.TrySetCanceled(cancellationToken);
        }

        // The timer outlives the wait that creates it, so it does not capture the execution context of that wait.
        private ITimer CreateTimer()
        {
            using AsyncFlowControl? flow = ExecutionContext.IsFlowSuppressed() ? null : ExecutionContext.SuppressFlow();

            return _host._timeProvider.CreateTimer(
                static state => ((InProcessSubscription)state!).EndAtDeadline(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
        }

        // The timer's callback. It may fire for an earlier wait whose deadline was never reached, so it ends a wait only
        // when the member waits and the deadline of its current wait has come.
        private void EndAtDeadline()
        {
            DateTimeOffset now = _host._timeProvider.GetUtcNow();
            TaskCompletionSource<SignalWaitResultEnum> waiter;

            lock (_host._lock)
            {
                if (State != GroupMemberStateEnum.Waiting || _deadline is not { } deadline)
                {
                    return;
                }

                TimeSpan? rest = TimeUntil(deadline, now);

                if (rest != TimeSpan.Zero)
                {
                    // Armed for an earlier wait, or the clock was set back: the deadline of this wait has not come.
                    if (rest is { } due)
                    {
                        _timer!.Change(due, Timeout.InfiniteTimeSpan);
                    }

                    return;
                }

                _host._delivery.EndWait(this);
                waiter = _waiter!;
            }

            waiter.TrySetResult(SignalWaitResultEnum.DeadlineReached);
        }
    }
}
