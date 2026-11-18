using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.InProcess;

// The rule "Completions" of the contract, through the in-process ISignals of a host: a set that wakes a waiting member
// does not run that member's code; the member continues on the thread pool, and the set returns without waiting for
// it. Were the member's code run inside the set, the set would not return, because that code is blocked until after
// the set has returned.
public sealed class CompletionTests
{
    [Fact]
    public async Task A_set_that_wakes_a_member_returns_while_the_code_after_its_wait_is_still_blocked()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null, cancellationToken);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        Task<SignalWaitResultEnum> pass = PassThatBlocksAsync(lane, blocked, release, cancellationToken);

        await host.SetAsync("jobs", cancellationToken);
        await blocked.Task.WaitAsync(cancellationToken);
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
