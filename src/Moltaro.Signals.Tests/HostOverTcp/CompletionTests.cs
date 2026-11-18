using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.HostOverTcp;

// The rule "Completions" of the contract over a connection: an in-process member woken by a SET of a connection
// continues off the thread that reads that connection. Were the member's code run there, the host would not answer
// the next line of that connection, because that code is blocked until the answer has been read.
public sealed class CompletionTests
{
    [Fact]
    public async Task The_host_answers_the_next_line_of_a_connection_while_the_member_its_SET_woke_is_still_blocked()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null, cancellationToken);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        Task<SignalWaitResultEnum> pass = PassThatBlocksAsync(lane, blocked, release, cancellationToken);
        using RawConnection connection = await RawConnection.HelloAsync(host, cancellationToken);

        await connection.SendAsync("SET 1 jobs\r\n", cancellationToken);
        Assert.Equal("OK 1 1", await connection.ReadLineAsync(cancellationToken));
        await blocked.Task.WaitAsync(cancellationToken);
        await connection.SendAsync("PING\r\n", cancellationToken);
        Assert.Equal("PONG", await connection.ReadLineAsync(cancellationToken));
        release.Set();

        Assert.Equal(SignalWaitResultEnum.Signaled, await pass);
    }

    // The member's code after its await reports that it runs, then blocks until the test releases it.
    // ConfigureAwait(false) lets it continue wherever the library completes the wait, not on a context of the test
    // framework.
    private static async Task<SignalWaitResultEnum> PassThatBlocksAsync(
        ISignalSubscription lane,
        TaskCompletionSource blocked,
        ManualResetEventSlim release,
        CancellationToken cancellationToken)
    {
        SignalWaitResultEnum result = await lane.WaitAsync(until: null, cancellationToken).ConfigureAwait(false);

        blocked.SetResult();
        release.Wait(cancellationToken);

        return result;
    }
}
