using Microsoft.Extensions.DependencyInjection;

namespace Moltaro.Signals.Hosting;

/// <summary>
/// Registers the <see cref="ISignals"/> of a process in a service container and a .NET generic host: the
/// <see cref="SignalHost"/> of the process, or a <see cref="SignalClient"/> of a host elsewhere.
/// </summary>
/// <remarks>
/// <para>
/// A container serves one <see cref="ISignals"/>. Registering both, or one of them twice, in one container is not
/// supported: <see cref="ISignals"/> then resolves to the one registered last. <see cref="SignalHost"/> and
/// <see cref="SignalClient"/> are only <see cref="IAsyncDisposable"/>, so a container that resolved one has to be
/// disposed asynchronously, as the generic host does.
/// </para>
/// <para>
/// The hosted services write log lines through the container's
/// <see cref="Microsoft.Extensions.Logging.ILoggerFactory"/>, with the category of the type that reports. The named
/// values are structured properties; the message text is not part of the contract. The <c>HostEndPoint</c> of the
/// client's lines is the one its options name, as configured: a name is not resolved for them.
/// </para>
/// <list type="table">
/// <listheader>
/// <term>Category and level</term>
/// <description>When, and its values</description>
/// </listheader>
/// <item>
/// <term><c>Moltaro.Signals.SignalHost</c>, Information</term>
/// <description>The host listens; <c>LocalEndPoint</c>.</description>
/// </item>
/// <item>
/// <term><c>Moltaro.Signals.SignalClient</c>, Information</term>
/// <description>Connected to the host; <c>HostEndPoint</c>.</description>
/// </item>
/// <item>
/// <term><c>Moltaro.Signals.SignalClient</c>, Warning</term>
/// <description>
/// The connection to the host ended, or the host refused an attempt; then
/// <see cref="SignalConnectionChangedEventArgs.Reason"/> begins with its code. <c>HostEndPoint</c>, <c>Reason</c>.
/// </description>
/// </item>
/// </list>
/// </remarks>
public static class SignalServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="SignalHost"/> as a singleton, <see cref="ISignals"/> as that same host, and a hosted
    /// service that starts it.
    /// </summary>
    /// <param name="services">The container to register in.</param>
    /// <param name="configure">Fills the options of the host when the host is first resolved.</param>
    /// <returns><paramref name="services"/>.</returns>
    /// <remarks>
    /// <para>
    /// The <see cref="Microsoft.Extensions.Hosting.IHostedService.StartAsync"/> of the hosted service resolves the
    /// host, calls <see cref="SignalHost.Start"/> and writes the host's log line. Registered before the other hosted
    /// services of the process, the port is therefore bound before they start; a port that cannot be bound fails the
    /// start of the application. The order matters only for when the port is bound: the in-process
    /// <see cref="ISignals"/> works from the host's construction.
    /// </para>
    /// <para>
    /// The hosted service does nothing when the application stops: the container disposes the host after every
    /// hosted service has stopped, so the workers of the process keep their host until they are done. Wrong options
    /// throw when the host is resolved, as its constructor says.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown by the call: <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddSignalHost(this IServiceCollection services, Action<SignalHostOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddSingleton(_ =>
        {
            var options = new SignalHostOptions();
            configure(options);

            return new SignalHost(options);
        });

        // The same object. Once ISignals has been resolved, the container disposes the host for each of the two
        // registrations; the second DisposeAsync completes with the first.
        services.AddSingleton<ISignals>(provider => provider.GetRequiredService<SignalHost>());
        services.AddHostedService(provider => new SignalHostStarter(provider));

        return services;
    }

    /// <summary>
    /// Registers a <see cref="SignalClient"/> as a singleton, <see cref="ISignals"/> as that same client, and a
    /// hosted service that starts it.
    /// </summary>
    /// <param name="services">The container to register in.</param>
    /// <param name="configure">Fills the options of the client when the client is first resolved.</param>
    /// <returns><paramref name="services"/>.</returns>
    /// <remarks>
    /// The <see cref="Microsoft.Extensions.Hosting.IHostedService.StartAsync"/> of the hosted service resolves the
    /// client, attaches its log lines and calls <see cref="SignalClient.Start"/>. The hosted service does nothing when
    /// the application stops: the container disposes the client. Wrong options throw when the client is resolved, as
    /// its constructor says.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown by the call: <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddSignalClient(this IServiceCollection services, Action<SignalClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddSingleton(_ =>
        {
            var options = new SignalClientOptions();
            configure(options);

            return new ConfiguredClientOptions(options);
        });
        services.AddSingleton(provider => new SignalClient(provider.GetRequiredService<ConfiguredClientOptions>().Options));

        // The same object, disposed by the container as the host of AddSignalHost is.
        services.AddSingleton<ISignals>(provider => provider.GetRequiredService<SignalClient>());
        services.AddHostedService(provider => new SignalClientStarter(provider));

        return services;
    }
}
