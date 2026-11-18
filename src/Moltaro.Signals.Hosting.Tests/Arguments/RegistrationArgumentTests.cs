using Microsoft.Extensions.DependencyInjection;

namespace Moltaro.Signals.Hosting.Tests.Arguments;

public sealed class RegistrationArgumentTests
{
    [Fact]
    public void AddSignalHost_refuses_null_services_and_a_null_configure()
    {
        Assert.Throws<ArgumentNullException>(
            "services",
            () => SignalServiceCollectionExtensions.AddSignalHost(null!, _ => { }));
        Assert.Throws<ArgumentNullException>("configure", () => new ServiceCollection().AddSignalHost(null!));
    }

    [Fact]
    public void AddSignalClient_refuses_null_services_and_a_null_configure()
    {
        Assert.Throws<ArgumentNullException>(
            "services",
            () => SignalServiceCollectionExtensions.AddSignalClient(null!, _ => { }));
        Assert.Throws<ArgumentNullException>("configure", () => new ServiceCollection().AddSignalClient(null!));
    }
}
