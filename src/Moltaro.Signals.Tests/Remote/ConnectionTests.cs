using System.Net;
using System.Net.Sockets;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.Remote;

// A client's connection to a host over loopback: before a host exists, when it connects, when the host refuses it,
// when the host restarts, and when the client is disposed.
public sealed class ConnectionTests
{
    [Fact]
    public async Task A_set_returns_Unavailable_at_once_while_no_host_exists()
    {
        // A port the system chose, freed again, so that nothing listens on it.
        IPEndPoint nobody;
        using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            nobody = (IPEndPoint)socket.LocalEndPoint!;
        }

        await using var client = new SignalClient(LoopbackOptions.Client(nobody));
        client.Start();

        ValueTask<SignalSetResult> set = client.SetAsync("jobs", TestContext.Current.CancellationToken);

        Assert.True(set.IsCompletedSuccessfully);
        SignalSetResult result = await set;
        Assert.Equal(SignalSetStatusEnum.Unavailable, result.Status);
        Assert.Equal(0, result.Groups);
    }

    // A member subscribed before the client is started is opened on the host once the client connects, and Joined
    // comes only once the host has opened it: the set of the host then counts it.
    [Fact]
    public async Task The_first_wait_returns_Joined_once_the_host_has_opened_the_member()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        host.Start();
        await using var client = new SignalClient(LoopbackOptions.Client(host.LocalEndPoint!));
        using ISignalSubscription lane = client.Subscribe("jobs", "workers");

        client.Start();

        Assert.Equal(SignalWaitResultEnum.Joined, await lane.WaitAsync(until: null, cancellationToken));
        Assert.Equal(1, (await host.SetAsync("jobs", cancellationToken)).Groups);
    }

    [Fact]
    public async Task A_wrong_token_gives_Disconnected_with_the_code_token_at_the_start_of_Reason()
    {
        await using var host = new SignalHost(LoopbackOptions.Host());
        host.Start();
        SignalClientOptions options = LoopbackOptions.Client(host.LocalEndPoint!);
        options.Token = "wrong-token";
        await using var client = new SignalClient(options);
        var change = new TaskCompletionSource<SignalConnectionChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.StateChanged += (_, arguments) => change.TrySetResult(arguments);

        client.Start();
        SignalConnectionChangedEventArgs refusal = await change.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SignalConnectionStateEnum.Disconnected, refusal.State);
        Assert.StartsWith("token", refusal.Reason);
        Assert.Equal(SignalConnectionStateEnum.Disconnected, client.State);
    }

    [Fact]
    public async Task A_waiting_member_returns_Joined_from_a_new_host_on_the_same_port()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var first = new SignalHost(LoopbackOptions.Host());
        first.Start();
        IPEndPoint endPoint = first.LocalEndPoint!;
        await using var client = new SignalClient(LoopbackOptions.Client(endPoint));
        client.Start();
        using ISignalSubscription lane = client.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null, cancellationToken);
        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null, cancellationToken);

        await first.DisposeAsync();
        SignalHostOptions options = LoopbackOptions.Host();
        options.ListenEndPoint = endPoint;
        await using var second = new SignalHost(options);
        second.Start();

        Assert.Equal(SignalWaitResultEnum.Joined, await wait);
    }

    [Fact]
    public async Task Disposing_the_client_ends_its_pending_waits_and_refuses_later_calls()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        host.Start();
        var client = new SignalClient(LoopbackOptions.Client(host.LocalEndPoint!));
        client.Start();
        ISignalSubscription lane = client.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null, cancellationToken);
        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null, cancellationToken);

        await client.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await wait);
        Assert.Equal(SignalConnectionStateEnum.Disconnected, client.State);
        Assert.Throws<ObjectDisposedException>(() => lane.WaitAsync(until: null, cancellationToken));
        Assert.Throws<ObjectDisposedException>(() => client.Subscribe("jobs", "workers"));
        Assert.Throws<ObjectDisposedException>(() => client.SetAsync("jobs", cancellationToken));
        Assert.Throws<ObjectDisposedException>(client.Start);

        // The subscription ended with the client; its own dispose does nothing, and so does a second dispose of the
        // client.
        lane.Dispose();
        await client.DisposeAsync();
    }
}
