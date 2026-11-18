using System.Net;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.Names;

// A key and a group follow one rule. It is stated in full through the key of the host's Subscribe; the other tests
// show that every name parameter of the host and of the client applies it, with its own ParamName.
public sealed class NameRuleTests
{
    public static TheoryData<string> BrokenNames =>
    [
        "",
        new string('a', 129),
        "a..b",
        ".a",
        "a.",
        "a b",
        "a/b",
        "a*",
        "caf\u00E9",
    ];

    public static TheoryData<string> GoodNames =>
    [
        "a",
        new string('a', 128),
        "Aa0_:-.Zz9",
        "entity.contacts.42",
    ];

    [Theory]
    [MemberData(nameof(BrokenNames))]
    public async Task A_name_that_breaks_the_rule_is_refused(string name)
    {
        await using var host = new SignalHost(LoopbackOptions.Host());

        Assert.Throws<ArgumentException>("key", () => host.Subscribe(name, "workers"));
    }

    [Theory]
    [MemberData(nameof(GoodNames))]
    public async Task A_name_that_keeps_the_rule_is_accepted_as_a_key_and_as_a_group(string name)
    {
        await using var host = new SignalHost(LoopbackOptions.Host());

        host.Subscribe(name, name).Dispose();
    }

    [Fact]
    public async Task The_host_refuses_a_null_key_or_group()
    {
        await using var host = new SignalHost(LoopbackOptions.Host());

        Assert.Throws<ArgumentNullException>("key", () => host.Subscribe(null!, "workers"));
        Assert.Throws<ArgumentNullException>("group", () => host.Subscribe("jobs", null!));
        Assert.Throws<ArgumentNullException>("key", () => host.SetAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_host_refuses_a_key_or_group_with_an_empty_segment()
    {
        await using var host = new SignalHost(LoopbackOptions.Host());

        Assert.Throws<ArgumentException>("key", () => host.Subscribe("a..b", "workers"));
        Assert.Throws<ArgumentException>("group", () => host.Subscribe("jobs", "a..b"));
        Assert.Throws<ArgumentException>("key", () => host.SetAsync("a..b", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_client_refuses_a_null_key_or_group()
    {
        await using var client = new SignalClient(LoopbackOptions.Client(new IPEndPoint(IPAddress.Loopback, 5000)));

        Assert.Throws<ArgumentNullException>("key", () => client.Subscribe(null!, "workers"));
        Assert.Throws<ArgumentNullException>("group", () => client.Subscribe("jobs", null!));
        Assert.Throws<ArgumentNullException>("key", () => client.SetAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_client_refuses_a_key_or_group_with_an_empty_segment()
    {
        await using var client = new SignalClient(LoopbackOptions.Client(new IPEndPoint(IPAddress.Loopback, 5000)));

        Assert.Throws<ArgumentException>("key", () => client.Subscribe("a..b", "workers"));
        Assert.Throws<ArgumentException>("group", () => client.Subscribe("jobs", "a..b"));
        Assert.Throws<ArgumentException>("key", () => client.SetAsync("a..b", TestContext.Current.CancellationToken));
    }
}
