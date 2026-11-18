using System.Net;
using System.Net.Sockets;
using System.Text;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.HostOverTcp;

// A client that speaks the protocol by hand over a loopback socket: it sends the text a test writes, each \r\n
// included, and reads the lines the host writes, one at a time.
internal sealed class RawConnection : IDisposable
{
    private readonly NetworkStream _stream;

    // The bytes received and not read yet lie from _start to _end.
    private readonly byte[] _received = new byte[4096];

    private int _start;

    private int _end;

    private RawConnection(Socket socket)
    {
        _stream = new NetworkStream(socket, ownsSocket: true);
    }

    // Connects to the host and sends nothing.
    public static async Task<RawConnection> ConnectAsync(SignalHost host, CancellationToken cancellationToken)
    {
        IPEndPoint endPoint = host.LocalEndPoint!;
        var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        await socket.ConnectAsync(endPoint, cancellationToken);

        return new RawConnection(socket);
    }

    // Connects to the host and sends the HELLO of the test token, which the host answers with OK 0.
    public static async Task<RawConnection> HelloAsync(SignalHost host, CancellationToken cancellationToken)
    {
        RawConnection connection = await ConnectAsync(host, cancellationToken);

        await connection.SendAsync($"HELLO 1 {LoopbackOptions.Token}\r\n", cancellationToken);
        Assert.Equal("OK 0", await connection.ReadLineAsync(cancellationToken));

        return connection;
    }

    public async Task SendAsync(string text, CancellationToken cancellationToken)
    {
        await _stream.WriteAsync(Encoding.ASCII.GetBytes(text), cancellationToken);
    }

    // The next line the host wrote, without its \r\n; null once the host has ended the connection after its last line.
    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            int lineEnd = Array.IndexOf(_received, (byte)'\n', _start, _end - _start);

            if (lineEnd >= 0)
            {
                Assert.True(lineEnd > _start && _received[lineEnd - 1] == '\r', "A line of the host ends with \\r\\n.");

                string line = Encoding.ASCII.GetString(_received, _start, lineEnd - 1 - _start);
                _start = lineEnd + 1;

                return line;
            }

            // Only the part of a line that has not arrived in full is kept, at the front.
            Array.Copy(_received, _start, _received, 0, _end - _start);
            _end -= _start;
            _start = 0;

            int received = await ReceiveAsync(cancellationToken);

            if (received == 0)
            {
                Assert.True(_end == 0, "The host ended the connection inside a line.");

                return null;
            }

            _end += received;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }

    // A reset ends the connection as a close does: the host disposes the socket of a connection that it ends for
    // silence or for its own dispose at once, while it still receives on it, and .NET resets a connection whose socket
    // is disposed with a receive pending.
    private async Task<int> ReceiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _stream.ReadAsync(_received.AsMemory(_end), cancellationToken);
        }
        catch (IOException exception) when (exception.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset })
        {
            return 0;
        }
    }
}
