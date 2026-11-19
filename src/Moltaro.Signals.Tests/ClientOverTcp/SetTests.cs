using Moltaro.Signals.Tests.HostOverTcp;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.ClientOverTcp;

// The sets of a client against a raw host: a set never waits for a connection and is never queued for a later one; a
// connected client waits for the host's answer, and a set whose answer is lost with the connection is Unavailable.
// Concurrent sets share the connection, and none waits for another.
public sealed class SetTests
{
    // The client is not connected while its first attempt waits for the answer to its HELLO.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_set_made_while_the_client_is_not_connected_is_Unavailable_at_once_and_never_reaches_the_host()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(new FakeClock());
        var changes = new StateChanges(client);
        client.Start();
        using RawConnection connection = await host.AcceptHelloAsync(cancellationToken);

        ValueTask<SignalSetResult> early = client.SetAsync("early", cancellationToken);

        Assert.True(early.IsCompletedSuccessfully);
        Assert.Equal(SignalSetStatusEnum.Unavailable, (await early).Status);

        await connection.SendAsync("OK 0\r\n", cancellationToken);
        Assert.Equal(SignalConnectionStateEnum.Connected, (await changes.NextAsync(cancellationToken)).State);
        ValueTask<SignalSetResult> later = client.SetAsync("later", cancellationToken);

        long rid = await connection.ReadIdAsync("SET", "later", cancellationToken);
        await connection.SendAsync($"OK {rid} 0\r\n", cancellationToken);

        SignalSetResult result = await later;
        Assert.Equal(SignalSetStatusEnum.Applied, result.Status);
        Assert.Equal(0, result.Groups);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_set_whose_answer_is_lost_with_the_connection_returns_Unavailable()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(new FakeClock());
        var changes = new StateChanges(client);
        client.Start();
        RawConnection connection = await host.AcceptAsync(changes, cancellationToken);
        ValueTask<SignalSetResult> set = client.SetAsync("jobs", cancellationToken);
        await connection.ReadIdAsync("SET", "jobs", cancellationToken);

        connection.Dispose();

        SignalSetResult result = await set;
        Assert.Equal(SignalSetStatusEnum.Unavailable, result.Status);
        Assert.Equal(0, result.Groups);
    }

    // The host reads both lines, in the order of the calls, before it answers either; each set returns the count of
    // its own answer.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Concurrent_sets_share_the_connection_and_none_waits_for_another()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(new FakeClock());
        var changes = new StateChanges(client);
        client.Start();
        using RawConnection connection = await host.AcceptAsync(changes, cancellationToken);

        ValueTask<SignalSetResult> jobs = client.SetAsync("jobs", cancellationToken);
        ValueTask<SignalSetResult> mail = client.SetAsync("mail", cancellationToken);
        long jobsRid = await connection.ReadIdAsync("SET", "jobs", cancellationToken);
        long mailRid = await connection.ReadIdAsync("SET", "mail", cancellationToken);
        await connection.SendAsync($"OK {jobsRid} 2\r\nOK {mailRid} 1\r\n", cancellationToken);

        Assert.Equal(2, (await jobs).Groups);
        Assert.Equal(1, (await mail).Groups);
    }
}
