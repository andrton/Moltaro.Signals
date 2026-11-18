namespace Moltaro.Signals.Measurements;

// Runs a publisher on a thread of its own instead of the thread pool, where the woken members continue. A publisher
// never yields, because an in-process set completes at once; on the pool it would hold a thread that a member needs,
// and the figures would measure the pool instead of the host.
internal static class OwnThread
{
    public static Task RunAsync(Func<Task> publisher)
    {
        Task<Task> started = Task.Factory.StartNew(
            publisher,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        return started.Unwrap();
    }
}
