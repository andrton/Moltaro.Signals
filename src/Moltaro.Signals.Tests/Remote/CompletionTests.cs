using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.Remote;

// The rule "Completions" of the contract for a client, over loopback: the code that continues after an awaited
// WaitAsync or SetAsync of a client never runs on the thread that reads its connection. Were it run there, the client
// would not read the answer to its next set while that code is blocked.
public sealed class CompletionTests
{
    // The member's WaitAsync is called before the first set, so its WAIT reaches the host first and the set wakes it.
    [Fact]
    public async Task Another_set_of_a_client_completes_while_the_code_after_its_awaited_wait_and_set_is_blocked()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        host.Start();
        await using var client = new SignalClient(LoopbackOptions.Client(host.LocalEndPoint!));
        using ISignalSubscription lane = client.Subscribe("jobs", "workers");
        client.Start();
        await lane.WaitAsync(until: null, cancellationToken);
        var waitBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var setBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();

        Task<SignalWaitResultEnum> pass = WaitThenBlockAsync(lane, waitBlocked, release, cancellationToken);
        Task<SignalSetResult> set = SetThenBlockAsync(client, setBlocked, release, cancellationToken);
        await waitBlocked.Task.WaitAsync(cancellationToken);
        await setBlocked.Task.WaitAsync(cancellationToken);

        SignalSetResult another = await client.SetAsync("jobs", cancellationToken);

        Assert.Equal(SignalSetStatusEnum.Applied, another.Status);
        Assert.Equal(SignalConnectionStateEnum.Connected, client.State);

        release.Set();

        Assert.Equal(SignalWaitResultEnum.Signaled, await pass);
        Assert.Equal(SignalSetStatusEnum.Applied, (await set).Status);
    }

    // The member's code after its await reports that it runs, then blocks until the test releases it.
    // ConfigureAwait(false) lets it continue wherever the library completes the wait, not on a context of the test
    // framework.
    private static async Task<SignalWaitResultEnum> WaitThenBlockAsync(
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

    // The same for the code after a set.
    private static async Task<SignalSetResult> SetThenBlockAsync(
        SignalClient client,
        TaskCompletionSource blocked,
        ManualResetEventSlim release,
        CancellationToken cancellationToken)
    {
        SignalSetResult result = await client.SetAsync("jobs", cancellationToken).ConfigureAwait(false);

        blocked.SetResult();
        release.Wait(cancellationToken);

        return result;
    }
}
