using System.Buffers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Moltaro.Signals.Delivery;
using Moltaro.Signals.Protocol;

namespace Moltaro.Signals;

public sealed partial class SignalHost
{
    // One client connection of the host, from its accept until its socket is closed: the transport that ties its socket,
    // the codec and signal delivery together, by the rules of docs/protocol.md.
    //
    // Its reader owns its lines. It handles each complete line in one section under the host's lock, together with
    // signal delivery, and appends the answer there; a SIG is appended in the section that woke its member, whichever
    // connection that section serves. So the output of a connection follows the order of the decisions. A flush sends
    // the output on the thread pool, and the reader receives again only after the output is sent: a client that does
    // not read leaves the host holding the answers to one receive and at most one SIG per armed member, and as the
    // host then receives nothing, the timer ends the connection.
    //
    // Its end runs once, begun under the host's lock by whichever comes first: the reader, the flush, the timer or the
    // host's dispose. From that section the connection takes no more lines and no more output.
    private sealed class Connection
    {
        private const int BufferLength = 4096;

        // The fixed sentences of the refusals that depend on the state of the connection, written as the <text> of an
        // ERR line. None repeats what the client sent.
        private const string NotHelloFirst = "The first line of a connection is not its HELLO.";

        private const string SecondHello = "A connection sends HELLO once.";

        private const string OtherToken = "The token is not the token of the host.";

        private const string SidOpen = "A SUB names a sid that is open.";

        private const string MemberArmed = "A WAIT names a member that is armed.";

        private const string TooManySubscriptions = "The connection holds its limit of subscriptions.";

        private const string NoHello = "No HELLO came within the dead-peer time of the host.";

        private readonly SignalHost _host;

        private readonly Socket _socket;

        private readonly ITimer _timer;

        // The UTC ticks of the accept: before HELLO, the deadline of the connection is its accept plus DeadPeerTimeout.
        private readonly long _acceptedTicks;

        // Complete lines are read where they lie; only the incomplete tail of a receive is moved to the front.
        private readonly byte[] _received = new byte[BufferLength];

        // The open members by their <sid>. Guarded by the host's lock.
        private readonly Dictionary<long, RemoteMember> _members = [];

        private readonly Func<Task> _flushAsync;

        private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // The output not sent yet, guarded by the host's lock. The flush swaps it with _sending, which only the flush
        // touches, and at most one flush runs.
        private ArrayBufferWriter<byte> _pending = new(BufferLength);

        private ArrayBufferWriter<byte> _sending = new(BufferLength);

        // The flush that runs; null when none does. Guarded by the host's lock.
        private Task? _flush;

        // The UTC ticks of the last receive: after HELLO, the deadline of the connection is this plus DeadPeerTimeout.
        // Written by the reader and read by the timer, without the lock.
        private long _lastReceivedTicks;

        // Written by the reader under the host's lock; read under it by the timer and the end, and by the reader itself
        // without it.
        private bool _helloAccepted;

        // Set by the section that begins the end of the connection. Guarded by the host's lock.
        private bool _ending;

        // Under the host's lock, when the host admits the connection. Its timer is armed for the deadline of its HELLO.
        internal Connection(SignalHost host, Socket socket, DateTimeOffset acceptedAt)
        {
            _host = host;
            _socket = socket;
            _acceptedTicks = acceptedAt.UtcTicks;
            _lastReceivedTicks = _acceptedTicks;
            _flushAsync = FlushAsync;
            _timer = host._timeProvider.CreateTimer(
                static state => ((Connection)state!).OnTimer(),
                this,
                host._deadPeerTimeout,
                Timeout.InfiniteTimeSpan);
        }

        // Completes once the end has run: the socket is closed and the connection has left the host's set.
        internal Task Ended => _ended.Task;

        // After the host's lock: starts the reader, on the thread pool, apart from the loop that accepts connections.
        internal void Start()
        {
            _ = Task.Run(ReadAsync);
        }

        // Under the host's lock, in the section that woke the member of sid: writes its SIG. A connection that ends
        // takes it no more; its member is busy then and hands its signal on when it is closed.
        internal void Signal(long sid)
        {
            if (!_ending)
            {
                Write(LineFormatter.Signal(LineSpace(), sid));
            }
        }

        // Under the host's lock: begins the end of the connection, unless it has begun. A refusal writes its ERR 0 line,
        // the last output the connection takes. True when this call began the end; the caller then runs FinishEndAsync
        // after the lock.
        internal bool BeginEnd(ProtocolErrorEnum? refusal, string? text)
        {
            if (_ending)
            {
                return false;
            }

            if (refusal is { } code)
            {
                Write(LineFormatter.Error(LineSpace(), 0, code, text!));
            }

            _ending = true;

            return true;
        }

        // After the host's lock, on its dispose, for a connection whose end began before: closing the socket ends a wait
        // of that end for its flush.
        internal void CloseSocket()
        {
            _socket.Dispose();
        }

        // After the host's lock, once, by whoever began the end. After a refusal the members are closed, then the end
        // waits until the flush has sent the ERR 0 line or the timer fires, and shuts the socket down; after a dead peer,
        // the client's close, a failed receive or send, or the host's dispose, the socket is disposed at once. Every end
        // then disposes the socket, which ends a pending send and receive, and leaves the host's set.
        internal async Task FinishEndAsync(bool refused)
        {
            try
            {
                if (!refused)
                {
                    _socket.Dispose();
                }

                CloseMembers();

                if (refused)
                {
                    // The timer of an ending connection disposes its socket, so a client that does not read holds the end
                    // for DeadPeerTimeout at most.
                    _timer.Change(_host._deadPeerTimeout, Timeout.InfiniteTimeSpan);

                    Task? flush;

                    lock (_host._lock)
                    {
                        flush = _flush;
                    }

                    if (flush is not null)
                    {
                        await flush.ConfigureAwait(false);
                    }

                    // After a shutdown a socket is closed normally, even with a receive pending.
                    try
                    {
                        _socket.Shutdown(SocketShutdown.Both);
                    }
                    catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
                    {
                        // The socket was disposed or the client is gone: the connection is closed already.
                    }
                }
            }
            finally
            {
                // Here too, so that an application's meter listener that throws while the members are closed leaves no
                // connection open and no dispose of the host waiting.
                _socket.Dispose();
                _timer.Dispose();

                bool counted;

                lock (_host._lock)
                {
                    _host._openConnections.Remove(this);
                    counted = _helloAccepted;
                }

                _ended.SetResult();

                if (counted)
                {
                    _host._connections.Add(-1);
                }
            }
        }

        // Under the host's lock: room for one line at the end of the pending output.
        private Span<byte> LineSpace()
        {
            return _pending.GetSpan(ProtocolRules.LineRoom);
        }

        // Under the host's lock: takes the line just written into LineSpace into the pending output, and starts a flush
        // when none runs. The flush runs on the thread pool, never inline in the section that wrote the line.
        private void Write(int length)
        {
            _pending.Advance(length);

            if (_flush is null)
            {
                // The flush serves the connection, not the code that wrote the line, so it does not capture the
                // execution context of that code, as Start does not for the loop that accepts connections.
                using AsyncFlowControl? flow = ExecutionContext.IsFlowSuppressed() ? null : ExecutionContext.SuppressFlow();

                _flush = Task.Run(_flushAsync);
            }
        }

        // Receives, stores the time, and handles every complete line before it receives again, once the output is sent.
        private async Task ReadAsync()
        {
            int filled = 0;

            while (true)
            {
                int received;

                try
                {
                    received = await _socket.ReceiveAsync(_received.AsMemory(filled), SocketFlags.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
                {
                    received = 0;
                }

                if (received == 0)
                {
                    // The client closed the connection, the receive failed, or an end that began elsewhere disposed the
                    // socket.
                    End();

                    return;
                }

                Volatile.Write(ref _lastReceivedTicks, _host._timeProvider.GetUtcNow().UtcTicks);
                filled += received;

                int consumed = HandleLines(filled);

                if (consumed < 0)
                {
                    return;
                }

                // The tail is shorter than the longest line, so the buffer always has room for the next receive.
                _received.AsSpan(consumed, filled - consumed).CopyTo(_received);
                filled -= consumed;

                Task? flush;

                lock (_host._lock)
                {
                    flush = _flush;
                }

                if (flush is not null)
                {
                    await flush.ConfigureAwait(false);
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
                ClientLine line = ClientLine.Read(_received.AsSpan(consumed, filled - consumed));

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
        // The reader is the only writer of _helloAccepted, so it reads it here without the lock.
        private bool Handle(ClientLine line)
        {
            if (line.Status == LineStatusEnum.Refused)
            {
                // A HELLO of another version is refused with version only as the first line of a connection.
                return line.Error == ProtocolErrorEnum.Version && _helloAccepted
                    ? Refuse(ProtocolErrorEnum.Protocol, SecondHello)
                    : Refuse(line.Error, line.Reason!);
            }

            if (line.Verb == ClientVerbEnum.Hello)
            {
                return _helloAccepted ? Refuse(ProtocolErrorEnum.Protocol, SecondHello) : HandleHello(line.Token);
            }

            if (!_helloAccepted)
            {
                return Refuse(ProtocolErrorEnum.Protocol, NotHelloFirst);
            }

            return line.Verb switch
            {
                ClientVerbEnum.Sub => HandleSub(line),
                ClientVerbEnum.Wait => HandleWait(line.Id),
                ClientVerbEnum.Unwait => HandleUnwait(line.Id),
                ClientVerbEnum.Unsub => HandleUnsub(line.Id),
                ClientVerbEnum.Set => HandleSet(line.Id, line.Key),
                _ => HandlePing(),
            };
        }

        private bool HandleHello(ReadOnlySpan<byte> token)
        {
            bool accepted = CryptographicOperations.FixedTimeEquals(token, _host._token);

            lock (_host._lock)
            {
                if (_ending)
                {
                    return false;
                }

                if (!accepted)
                {
                    BeginEnd(ProtocolErrorEnum.Token, OtherToken);
                }
                else
                {
                    _helloAccepted = true;
                    Write(LineFormatter.Ok(LineSpace(), 0));
                }
            }

            if (!accepted)
            {
                _ = FinishEndAsync(refused: true);

                return false;
            }

            _host._connections.Add(1);

            return true;
        }

        // The two names are made before the lock, as the member is: a SUB that is refused drops them.
        private bool HandleSub(ClientLine line)
        {
            long sid = line.Id;
            string key = Encoding.ASCII.GetString(line.Key);
            string group = Encoding.ASCII.GetString(line.Group);
            var member = new RemoteMember(this, sid);
            bool open = false;
            bool opened = false;

            lock (_host._lock)
            {
                if (_ending)
                {
                    return false;
                }

                open = _members.ContainsKey(sid);

                if (open)
                {
                    BeginEnd(ProtocolErrorEnum.Protocol, SidOpen);
                }
                else if (_members.Count >= _host._maxSubscriptionsPerConnection)
                {
                    Write(LineFormatter.Error(LineSpace(), sid, ProtocolErrorEnum.Subscriptions, TooManySubscriptions));
                }
                else
                {
                    _host._delivery.Open(member, key, group);
                    _members.Add(sid, member);
                    Write(LineFormatter.Ok(LineSpace(), sid));
                    opened = true;
                }
            }

            if (open)
            {
                _ = FinishEndAsync(refused: true);

                return false;
            }

            if (opened)
            {
                _host._subscriptions.Add(1);
            }

            return true;
        }

        private bool HandleWait(long sid)
        {
            bool armed = false;
            bool taken = false;

            lock (_host._lock)
            {
                if (_ending)
                {
                    return false;
                }

                // A <sid> that is not open is ignored.
                if (!_members.TryGetValue(sid, out RemoteMember? member))
                {
                    return true;
                }

                armed = member.State == GroupMemberStateEnum.Waiting;

                if (armed)
                {
                    BeginEnd(ProtocolErrorEnum.Protocol, MemberArmed);
                }
                else if (_host._delivery.Arm(member))
                {
                    // The member took a pending signal.
                    Write(LineFormatter.Signal(LineSpace(), sid));
                    taken = true;
                }
            }

            if (armed)
            {
                _ = FinishEndAsync(refused: true);

                return false;
            }

            if (taken)
            {
                _host._deliveries.Add(1);
            }

            return true;
        }

        // An armed member becomes idle; a busy or idle one stays as it is. Either way the answer comes after any SIG the
        // host wrote before it.
        private bool HandleUnwait(long sid)
        {
            lock (_host._lock)
            {
                if (_ending)
                {
                    return false;
                }

                // A <sid> that is not open is ignored.
                if (_members.TryGetValue(sid, out RemoteMember? member))
                {
                    _host._delivery.EndWait(member);
                    Write(LineFormatter.Ok(LineSpace(), sid));
                }
            }

            return true;
        }

        // Not answered. A busy member hands its signal on (rule 7).
        private bool HandleUnsub(long sid)
        {
            CloseOutcome outcome;

            lock (_host._lock)
            {
                if (_ending)
                {
                    return false;
                }

                // A <sid> that is not open is ignored.
                if (!_members.Remove(sid, out RemoteMember? member))
                {
                    return true;
                }

                outcome = _host._delivery.Close(member);
                AnswerIfRemote(outcome.Receiver);
            }

            _host.FinishClose(outcome);

            return true;
        }

        // The SIG lines of the members the set woke on this connection come before its answer.
        private bool HandleSet(long rid, ReadOnlySpan<byte> keyBytes)
        {
            // The key is ASCII by the grammar, so widening its bytes gives the characters SignalDelivery.Set takes.
            Span<char> key = stackalloc char[ProtocolRules.MaxNameLength];
            int length = Encoding.ASCII.GetChars(keyBytes, key);
            SetOutcome outcome;
            GroupMember? firstInProcess;
            int woken;

            lock (_host._lock)
            {
                if (_ending)
                {
                    return false;
                }

                outcome = _host._delivery.Set(key[..length]);
                firstInProcess = AnswerRemoteMembers(outcome.FirstWoken, out woken);
                Write(LineFormatter.Ok(LineSpace(), rid, outcome.Groups));
            }

            _host.FinishSet(outcome, firstInProcess, woken);

            return true;
        }

        private bool HandlePing()
        {
            lock (_host._lock)
            {
                if (_ending)
                {
                    return false;
                }

                Write(LineFormatter.Pong(LineSpace()));
            }

            return true;
        }

        // Refuses the connection with its ERR 0 line, unless its end has begun. Returns false, for the line that ends it.
        private bool Refuse(ProtocolErrorEnum code, string text)
        {
            bool began;

            lock (_host._lock)
            {
                began = BeginEnd(code, text);
            }

            if (began)
            {
                _ = FinishEndAsync(refused: true);
            }

            return false;
        }

        // Ends the connection with nothing written, unless its end has begun.
        private void End()
        {
            bool began;

            lock (_host._lock)
            {
                began = BeginEnd(refusal: null, text: null);
            }

            if (began)
            {
                _ = FinishEndAsync(refused: false);
            }
        }

        // Closes the members one at a time (rule 7): a busy one hands its signal on, a remote receiver getting its SIG
        // under the lock and an in-process one completed after it. The connection takes no more lines, so no line opens
        // or closes a member meanwhile; a member woken meanwhile is busy and hands its signal on. On the host's dispose
        // nothing is done: CloseAll has removed every member.
        private void CloseMembers()
        {
            RemoteMember[] members;

            lock (_host._lock)
            {
                members = [.. _members.Values];
            }

            foreach (RemoteMember member in members)
            {
                CloseOutcome outcome;

                lock (_host._lock)
                {
                    if (_host._disposal is not null)
                    {
                        return;
                    }

                    outcome = _host._delivery.Close(member);
                    AnswerIfRemote(outcome.Receiver);
                }

                _host.FinishClose(outcome);
            }
        }

        // Sends the pending output until none is left, then ends under the host's lock. It swaps the buffers under the
        // lock and sends outside it. A failed send ends the connection.
        private async Task FlushAsync()
        {
            try
            {
                while (true)
                {
                    lock (_host._lock)
                    {
                        if (_pending.WrittenCount == 0)
                        {
                            _flush = null;

                            return;
                        }

                        (_pending, _sending) = (_sending, _pending);
                    }

                    ReadOnlyMemory<byte> unsent = _sending.WrittenMemory;

                    while (!unsent.IsEmpty)
                    {
                        int sent = await _socket.SendAsync(unsent, SocketFlags.None).ConfigureAwait(false);
                        unsent = unsent[sent..];
                    }

                    _sending.ResetWrittenCount();
                }
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
            {
                End();
            }
        }

        // The timer's callback. Before HELLO the deadline is the accept plus DeadPeerTimeout, which refuses the
        // connection with protocol; after it, the last receive plus DeadPeerTimeout, which ends it with nothing written.
        // A receive since the timer was armed moves the deadline on, so the timer is armed again for the rest. The timer
        // of an ending connection disposes its socket.
        private void OnTimer()
        {
            // Read before the lock is taken, because the TimeProvider is code of the application.
            DateTimeOffset now = _host._timeProvider.GetUtcNow();
            bool ending;
            bool refused;

            lock (_host._lock)
            {
                ending = _ending;
                refused = !_helloAccepted;

                if (!ending)
                {
                    long from = refused ? _acceptedTicks : Volatile.Read(ref _lastReceivedTicks);
                    var deadline = new DateTimeOffset(from, TimeSpan.Zero);
                    TimeSpan? rest = SignalChecks.TimeUntil(deadline + _host._deadPeerTimeout, now);

                    if (rest != TimeSpan.Zero)
                    {
                        // A rest beyond the longest timer comes only from a clock set back that far.
                        _timer.Change(rest ?? _host._deadPeerTimeout, Timeout.InfiniteTimeSpan);

                        return;
                    }

                    BeginEnd(refused ? ProtocolErrorEnum.Protocol : null, refused ? NoHello : null);
                }
            }

            if (ending)
            {
                _socket.Dispose();
            }
            else
            {
                _ = FinishEndAsync(refused);
            }
        }
    }
}
