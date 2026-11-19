using System.Net.Sockets;
using System.Text;
using Moltaro.Signals.Protocol;

namespace Moltaro.Signals;

public sealed partial class SignalClient
{
    // One attempt of the client and, once the host accepts its HELLO, its connection, until its socket is closed: the
    // transport that ties the socket and the codec to the sets and the subscriptions of the client, by the rules of
    // docs/protocol.md.
    //
    // Its reader owns its lines. It handles each complete line in one section under the client's lock, which matches an
    // answer to its request by <rid> or <sid>, and completes the caller after the lock. The calls of the client append
    // their lines to its output under the lock, and its ConnectionOutput sends them on the thread pool. The reader does
    // not wait for the flush: the output is bounded by the requests outstanding on the connection, and a host that does
    // not read answers nothing and is declared dead.
    //
    // Liveness is measured with the timestamps of the TimeProvider, not with its wall clock, so that a clock set back or
    // forward changes no heartbeat and no dead peer.
    //
    // Its end runs once, begun under the client's lock by whichever comes first: the reader, the flush, the timer or the
    // client's dispose. From that section the connection takes no more lines and no more output, its sets are
    // Unavailable, and its subscriptions wait for the next connection.
    private sealed class Connection
    {
        private const int BufferLength = 4096;

        private readonly SignalClient _client;

        private readonly Socket _socket;

        // The one timer of the connection, armed once the host accepts the HELLO: for the earlier of the next PING and the
        // dead peer. Changed only under the client's lock while the end has not begun, and disposed by the end.
        private readonly ITimer _timer;

        // Complete lines are read where they lie; only the incomplete tail of a receive is moved to the front.
        private readonly byte[] _received = new byte[BufferLength];

        // The sets that wait for the host's answer, by their <rid>. Guarded by the client's lock until the end begins;
        // then only the end reads them.
        private readonly Dictionary<long, TaskCompletionSource<SignalSetResult>> _sets = [];

        // The lines the client writes on the connection. It takes the timestamp of every send from the client's clock,
        // for the heartbeat; a failed send ends the connection with ConnectionFailed.
        private readonly ConnectionOutput _output;

        // Completes once the end has run: the socket is closed, and the callers it ended are completed.
        private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // The timestamp of the last receive, written by the reader; the timer reads it, and that of the last send from the
        // output.
        private long _lastReceivedTimestamp;

        // The timestamp of the last PING; null before the first. Guarded by the client's lock.
        private long? _lastPingTimestamp;

        // Written by the reader under the client's lock; read under it by the others, and by the reader itself without it.
        private bool _accepted;

        // Set by the section that begins the end of the connection. Guarded by the client's lock.
        private bool _ending;

        // Set by the section that begins the end, for FinishEnd: whether the client's dispose began it, and the
        // subscriptions whose wait for the answer to an UNWAIT it ends.
        private bool _endedByDispose;

        private List<RemoteSubscription>? _endedWaits;

        internal Connection(SignalClient client)
        {
            _client = client;

            // A line goes out at once, not held back to be sent together with later ones. A socket of both families,
            // where the system has both, connects to an address of either, and to a name resolved to either.
            _socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            _output = new ConnectionOutput(
                client._lock,
                _socket,
                sentClock: client._timeProvider,
                sendFailed: () => End(ConnectionFailed));

            // Not armed before the host accepts the HELLO: until then the deadline of the attempt bounds it.
            _timer = client._timeProvider.CreateTimer(
                static state => ((Connection)state!).OnTimer(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
        }

        // Runs the attempt and, once the host accepts it, the connection, until its end has run and its output is sent or
        // failed. True when the host accepted the HELLO.
        internal async Task<bool> RunAsync()
        {
            // One deadline bounds the attempt as a whole, the connect and the answer to HELLO together; the client's
            // dispose ends the attempt too.
            var attempt = new CancellationTokenSource(_client._deadPeerTimeout, _client._timeProvider);
            CancellationTokenRegistration stopping = _client._stopping.Token.UnsafeRegister(
                static state => ((CancellationTokenSource)state!).Cancel(),
                attempt);
            string reason = ConnectionFailed;

            try
            {
                await ConnectAsync(attempt.Token).ConfigureAwait(false);
                WriteHello();

                int filled = 0;

                while (true)
                {
                    // Until the host answers the HELLO, the deadline of the attempt ends a receive; afterwards the timer
                    // ends the connection.
                    CancellationToken cancellationToken = _accepted ? CancellationToken.None : attempt.Token;
                    int received = await _socket
                        .ReceiveAsync(_received.AsMemory(filled), SocketFlags.None, cancellationToken)
                        .ConfigureAwait(false);

                    if (received == 0)
                    {
                        reason = ClosedByHost;

                        break;
                    }

                    Volatile.Write(ref _lastReceivedTimestamp, _client._timeProvider.GetTimestamp());
                    filled += received;

                    bool accepting = !_accepted;
                    int consumed = HandleLines(filled);

                    if (consumed < 0)
                    {
                        break;
                    }

                    if (accepting && _accepted)
                    {
                        // The attempt is over, and its deadline with it.
                        stopping.Dispose();
                        attempt.Dispose();
                    }

                    // The tail is shorter than the longest line, so the buffer always has room for the next receive.
                    _received.AsSpan(consumed, filled - consumed).CopyTo(_received);
                    filled -= consumed;
                }
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException or OperationCanceledException)
            {
                // The connect or a receive failed, the attempt reached its deadline or the client was disposed, or an end
                // that began elsewhere closed the socket.
            }
            finally
            {
                // The registration first, so that its callback does not cancel a disposed source.
                stopping.Dispose();
                attempt.Dispose();
            }

            End(reason);
            await _ended.Task.ConfigureAwait(false);
            await _output.WhenSent().ConfigureAwait(false);

            return _accepted;
        }

        // Under the client's lock, while the connection is accepted and its end has not begun: SUB for a member that the
        // connection opens.
        internal void WriteSub(long sid, string key, string group)
        {
            _output.Write(LineFormatter.Sub(_output.LineSpace(), sid, key, group));
        }

        // Under the client's lock, as WriteSub: WAIT for an open member.
        internal void WriteWait(long sid)
        {
            _output.Write(LineFormatter.Wait(_output.LineSpace(), sid));
        }

        // Under the client's lock, as WriteSub: UNWAIT for an armed member.
        internal void WriteUnwait(long sid)
        {
            _output.Write(LineFormatter.Unwait(_output.LineSpace(), sid));
        }

        // Under the client's lock, as WriteSub: UNSUB for a member that the client closes.
        internal void WriteUnsub(long sid)
        {
            _output.Write(LineFormatter.Unsub(_output.LineSpace(), sid));
        }

        // Under the client's lock, as WriteSub: SET with a new <rid>, whose answer the returned completion receives.
        internal TaskCompletionSource<SignalSetResult> WriteSet(string key)
        {
            long rid = ++_client._lastId;
            var answer = new TaskCompletionSource<SignalSetResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            _sets.Add(rid, answer);
            _output.Write(LineFormatter.Set(_output.LineSpace(), rid, key));

            return answer;
        }

        // Under the client's lock: begins the end of the connection, unless it has begun, and reports Disconnected with
        // reason unless it is null. From this section the connection takes no more lines and no more output; the client
        // is Disconnected, and every subscription of an accepted connection waits for the next one, with a Joined due. True
        // when this call began the end; the caller then runs FinishEnd after the lock.
        internal bool BeginEnd(string? reason)
        {
            if (_ending)
            {
                return false;
            }

            _ending = true;
            _endedByDispose = _client._disposal is not null;

            if (_client._connection == this)
            {
                _client._connection = null;
            }

            if (_accepted)
            {
                _client._state = SignalConnectionStateEnum.Disconnected;

                foreach (RemoteSubscription subscription in _client._subscriptions.Values)
                {
                    if (subscription.LoseConnection())
                    {
                        (_endedWaits ??= []).Add(subscription);
                    }
                }
            }

            if (reason is not null)
            {
                _client.Notify(SignalConnectionStateEnum.Disconnected, reason);
            }

            return true;
        }

        // After the client's lock, once, by whoever began the end. Closing the socket ends a pending connect, receive and
        // send. The sets end Unavailable, or with ObjectDisposedException when the client's dispose began the end; a wait
        // for the answer to an UNWAIT ends with what came before it. Nothing writes _sets or _endedWaits any more.
        internal void FinishEnd()
        {
            _socket.Dispose();
            _timer.Dispose();

            foreach (TaskCompletionSource<SignalSetResult> answer in _sets.Values)
            {
                if (_endedByDispose)
                {
                    answer.TrySetException(new ObjectDisposedException(typeof(SignalClient).FullName));
                }
                else
                {
                    answer.TrySetResult(new SignalSetResult(SignalSetStatusEnum.Unavailable, 0));
                }
            }

            if (_endedWaits is not null)
            {
                foreach (RemoteSubscription subscription in _endedWaits)
                {
                    subscription.CompleteWait();
                }
            }

            _ended.SetResult();
        }

        // The fixed sentence of a refusal by the host: its code, then its text.
        private static string RefusalReason(HostLine line)
        {
            return $"{Encoding.ASCII.GetString(LineFormatter.CodeWord(line.Error))} {Encoding.ASCII.GetString(line.Text)}";
        }

        // On Unix a name lookup that has begun cannot be cancelled, so it can outlive the attempt: the attempt stops
        // waiting for it at its deadline, and the lookup ends by itself.
        private async Task ConnectAsync(CancellationToken cancellationToken)
        {
            Task connecting = _socket.ConnectAsync(_client._hostEndPoint, cancellationToken).AsTask();

            try
            {
                await connecting.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!connecting.IsCompleted)
            {
                // The failure of the connect that is left behind is observed when it comes.
                _ = connecting.ContinueWith(
                    static task => task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                throw;
            }
        }

        // HELLO, the first line of the attempt, unless the client's dispose has ended the attempt.
        private void WriteHello()
        {
            lock (_client._lock)
            {
                if (!_ending)
                {
                    _output.Write(LineFormatter.Hello(_output.LineSpace(), _client._token));
                }
            }
        }

        // Handles the complete lines of the receive buffer up to filled, in order. Returns the bytes they take, or -1 when
        // the connection ends.
        private int HandleLines(int filled)
        {
            int consumed = 0;

            while (true)
            {
                HostLine line = HostLine.Read(_received.AsSpan(consumed, filled - consumed));

                if (line.Status == LineStatusEnum.Incomplete)
                {
                    return consumed;
                }

                if (!Handle(line))
                {
                    return -1;
                }

                consumed += line.Length;
            }
        }

        // Handles one complete or refused line. False when the connection ends: its end had begun, or this line ends it.
        // The reader is the only writer of _accepted, so it reads it here without the lock.
        private bool Handle(HostLine line)
        {
            if (!_accepted)
            {
                return HandleAnswerToHello(line);
            }

            if (line.Status == LineStatusEnum.Refused)
            {
                // A line that breaks the grammar closes the connection, with the fixed sentence that says why.
                return Close(line.Reason!);
            }

            return line.Verb switch
            {
                HostVerbEnum.Ok when line.Groups is { } groups => HandleSetAnswer(line.Id, groups),
                HostVerbEnum.Ok when line.Id == 0 => Close(NotOutstanding),
                HostVerbEnum.Ok => HandleSubscriptionAnswer(line.Id),
                HostVerbEnum.Err when line.Id == 0 => Refused(line),
                HostVerbEnum.Err => HandleRefusedSubscription(line.Id, line.Text),
                HostVerbEnum.Sig => HandleSignal(line.Id),

                // PONG: traffic, which the receive has counted.
                _ => true,
            };
        }

        // The first line of the host answers the HELLO: OK 0 accepts the attempt, ERR 0 refuses it with its code. Any
        // other line is no answer, and the attempt ends without a notification.
        private bool HandleAnswerToHello(HostLine line)
        {
            if (line.Status == LineStatusEnum.Complete && line.Id == 0)
            {
                if (line.Verb == HostVerbEnum.Ok)
                {
                    return Accept();
                }

                if (line.Verb == HostVerbEnum.Err)
                {
                    return Refused(line);
                }
            }

            // Not reported: the host has not accepted the attempt.
            return Close(NotOutstanding);
        }

        // OK 0. In one section the attempt is accepted, every open subscription is opened on it, the client becomes
        // Connected and its notification is queued. The reader handles the answers to these SUBs only after this section,
        // so no Joined of the connection is decided before Connected (open question Q1). The timer is armed for the first
        // heartbeat, counted from the HELLO and from this answer, whose receive is the present.
        private bool Accept()
        {
            lock (_client._lock)
            {
                if (_ending)
                {
                    return false;
                }

                _accepted = true;

                foreach (RemoteSubscription subscription in _client._subscriptions.Values)
                {
                    subscription.Open(this);
                }

                _client._state = SignalConnectionStateEnum.Connected;
                _client.Notify(SignalConnectionStateEnum.Connected, reason: null);
                Beat(Volatile.Read(ref _lastReceivedTimestamp));
            }

            return true;
        }

        // ERR 0 <code> <text>: the host refused the attempt, or closes the connection; either is reported with the code
        // at the start of its reason.
        private bool Refused(HostLine line)
        {
            string reason = RefusalReason(line);
            bool began;

            lock (_client._lock)
            {
                began = BeginEnd(reason);
            }

            if (began)
            {
                FinishEnd();
            }

            return false;
        }

        // OK <rid> <groups>. A <rid> the client does not wait for is ignored.
        private bool HandleSetAnswer(long rid, int groups)
        {
            TaskCompletionSource<SignalSetResult>? answer;

            lock (_client._lock)
            {
                if (_ending)
                {
                    return false;
                }

                _sets.Remove(rid, out answer);
            }

            answer?.TrySetResult(new SignalSetResult(SignalSetStatusEnum.Applied, groups));

            return true;
        }

        // OK <sid>: the answer to the SUB or the UNWAIT of the member. A <sid> the client has closed is ignored.
        private bool HandleSubscriptionAnswer(long sid)
        {
            RemoteSubscription? subscription;
            bool answered = true;
            bool complete = false;

            lock (_client._lock)
            {
                if (_ending)
                {
                    return false;
                }

                if (_client._subscriptions.TryGetValue(sid, out subscription))
                {
                    answered = subscription.Answer(out complete);
                }
            }

            if (!answered)
            {
                return Close(NotOutstanding);
            }

            if (complete)
            {
                subscription!.CompleteWait();
            }

            return true;
        }

        // ERR <sid> subscriptions <text>: the host refused to open the member, which is closed for good. A <sid> the
        // client has closed is ignored.
        private bool HandleRefusedSubscription(long sid, ReadOnlySpan<byte> text)
        {
            string refusal = Encoding.ASCII.GetString(text);
            RemoteSubscription? subscription;
            bool answered = true;
            bool complete = false;

            lock (_client._lock)
            {
                if (_ending)
                {
                    return false;
                }

                if (_client._subscriptions.TryGetValue(sid, out subscription))
                {
                    answered = subscription.Refuse(refusal, out complete);

                    if (answered)
                    {
                        _client._subscriptions.Remove(sid);
                    }
                }
            }

            if (!answered)
            {
                return Close(NotOutstanding);
            }

            if (complete)
            {
                subscription!.CompleteWait();
            }

            return true;
        }

        // SIG <sid>. A <sid> the client has closed is ignored: the host handed that signal on when it removed the member.
        private bool HandleSignal(long sid)
        {
            RemoteSubscription? subscription;
            bool answered = true;
            bool complete = false;

            lock (_client._lock)
            {
                if (_ending)
                {
                    return false;
                }

                if (_client._subscriptions.TryGetValue(sid, out subscription))
                {
                    answered = subscription.Signal(out complete);
                }
            }

            if (!answered)
            {
                return Close(NotOutstanding);
            }

            if (complete)
            {
                subscription!.CompleteWait();
            }

            return true;
        }

        // Ends the connection for a line of the host. Returns false, for the line that ends it.
        private bool Close(string reason)
        {
            End(reason);

            return false;
        }

        // Ends the connection, unless its end has begun, with Disconnected and reason when the host had accepted it.
        private void End(string reason)
        {
            bool began;

            lock (_client._lock)
            {
                began = BeginEnd(_accepted ? reason : null);
            }

            if (began)
            {
                FinishEnd();
            }
        }

        // The timer's callback: a heartbeat, or the end of a connection whose host is dead. A timer that fires for an end
        // that has begun does nothing.
        private void OnTimer()
        {
            // Read before the lock is taken, because the TimeProvider is code of the application.
            long now = _client._timeProvider.GetTimestamp();
            bool began = false;

            lock (_client._lock)
            {
                if (_ending)
                {
                    return;
                }

                if (!Beat(now))
                {
                    began = BeginEnd(NoTraffic);
                }
            }

            if (began)
            {
                FinishEnd();
            }
        }

        // Under the client's lock, with the present timestamp read before it: sends a PING when the client has sent
        // nothing, or received nothing, for HeartbeatInterval, and at most one in that time, and arms the timer for the
        // earlier of the next PING and the dead peer. False when nothing was received for DeadPeerTimeout: the host is
        // dead. The timestamps of the traffic are read here, so that a receive handled meanwhile counts; one taken after
        // the present counts as taken at it.
        private bool Beat(long now)
        {
            TimeProvider timeProvider = _client._timeProvider;
            TimeSpan sinceReceived = SignalChecks.Elapsed(timeProvider, Volatile.Read(ref _lastReceivedTimestamp), now);

            if (sinceReceived >= _client._deadPeerTimeout)
            {
                return false;
            }

            TimeSpan sinceSent = SignalChecks.Elapsed(timeProvider, _output.LastSentTimestamp, now);
            TimeSpan sincePing = _lastPingTimestamp is { } pinged
                ? SignalChecks.Elapsed(timeProvider, pinged, now)
                : TimeSpan.MaxValue;

            // How long the connection has been quiet on the side that was quiet longer, unless a PING went out since.
            TimeSpan quiet = sinceSent > sinceReceived ? sinceSent : sinceReceived;

            if (sincePing < quiet)
            {
                quiet = sincePing;
            }

            if (quiet >= _client._heartbeatInterval)
            {
                _output.Write(LineFormatter.Ping(_output.LineSpace()));
                _lastPingTimestamp = now;
                quiet = TimeSpan.Zero;
            }

            // Both rests are positive and at most DeadPeerTimeout, so the due time is too.
            TimeSpan untilPing = _client._heartbeatInterval - quiet;
            TimeSpan untilDead = _client._deadPeerTimeout - sinceReceived;

            _timer.Change(SignalChecks.DueTime(untilPing < untilDead ? untilPing : untilDead), Timeout.InfiniteTimeSpan);

            return true;
        }

    }
}
