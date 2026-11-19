using System.Threading.Channels;
using Moltaro.Signals.Tests.RawProtocol;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.ClientOverTcp;

// StateChanged of a client, against a raw host, by the rule "Notifications" of the contract: a handler runs on a
// thread-pool thread, never inside a call of the application and never on the thread that reads the connection; the
// notifications come one at a time in the order of the changes; a handler that blocks holds up only the later
// notifications; a handler may dispose the client.
public sealed class NotificationTests
{
    // Each attempt follows the last after ReconnectDelay, which MaxReconnectDelay keeps from growing. No heartbeat falls
    // due while a test advances the clock to the next attempt.
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan DeadPeerTimeout = TimeSpan.FromSeconds(30);

    private static readonly string[] Codes = ["version", "token", "connections", "protocol"];

    // The host refuses four attempts, one with each code that refuses a connection, accepts the fifth and then ends it.
    // The handler of the first notification blocks until the client has gone through all of these changes, which it
    // does meanwhile: its member returns Joined from the fifth. The later notifications wait behind the first.
    [Fact]
    public async Task Notifications_come_one_at_a_time_in_the_order_of_the_changes_with_the_code_of_each_refused_attempt_at_the_start_of_Reason()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(clock, Configure);
        var seen = Channel.CreateUnbounded<(SignalConnectionChangedEventArgs Change, bool OnThreadPool)>();
        using var release = new ManualResetEventSlim();
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int handlersRunning = 0;
        int handlersStarted = 0;
        bool overlapped = false;
        client.StateChanged += (_, change) =>
        {
            if (Interlocked.Increment(ref handlersRunning) > 1)
            {
                Volatile.Write(ref overlapped, true);
            }

            seen.Writer.TryWrite((change, Thread.CurrentThread.IsThreadPoolThread));

            if (Interlocked.Increment(ref handlersStarted) == 1)
            {
                release.Wait();
                released.SetResult();
            }

            Interlocked.Decrement(ref handlersRunning);
        };
        using ISignalSubscription lane = client.Subscribe("jobs", "workers");
        client.Start();

        foreach (string code in Codes)
        {
            using (RawConnection refused = await host.AcceptHelloAsync(cancellationToken))
            {
                await refused.SendAsync($"ERR 0 {code} The host refuses this attempt.\r\n", cancellationToken);
            }

            await clock.WaitForTimerAsync(clock.GetUtcNow() + ReconnectDelay, cancellationToken);
            clock.Advance(ReconnectDelay);
        }

        RawConnection accepted = await host.AcceptHelloAsync(cancellationToken);
        await accepted.SendAsync("OK 0\r\n", cancellationToken);
        long sid = await accepted.ReadIdAsync("SUB", "jobs workers", cancellationToken);
        await accepted.SendAsync($"OK {sid}\r\n", cancellationToken);
        Assert.Equal(SignalWaitResultEnum.Joined, await lane.WaitAsync(until: null, cancellationToken));
        accepted.Dispose();
        release.Set();
        await released.Task.WaitAsync(cancellationToken);

        foreach (string code in Codes)
        {
            (SignalConnectionChangedEventArgs refusal, bool refusalOnThreadPool) = await seen.Reader.ReadAsync(cancellationToken);
            Assert.Equal(SignalConnectionStateEnum.Disconnected, refusal.State);
            Assert.StartsWith(code, refusal.Reason);
            Assert.True(refusalOnThreadPool);
        }

        (SignalConnectionChangedEventArgs connected, bool connectedOnThreadPool) = await seen.Reader.ReadAsync(cancellationToken);
        Assert.Equal(SignalConnectionStateEnum.Connected, connected.State);
        Assert.Null(connected.Reason);
        Assert.True(connectedOnThreadPool);

        (SignalConnectionChangedEventArgs ended, bool endedOnThreadPool) = await seen.Reader.ReadAsync(cancellationToken);
        Assert.Equal(SignalConnectionStateEnum.Disconnected, ended.State);
        Assert.NotNull(ended.Reason);
        Assert.True(endedOnThreadPool);

        Assert.False(Volatile.Read(ref overlapped));
    }

    // The handler of Connected blocks until the end of the test. Meanwhile the client reads the host's answers and
    // signals and sends its heartbeat.
    [Fact]
    public async Task While_a_handler_blocks_sets_waits_and_heartbeats_go_on()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(clock, Configure);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        client.StateChanged += (_, change) =>
        {
            if (change.State == SignalConnectionStateEnum.Connected)
            {
                blocked.TrySetResult();
                release.Wait();
                released.TrySetResult();
            }
        };
        using ISignalSubscription lane = client.Subscribe("jobs", "workers");
        client.Start();
        using RawConnection connection = await host.AcceptHelloAsync(cancellationToken);
        await connection.SendAsync("OK 0\r\n", cancellationToken);
        await blocked.Task.WaitAsync(cancellationToken);
        DateTimeOffset t0 = clock.GetUtcNow();

        long sid = await connection.ReadIdAsync("SUB", "jobs workers", cancellationToken);
        await connection.SendAsync($"OK {sid}\r\n", cancellationToken);
        Assert.Equal(SignalWaitResultEnum.Joined, await lane.WaitAsync(until: null, cancellationToken));

        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null, cancellationToken);
        Assert.Equal($"WAIT {sid}", await connection.ReadLineAsync(cancellationToken));
        await connection.SendAsync($"SIG {sid}\r\n", cancellationToken);
        Assert.Equal(SignalWaitResultEnum.Signaled, await wait);

        ValueTask<SignalSetResult> set = client.SetAsync("jobs", cancellationToken);
        long rid = await connection.ReadIdAsync("SET", "jobs", cancellationToken);
        await connection.SendAsync($"OK {rid} 1\r\n", cancellationToken);
        Assert.Equal(1, (await set).Groups);

        await clock.WaitForTimerAsync(t0 + HeartbeatInterval, cancellationToken);
        clock.Advance(HeartbeatInterval);
        Assert.Equal("PING", await connection.ReadLineAsync(cancellationToken));

        release.Set();
        await released.Task.WaitAsync(cancellationToken);
    }

    // The handler of Connected disposes the client and waits until the dispose has completed before it returns: a
    // dispose that waited for the handler that is running would never complete.
    [Fact]
    public async Task A_handler_that_disposes_the_client_completes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(new FakeClock(), Configure);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StateChanged += (_, _) =>
        {
            DisposeAndWait(client);
            disposed.TrySetResult();
        };
        client.Start();
        using RawConnection connection = await host.AcceptHelloAsync(cancellationToken);

        await connection.SendAsync("OK 0\r\n", cancellationToken);
        await disposed.Task.WaitAsync(cancellationToken);

        Assert.Equal(SignalConnectionStateEnum.Disconnected, client.State);
        Assert.Throws<ObjectDisposedException>(() => client.SetAsync("jobs", cancellationToken));
        Assert.Null(await connection.ReadLineAsync(cancellationToken));
    }

    private static void DisposeAndWait(SignalClient client)
    {
        client.DisposeAsync().AsTask().Wait();
    }

    private static void Configure(SignalClientOptions options)
    {
        options.ReconnectDelay = ReconnectDelay;
        options.MaxReconnectDelay = ReconnectDelay;
        options.HeartbeatInterval = HeartbeatInterval;
        options.DeadPeerTimeout = DeadPeerTimeout;
    }
}
