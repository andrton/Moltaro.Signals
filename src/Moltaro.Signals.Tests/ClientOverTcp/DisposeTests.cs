using Moltaro.Signals.Tests.HostOverTcp;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.ClientOverTcp;

// The dispose of a client, against a raw host: it ends a set that waits for the host's answer, closes the connection,
// and stops reconnecting. The client waits for its next attempt through its clock, so once it has stopped no timer of
// it remains armed for that attempt.
public sealed class DisposeTests
{
    // No heartbeat falls due before the next attempt.
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan DeadPeerTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Disposing_the_client_ends_a_set_that_waits_for_the_answer_of_the_host_and_closes_the_connection()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(new FakeClock(), Configure);
        var changes = new StateChanges(client);
        client.Start();
        using RawConnection connection = await host.AcceptAsync(changes, cancellationToken);
        ValueTask<SignalSetResult> set = client.SetAsync("jobs", cancellationToken);
        await connection.ReadIdAsync("SET", "jobs", cancellationToken);

        await client.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await set);
        Assert.Null(await connection.ReadLineAsync(cancellationToken));
    }

    [Fact]
    public async Task Disposing_the_client_while_it_waits_for_its_next_attempt_stops_reconnecting()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(clock, Configure);
        var changes = new StateChanges(client);
        client.Start();
        (await host.AcceptAsync(changes, cancellationToken)).Dispose();
        Assert.Equal(SignalConnectionStateEnum.Disconnected, (await changes.NextAsync(cancellationToken)).State);
        DateTimeOffset nextAttempt = clock.GetUtcNow() + ReconnectDelay;
        await clock.WaitForTimerAsync(nextAttempt, cancellationToken);

        await client.DisposeAsync();

        Assert.False(clock.IsTimerDueBy(nextAttempt));
        Assert.Equal(SignalConnectionStateEnum.Disconnected, client.State);
    }

    private static void Configure(SignalClientOptions options)
    {
        options.ReconnectDelay = ReconnectDelay;
        options.HeartbeatInterval = HeartbeatInterval;
        options.DeadPeerTimeout = DeadPeerTimeout;
    }
}
