using Moltaro.Signals.Tests.RawProtocol;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.ClientOverTcp;

// The attempts of a client to connect, against a raw host and with a fake clock: one attempt is bounded by
// DeadPeerTimeout, one that ends without the host's answer to its HELLO raises no notification, the delay between
// attempts grows from ReconnectDelay to MaxReconnectDelay and falls back once the host accepts, and every open
// subscription is opened on a new connection before a request made after Connected.
//
// While the client waits for its next attempt, no other timer of the client is due before that attempt, so the
// earliest timer is the one that ends the wait. The clock does not move while an attempt runs, so an attempt ends at
// the instant at which the raw host ended it.
public sealed class ReconnectionTests
{
    // Not the defaults, so that a time the client did not take from its options shows. Neither a heartbeat nor the
    // bound of an attempt is due before the next attempt.
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan DeadPeerTimeout = TimeSpan.FromSeconds(5);

    // The first attempt is at once. The host ends every attempt before it answers the HELLO, until the fifth, which it
    // accepts and then ends. The next notification after each failed attempt is Connected, so a failed attempt raised
    // nothing.
    [Fact]
    public async Task The_delay_between_attempts_doubles_from_ReconnectDelay_up_to_MaxReconnectDelay_and_falls_back_once_the_host_accepts()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(clock, Configure);
        var changes = new StateChanges(client);
        client.Start();
        (await host.AcceptHelloAsync(cancellationToken)).Dispose();

        TimeSpan[] growingDelays = [ReconnectDelay, 2 * ReconnectDelay, MaxReconnectDelay];

        foreach (TimeSpan delay in growingDelays)
        {
            await AdvanceToNextAttemptAsync(clock, delay, cancellationToken);
            (await host.AcceptHelloAsync(cancellationToken)).Dispose();
        }

        await AdvanceToNextAttemptAsync(clock, MaxReconnectDelay, cancellationToken);
        (await host.AcceptAsync(changes, cancellationToken)).Dispose();
        Assert.Equal(SignalConnectionStateEnum.Disconnected, (await changes.NextAsync(cancellationToken)).State);

        await AdvanceToNextAttemptAsync(clock, ReconnectDelay, cancellationToken);
        using RawConnection next = await host.AcceptHelloAsync(cancellationToken);
    }

    // The first attempt gets no answer and ends at DeadPeerTimeout; the host ends the second before it answers; it
    // answers the third just before DeadPeerTimeout, which the client accepts. Connected is the first notification.
    [Fact]
    public async Task An_attempt_that_gets_no_answer_to_its_HELLO_within_DeadPeerTimeout_or_whose_connection_ends_before_the_answer_raises_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        TimeSpan justBefore = DeadPeerTimeout - TimeSpan.FromMilliseconds(1);
        var clock = new FakeClock();
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(clock, Configure);
        var changes = new StateChanges(client);
        client.Start();

        using (RawConnection unanswered = await host.AcceptHelloAsync(cancellationToken))
        {
            await clock.WaitForTimerAsync(clock.GetUtcNow() + DeadPeerTimeout, cancellationToken);
            clock.Advance(DeadPeerTimeout);

            Assert.Null(await unanswered.ReadLineAsync(cancellationToken));
        }

        await AdvanceToNextAttemptAsync(clock, ReconnectDelay, cancellationToken);
        (await host.AcceptHelloAsync(cancellationToken)).Dispose();

        await AdvanceToNextAttemptAsync(clock, 2 * ReconnectDelay, cancellationToken);
        using RawConnection answered = await host.AcceptHelloAsync(cancellationToken);
        await clock.WaitForTimerAsync(clock.GetUtcNow() + DeadPeerTimeout, cancellationToken);
        clock.Advance(justBefore);
        await answered.SendAsync("OK 0\r\n", cancellationToken);

        Assert.Equal(SignalConnectionStateEnum.Connected, (await changes.NextAsync(cancellationToken)).State);
    }

    // On every connection, the first lines are the openings of both members, then the set made after Connected: on
    // the first connection, and on the next one, after the host ended the first.
    [Fact]
    public async Task Every_open_subscription_is_opened_on_a_new_connection_before_a_request_made_after_Connected()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(clock, Configure);
        var changes = new StateChanges(client);
        using ISignalSubscription first = client.Subscribe("jobs", "workers");
        using ISignalSubscription second = client.Subscribe("jobs", "workers");
        client.Start();

        using (RawConnection connection = await host.AcceptAsync(changes, cancellationToken))
        {
            ValueTask<SignalSetResult> set = client.SetAsync("mail", cancellationToken);

            await connection.ReadIdAsync("SUB", "jobs workers", cancellationToken);
            await connection.ReadIdAsync("SUB", "jobs workers", cancellationToken);
            await connection.ReadIdAsync("SET", "mail", cancellationToken);
        }

        Assert.Equal(SignalConnectionStateEnum.Disconnected, (await changes.NextAsync(cancellationToken)).State);
        await AdvanceToNextAttemptAsync(clock, ReconnectDelay, cancellationToken);

        using RawConnection reconnected = await host.AcceptAsync(changes, cancellationToken);
        ValueTask<SignalSetResult> later = client.SetAsync("mail", cancellationToken);

        await reconnected.ReadIdAsync("SUB", "jobs workers", cancellationToken);
        await reconnected.ReadIdAsync("SUB", "jobs workers", cancellationToken);
        await reconnected.ReadIdAsync("SET", "mail", cancellationToken);
    }

    // The next attempt has to be armed for exactly delay after the clock's present instant, at which the last attempt or
    // connection ended: not earlier.
    private static async Task AdvanceToNextAttemptAsync(FakeClock clock, TimeSpan delay, CancellationToken cancellationToken)
    {
        DateTimeOffset due = clock.GetUtcNow() + delay;

        await clock.WaitForTimerAsync(due, cancellationToken);
        Assert.False(clock.IsTimerDueBy(due - TimeSpan.FromMilliseconds(1)), $"The next attempt is due {delay} after the last one ended, not earlier.");

        clock.Advance(delay);
    }

    private static void Configure(SignalClientOptions options)
    {
        options.ReconnectDelay = ReconnectDelay;
        options.MaxReconnectDelay = MaxReconnectDelay;
        options.HeartbeatInterval = HeartbeatInterval;
        options.DeadPeerTimeout = DeadPeerTimeout;
    }
}
