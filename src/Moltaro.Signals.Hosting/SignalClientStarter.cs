using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Moltaro.Signals.Hosting;

// The hosted service of AddSignalClient. It attaches the client's log lines to its notifications, then starts the
// client of its container when the application starts. It does nothing when the application stops: the container
// disposes the client, and a dispose raises no notification.
internal sealed class SignalClientStarter(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        SignalClient client = services.GetRequiredService<SignalClient>();
        EndPoint? hostEndPoint = services.GetRequiredService<ConfiguredClientOptions>().Options.HostEndPoint;
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<SignalClient>();

        // Attached before Start, so that no change of the connection goes without its line. The handler runs on the
        // thread pool, one notification at a time.
        client.StateChanged += (_, change) =>
        {
            if (change.State == SignalConnectionStateEnum.Connected)
            {
                logger.LogInformation("Connected to the host {HostEndPoint}.", hostEndPoint);
            }
            else
            {
                logger.LogWarning("Disconnected from the host {HostEndPoint}: {Reason}", hostEndPoint, change.Reason);
            }
        };

        client.Start();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
