using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moltaro.Signals.Hosting;

namespace Moltaro.Signals.Samples;

// The application that owns the background work registers the host in its generic host, before its worker, so that
// the port is bound before the worker starts. The worker and the publisher of this process use the same ISignals.
internal static class GenericHostWithHost
{
    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // Keeps the output of the sample to its own lines and those of the host.
        builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);

        Console.WriteLine("The application registers the host first, then its worker.");
        builder.Services.AddSignalHost(options =>
        {
            options.ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0);
            options.Token = "sample-token";
        });
        builder.Services.AddSingleton<JobStore>();
        builder.Services.AddHostedService<JobWorker>();

        using IHost app = builder.Build();
        await app.StartAsync(cancellationToken);

        // Code of this process, such as an API endpoint, after its transaction has committed. Whether the worker has
        // joined before this set or not, it processes the job: a set that nobody listens to is not remembered, and
        // the first pass after joining finds the job.
        app.Services.GetRequiredService<JobStore>().Add("job 1");
        SignalSetResult result = await app.Services.GetRequiredService<ISignals>().SetAsync("jobs", cancellationToken);
        Console.WriteLine($"The publisher stored job 1 and set \"jobs\": {result.Status}, {result.Groups} group(s).");

        await app.WaitForShutdownAsync(cancellationToken);
    }
}
