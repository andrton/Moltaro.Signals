using System.Buffers;
using System.Net.Sockets;
using Moltaro.Signals.Protocol;

namespace Moltaro.Signals;

// The output of one connection, of the host or of a client: the lines its side writes, and the flush that sends them.
// The connection owns it; the one lock of the host or of the client guards its pending output and its flush.
//
// A line is written into LineSpace and taken by Write, both in the section under the lock that decided it, so that the
// output follows the order of the decisions. Write starts a flush on the thread pool when none runs, so at most one
// runs. The flush swaps the pending output with the output it sends under the lock and sends outside it, until nothing
// is pending: no lock is held across a send.
//
// The two sides differ in two points, which their connections give it. Each ends its connection in its own way when a
// send fails. And the client counts a line as sent when it is handed to the socket, for its heartbeat, so its output
// takes the timestamp of every send from the clock of the client; the host counts only what it receives, and gives no
// clock.
internal sealed class ConnectionOutput
{
    // The room each buffer starts with; it grows when more is pending.
    private const int BufferLength = 4096;

    // The one lock of the host or of the client.
    private readonly Lock _lock;

    private readonly Socket _socket;

    // The clock of the client's heartbeat; null for a connection of the host.
    private readonly TimeProvider? _sentClock;

    // Ends the connection when a send fails.
    private readonly Action _sendFailed;

    // FlushAsync as a delegate made once, so that starting a flush makes no new delegate.
    private readonly Func<Task> _flushAsync;

    // The output not sent yet, guarded by the lock. The flush swaps it with _sending, which only the flush touches, and
    // at most one flush runs.
    private ArrayBufferWriter<byte> _pending = new(BufferLength);

    private ArrayBufferWriter<byte> _sending = new(BufferLength);

    // The flush that runs; null when none does. Guarded by the lock.
    private Task? _flush;

    // The timestamp of the last output handed to the socket, written by the flush when a clock was given.
    private long _lastSentTimestamp;

    internal ConnectionOutput(Lock sideLock, Socket socket, TimeProvider? sentClock, Action sendFailed)
    {
        _lock = sideLock;
        _socket = socket;
        _sentClock = sentClock;
        _sendFailed = sendFailed;
        _flushAsync = FlushAsync;
    }

    // The timestamp of the last output handed to the socket, of the clock that was given; 0 before the first.
    internal long LastSentTimestamp => Volatile.Read(ref _lastSentTimestamp);

    // Under the lock: room for one line at the end of the pending output.
    internal Span<byte> LineSpace()
    {
        return _pending.GetSpan(ProtocolRules.LineRoom);
    }

    // Under the lock: takes the line just written into LineSpace into the pending output, and starts a flush when none
    // runs. The flush runs on the thread pool, never inline in the section that wrote the line.
    internal void Write(int length)
    {
        _pending.Advance(length);

        if (_flush is null)
        {
            // The flush serves the connection, not the code that wrote the line, so it does not capture the execution
            // context of that code.
            using AsyncFlowControl? flow = ExecutionContext.IsFlowSuppressed() ? null : ExecutionContext.SuppressFlow();

            _flush = Task.Run(_flushAsync);
        }
    }

    // Completes when the output written so far has been sent, with any written meanwhile, or when a send failed and
    // ended the connection: the flush that runs, or a completed task when none does.
    internal Task WhenSent()
    {
        lock (_lock)
        {
            return _flush ?? Task.CompletedTask;
        }
    }

    // Sends the pending output until none is left, then ends under the lock. It swaps the buffers under the lock and
    // sends outside it. A failed send ends the connection.
    private async Task FlushAsync()
    {
        try
        {
            while (true)
            {
                lock (_lock)
                {
                    if (_pending.WrittenCount == 0)
                    {
                        _flush = null;

                        return;
                    }

                    (_pending, _sending) = (_sending, _pending);
                }

                // A line counts as sent when it is handed to the socket. The other side cannot read it before, so the
                // instant is that at which the line was written unless the clock moved meanwhile.
                if (_sentClock is not null)
                {
                    Volatile.Write(ref _lastSentTimestamp, _sentClock.GetTimestamp());
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
            _sendFailed();
        }
    }
}
