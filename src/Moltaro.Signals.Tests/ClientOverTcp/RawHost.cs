using System.Net;
using System.Net.Sockets;
using Moltaro.Signals.Tests.HostOverTcp;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.ClientOverTcp;

// A host that speaks the protocol by hand, for tests of a client: a listener on a free port of the loopback address
// that hands each attempt of the client to the test as a RawConnection. The test reads the client's lines and writes
// the host's, holds them back, or writes nothing at all. Its clients take their time from a fake clock, so no timer of
// a client fires, and no attempt starts after the first, unless the test advances the clock.
internal sealed class RawHost : IDisposable
{
    private readonly Socket _listener;

    private RawHost(Socket listener)
    {
        _listener = listener;
    }

    public static RawHost Start()
    {
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen();

        return new RawHost(listener);
    }

    // A client of this host, not started yet, with the options of LoopbackOptions and the fake clock.
    public SignalClient CreateClient(FakeClock clock, Action<SignalClientOptions>? configure = null)
    {
        SignalClientOptions options = LoopbackOptions.Client(_listener.LocalEndPoint!);
        options.TimeProvider = clock;
        configure?.Invoke(options);

        return new SignalClient(options);
    }

    // The next attempt of the client: accepts its connection and reads its HELLO, which it leaves unanswered.
    public async Task<RawConnection> AcceptHelloAsync(CancellationToken cancellationToken)
    {
        Socket socket = await _listener.AcceptAsync(cancellationToken);
        socket.NoDelay = true;
        var connection = new RawConnection(socket);

        Assert.Equal($"HELLO 1 {LoopbackOptions.Token}", await connection.ReadLineAsync(cancellationToken));

        return connection;
    }

    // The next attempt of the client, accepted: answers its HELLO with OK 0 and returns once the next notification of
    // the client reports Connected.
    public async Task<RawConnection> AcceptAsync(StateChanges changes, CancellationToken cancellationToken)
    {
        RawConnection connection = await AcceptHelloAsync(cancellationToken);

        await connection.SendAsync("OK 0\r\n", cancellationToken);
        Assert.Equal(SignalConnectionStateEnum.Connected, (await changes.NextAsync(cancellationToken)).State);

        return connection;
    }

    public void Dispose()
    {
        _listener.Dispose();
    }
}
