using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.Remote;

// A host that holds its limit of connections, over loopback: the client beyond the limit is refused, stays
// Disconnected and tries again with backoff, and connects once the host has room again.
public sealed class ConnectionLimitTests
{
    // The second client takes its time from a fake clock: its first attempt is at once, and each later one comes when
    // the test advances the clock. No heartbeat of it falls due while the test advances the clock to its next attempt.
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan DeadPeerTimeout = TimeSpan.FromSeconds(30);

    // The host refuses the first attempt of the second client while the first client is connected: the second client
    // then waits for its next attempt. The host has room again once it has closed the socket of the first client, which
    // the second client cannot see, so the test advances its clock from one attempt to the next until the host accepts
    // one. Every notification of Connected records whether the first client was disposed by then.
    [Fact]
    public async Task A_second_client_never_reaches_Connected_while_the_first_holds_the_only_connection_of_the_host()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SignalHostOptions hostOptions = LoopbackOptions.Host();
        hostOptions.MaxConnections = 1;
        await using var host = new SignalHost(hostOptions);
        host.Start();
        await using var first = new SignalClient(LoopbackOptions.Client(host.LocalEndPoint!));
        var firstConnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.StateChanged += (_, change) =>
        {
            if (change.State == SignalConnectionStateEnum.Connected)
            {
                firstConnected.TrySetResult();
            }
        };
        first.Start();
        await firstConnected.Task.WaitAsync(cancellationToken);

        var clock = new FakeClock();
        SignalClientOptions secondOptions = LoopbackOptions.Client(host.LocalEndPoint!);
        secondOptions.TimeProvider = clock;
        secondOptions.ReconnectDelay = ReconnectDelay;
        secondOptions.MaxReconnectDelay = ReconnectDelay;
        secondOptions.HeartbeatInterval = HeartbeatInterval;
        secondOptions.DeadPeerTimeout = DeadPeerTimeout;
        await using var second = new SignalClient(secondOptions);
        bool firstDisposed = false;
        var secondConnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        second.StateChanged += (_, change) =>
        {
            if (change.State == SignalConnectionStateEnum.Connected)
            {
                secondConnected.TrySetResult(Volatile.Read(ref firstDisposed));
            }
        };

        second.Start();
        await clock.WaitForTimerAsync(clock.GetUtcNow() + ReconnectDelay, cancellationToken);

        Assert.Equal(SignalConnectionStateEnum.Disconnected, second.State);

        Volatile.Write(ref firstDisposed, true);
        await first.DisposeAsync();

        while (!secondConnected.Task.IsCompleted)
        {
            Task nextAttemptArmed = clock.WaitForTimerAsync(clock.GetUtcNow() + ReconnectDelay, cancellationToken);

            if (await Task.WhenAny(secondConnected.Task, nextAttemptArmed) == nextAttemptArmed)
            {
                await nextAttemptArmed;
                clock.Advance(ReconnectDelay);
            }
        }

        Assert.True(await secondConnected.Task);
    }
}
