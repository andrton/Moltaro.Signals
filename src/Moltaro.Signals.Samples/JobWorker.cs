using Microsoft.Extensions.Hosting;

namespace Moltaro.Signals.Samples;

// A worker with one lane. Its code is the same whether ISignals is the host of its own process or a client of a host
// elsewhere. So that the samples end, it stops the application once it has processed a job.
internal sealed class JobWorker(ISignals signals, JobStore store, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using ISignalSubscription lane = signals.Subscribe("jobs", "workers");

        while (!stoppingToken.IsCancellationRequested)
        {
            SignalWaitResultEnum result = await lane.WaitAsync(until: null, stoppingToken);

            // Every result is followed by one full pass over the storage.
            while (store.TryClaim(out string? job))
            {
                Console.WriteLine($"The worker ({result}) processes {job}.");
                // Only so that the sample ends; a real worker keeps its loop running.
                lifetime.StopApplication();
            }
        }
    }
}
