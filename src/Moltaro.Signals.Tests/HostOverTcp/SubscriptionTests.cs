using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.HostOverTcp;

// SUB, WAIT, SIG, UNWAIT, UNSUB and SET over loopback, in raw protocol lines. A member is busy once its SUB is
// answered, since joining counts as a signal, until its WAIT arms it. The host handles the lines of a connection in
// order and writes the SIG lines a line causes before its answer, so lines sent together give a fixed sequence of
// answers. An in-process member is in the same groups as the remote ones; a busy remote member that leaves hands its
// signal to it.
public sealed class SubscriptionTests
{
    // Over jobs.mail and its ancestor jobs: (jobs.mail, workers), (jobs, workers) with two members, counted once, and
    // (jobs, cache) in-process. Every member is busy, so no SIG is written.
    [Fact]
    public async Task A_SET_is_answered_with_its_rid_and_the_key_group_pairs_with_a_member_over_the_key_and_its_ancestors()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using ISignalSubscription cache = host.Subscribe("jobs", "cache");
        using RawConnection connection = await RawConnection.HelloAsync(host, cancellationToken);
        await connection.SendAsync("SUB 1 jobs workers\r\nSUB 2 jobs workers\r\nSUB 3 jobs.mail workers\r\n", cancellationToken);
        Assert.Equal("OK 1", await connection.ReadLineAsync(cancellationToken));
        Assert.Equal("OK 2", await connection.ReadLineAsync(cancellationToken));
        Assert.Equal("OK 3", await connection.ReadLineAsync(cancellationToken));

        await connection.SendAsync("SET 4 jobs.mail\r\nSET 5 files\r\n", cancellationToken);

        Assert.Equal("OK 4 3", await connection.ReadLineAsync(cancellationToken));
        Assert.Equal("OK 5 0", await connection.ReadLineAsync(cancellationToken));
    }

    [Fact]
    public async Task A_SET_hands_the_signal_to_a_member_armed_by_WAIT_with_a_SIG_before_its_answer()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using RawConnection connection = await RawConnection.HelloAsync(host, cancellationToken);
        await connection.SendAsync("SUB 1 jobs workers\r\n", cancellationToken);
        Assert.Equal("OK 1", await connection.ReadLineAsync(cancellationToken));

        await connection.SendAsync("WAIT 1\r\nSET 2 jobs\r\n", cancellationToken);

        Assert.Equal("SIG 1", await connection.ReadLineAsync(cancellationToken));
        Assert.Equal("OK 2 1", await connection.ReadLineAsync(cancellationToken));
    }

    // The SET finds the member busy and leaves the signal pending; the WAIT takes it.
    [Fact]
    public async Task A_WAIT_takes_a_pending_signal_with_a_SIG_at_once()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using RawConnection connection = await RawConnection.HelloAsync(host, cancellationToken);
        await connection.SendAsync("SUB 1 jobs workers\r\nSET 2 jobs\r\n", cancellationToken);
        Assert.Equal("OK 1", await connection.ReadLineAsync(cancellationToken));
        Assert.Equal("OK 2 1", await connection.ReadLineAsync(cancellationToken));

        await connection.SendAsync("WAIT 1\r\n", cancellationToken);

        Assert.Equal("SIG 1", await connection.ReadLineAsync(cancellationToken));
    }

    // The PONG shows that the host has handled the WAIT, so the signal goes to the waiting member, not to a later WAIT.
    [Fact]
    public async Task An_in_process_set_hands_the_signal_to_a_remote_member_that_waits()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using RawConnection connection = await RawConnection.HelloAsync(host, cancellationToken);
        await connection.SendAsync("SUB 1 jobs workers\r\nWAIT 1\r\nPING\r\n", cancellationToken);
        Assert.Equal("OK 1", await connection.ReadLineAsync(cancellationToken));
        Assert.Equal("PONG", await connection.ReadLineAsync(cancellationToken));

        SignalSetResult result = await host.SetAsync("jobs", cancellationToken);

        Assert.Equal(1, result.Groups);
        Assert.Equal("SIG 1", await connection.ReadLineAsync(cancellationToken));
    }

    // Had the UNWAIT left the member armed, the SET would have handed it the signal with a SIG before its answer.
    [Fact]
    public async Task An_UNWAIT_before_a_signal_is_answered_and_leaves_the_member_idle_so_a_SET_leaves_the_signal_pending()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using RawConnection connection = await RawConnection.HelloAsync(host, cancellationToken);
        await connection.SendAsync("SUB 1 jobs workers\r\n", cancellationToken);
        Assert.Equal("OK 1", await connection.ReadLineAsync(cancellationToken));

        await connection.SendAsync("WAIT 1\r\nUNWAIT 1\r\nSET 2 jobs\r\nWAIT 1\r\n", cancellationToken);

        Assert.Equal("OK 1", await connection.ReadLineAsync(cancellationToken));
        Assert.Equal("OK 2 1", await connection.ReadLineAsync(cancellationToken));
        Assert.Equal("SIG 1", await connection.ReadLineAsync(cancellationToken));
    }

    // The client reads the SIG before the answer to its UNWAIT, so it knows that the signal came first.
    [Fact]
    public async Task An_UNWAIT_after_a_signal_is_answered_after_the_SIG_of_that_signal()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using RawConnection connection = await RawConnection.HelloAsync(host, cancellationToken);
        await connection.SendAsync("SUB 1 jobs workers\r\n", cancellationToken);
        Assert.Equal("OK 1", await connection.ReadLineAsync(cancellationToken));

        await connection.SendAsync("WAIT 1\r\nSET 2 jobs\r\nUNWAIT 1\r\n", cancellationToken);

        Assert.Equal("SIG 1", await connection.ReadLineAsync(cancellationToken));
        Assert.Equal("OK 2 1", await connection.ReadLineAsync(cancellationToken));
        Assert.Equal("OK 1", await connection.ReadLineAsync(cancellationToken));
    }

    // Busy and idle look the same on the wire until the member leaves: only a busy one hands its signal on.
    [Fact]
    public async Task An_UNWAIT_leaves_a_busy_member_busy_so_it_hands_its_signal_on_when_it_leaves()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using ISignalSubscription staying = host.Subscribe("jobs", "workers");
        await staying.WaitAsync(until: null, cancellationToken);
        ValueTask<SignalWaitResultEnum> wait = staying.WaitAsync(until: null, cancellationToken);
        using RawConnection connection = await RawConnection.HelloAsync(host, cancellationToken);
        await connection.SendAsync("SUB 1 jobs workers\r\n", cancellationToken);
        Assert.Equal("OK 1", await connection.ReadLineAsync(cancellationToken));

        await connection.SendAsync("UNWAIT 1\r\n", cancellationToken);
        Assert.Equal("OK 1", await connection.ReadLineAsync(cancellationToken));
        await connection.SendAsync("UNSUB 1\r\n", cancellationToken);

        Assert.Equal(SignalWaitResultEnum.Signaled, await wait);
    }

    // The PONG comes next: the UNSUB has no answer.
    [Fact]
    public async Task An_UNSUB_is_not_answered_and_a_busy_member_hands_its_signal_on()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using ISignalSubscription staying = host.Subscribe("jobs", "workers");
        await staying.WaitAsync(until: null, cancellationToken);
        ValueTask<SignalWaitResultEnum> wait = staying.WaitAsync(until: null, cancellationToken);
        using RawConnection connection = await RawConnection.HelloAsync(host, cancellationToken);
        await connection.SendAsync("SUB 1 jobs workers\r\n", cancellationToken);
        Assert.Equal("OK 1", await connection.ReadLineAsync(cancellationToken));

        await connection.SendAsync("UNSUB 1\r\nPING\r\n", cancellationToken);

        Assert.Equal("PONG", await connection.ReadLineAsync(cancellationToken));
        Assert.Equal(SignalWaitResultEnum.Signaled, await wait);
    }

    [Fact]
    public async Task An_UNSUB_sent_before_its_SUB_is_answered_closes_the_member_once_it_is_open()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using ISignalSubscription staying = host.Subscribe("jobs", "workers");
        await staying.WaitAsync(until: null, cancellationToken);
        ValueTask<SignalWaitResultEnum> wait = staying.WaitAsync(until: null, cancellationToken);
        using RawConnection connection = await RawConnection.HelloAsync(host, cancellationToken);

        await connection.SendAsync("SUB 1 jobs workers\r\nUNSUB 1\r\n", cancellationToken);

        Assert.Equal("OK 1", await connection.ReadLineAsync(cancellationToken));
        Assert.Equal(SignalWaitResultEnum.Signaled, await wait);
    }

    // The refused <sid> is not open, so its UNSUB, sent before the refusal was read, is ignored. The limit counts the
    // subscriptions that are open: once one is closed, another SUB is answered.
    [Fact]
    public async Task A_SUB_beyond_MaxSubscriptionsPerConnection_is_refused_with_subscriptions_and_the_connection_stays_open()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock(), options => options.MaxSubscriptionsPerConnection = 1);
        using RawConnection connection = await RawConnection.HelloAsync(host, cancellationToken);

        await connection.SendAsync("SUB 1 jobs workers\r\nSUB 2 mail senders\r\nUNSUB 2\r\nPING\r\n", cancellationToken);

        Assert.Equal("OK 1", await connection.ReadLineAsync(cancellationToken));
        Assert.StartsWith("ERR 2 subscriptions ", await connection.ReadLineAsync(cancellationToken));
        Assert.Equal("PONG", await connection.ReadLineAsync(cancellationToken));

        await connection.SendAsync("UNSUB 1\r\nSUB 3 mail senders\r\n", cancellationToken);

        Assert.Equal("OK 3", await connection.ReadLineAsync(cancellationToken));
    }

    // Neither a <sid> that was never opened nor one that was closed gets an answer; the PONG comes next.
    [Fact]
    public async Task WAIT_UNWAIT_and_UNSUB_for_a_sid_that_is_not_open_are_ignored()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using RawConnection connection = await RawConnection.HelloAsync(host, cancellationToken);

        await connection.SendAsync("WAIT 7\r\nUNWAIT 7\r\nUNSUB 7\r\nSUB 1 jobs workers\r\nUNSUB 1\r\n", cancellationToken);
        await connection.SendAsync("WAIT 1\r\nUNWAIT 1\r\nUNSUB 1\r\nPING\r\n", cancellationToken);

        Assert.Equal("OK 1", await connection.ReadLineAsync(cancellationToken));
        Assert.Equal("PONG", await connection.ReadLineAsync(cancellationToken));
    }
}
