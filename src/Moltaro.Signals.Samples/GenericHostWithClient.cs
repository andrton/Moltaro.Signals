using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moltaro.Signals.Hosting;

namespace Moltaro.Signals.Samples;

// A worker process registers a client of a host elsewhere in its generic host. The host runs in this process only so
// that the sample runs on its own; in a deployment it lives in the process that owns the background work, and the
// store is the database both processes reach.
internal static class GenericHostWithClient
{
    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("The host process starts its host on a free port of the loopback address.");
        await using var signalHost = new SignalHost(new SignalHostOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            Token = "sample-token",
        });
        signalHost.Start();
        var store = new JobStore();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // Keeps the output of the sample to its own lines and those of the client.
        builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);

        Console.WriteLine("The worker process registers a client of that host, then its worker.");
        builder.Services.AddSignalClient(options =>
        {
            // The sample runs the host in this process; a real client names the host's address from its configuration.
            options.HostEndPoint = signalHost.LocalEndPoint;
            options.Token = "sample-token";
        });
        builder.Services.AddSingleton(store);
        builder.Services.AddHostedService<JobWorker>();

        using IHost app = builder.Build();
        await app.StartAsync(cancellationToken);

        // The host process, after its transaction has committed. Whether the worker's lane is open on the host before
        // this set or not, the worker processes the job: its first pass after joining finds it.
        store.Add("job 1");
        SignalSetResult result = await signalHost.SetAsync("jobs", cancellationToken);
        Console.WriteLine($"The host process stored job 1 and set \"jobs\": {result.Status}, {result.Groups} group(s).");

        await app.WaitForShutdownAsync(cancellationToken);
    }
}
