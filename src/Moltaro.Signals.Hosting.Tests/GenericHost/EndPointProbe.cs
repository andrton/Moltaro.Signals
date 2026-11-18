using System.Net;
using Microsoft.Extensions.Hosting;

namespace Moltaro.Signals.Hosting.Tests.GenericHost;

// A hosted service of the application that reads the host's LocalEndPoint when it starts, as a worker would.
internal sealed class EndPointProbe(SignalHost host) : IHostedService
{
    public IPEndPoint? LocalEndPointAtStart { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        LocalEndPointAtStart = host.LocalEndPoint;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
