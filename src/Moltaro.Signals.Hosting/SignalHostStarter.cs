using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Moltaro.Signals.Hosting;

// The hosted service of AddSignalHost. It starts the host of its container when the application starts, so that the
// port is bound before the hosted services registered after it start, and writes the host's log line. It does nothing
// when the application stops: the container disposes the host after every hosted service has stopped.
internal sealed class SignalHostStarter(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        SignalHost host = services.GetRequiredService<SignalHost>();
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<SignalHost>();

        // A port that cannot be bound throws here, and the exception fails the start of the application.
        host.Start();

        logger.LogInformation("The host listens on {LocalEndPoint}.", host.LocalEndPoint);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
