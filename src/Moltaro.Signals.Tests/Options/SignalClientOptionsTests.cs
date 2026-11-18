using System.Net;
using System.Net.Sockets;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.Options;

// The token rule and the range of a TimeSpan are stated in full by the tests of the host's options; these show that
// the client applies them to each of its options, and state the rules only the client has.
public sealed class SignalClientOptionsTests
{
    private static IPEndPoint HostEndPoint => new(IPAddress.Loopback, 5000);

    [Fact]
    public async Task Options_with_only_the_required_properties_set_are_accepted()
    {
        var options = new SignalClientOptions
        {
            HostEndPoint = HostEndPoint,
            Token = "test-token",
        };

        await using var client = new SignalClient(options);
    }

    [Fact]
    public void Null_options_are_refused()
    {
        Assert.Throws<ArgumentNullException>("options", () => new SignalClient(null!));
    }

    [Fact]
    public void The_host_end_point_is_required()
    {
        SignalClientOptions options = LoopbackOptions.Client(HostEndPoint);
        options.HostEndPoint = null;

        Assert.Throws<ArgumentException>(nameof(SignalClientOptions.HostEndPoint), () => new SignalClient(options));
    }

    [Fact]
    public void A_host_end_point_that_is_neither_an_IPEndPoint_nor_a_DnsEndPoint_is_refused()
    {
        SignalClientOptions options = LoopbackOptions.Client(new UnixDomainSocketEndPoint("signals.sock"));

        Assert.Throws<ArgumentException>(nameof(SignalClientOptions.HostEndPoint), () => new SignalClient(options));
    }

    [Fact]
    public async Task A_host_end_point_given_by_name_is_accepted()
    {
        SignalClientOptions options = LoopbackOptions.Client(new DnsEndPoint("localhost", 5000));

        await using var client = new SignalClient(options);
    }

    [Fact]
    public void The_token_is_required()
    {
        SignalClientOptions options = LoopbackOptions.Client(HostEndPoint);
        options.Token = null;

        Assert.Throws<ArgumentException>(nameof(SignalClientOptions.Token), () => new SignalClient(options));
    }

    [Fact]
    public void A_token_that_breaks_the_rule_is_refused()
    {
        SignalClientOptions options = LoopbackOptions.Client(HostEndPoint);
        options.Token = "two words";

        Assert.Throws<ArgumentException>(nameof(SignalClientOptions.Token), () => new SignalClient(options));
    }

    // Each value breaks the range and nothing else: a HeartbeatInterval or a ReconnectDelay above the range would also
    // contradict DeadPeerTimeout or MaxReconnectDelay, and a DeadPeerTimeout or a MaxReconnectDelay of zero would
    // contradict HeartbeatInterval or ReconnectDelay.
    [Fact]
    public void Each_TimeSpan_option_outside_its_range_is_refused_with_its_name()
    {
        SignalClientOptions heartbeat = LoopbackOptions.Client(HostEndPoint);
        heartbeat.HeartbeatInterval = TimeSpan.Zero;

        SignalClientOptions deadPeer = LoopbackOptions.Client(HostEndPoint);
        deadPeer.DeadPeerTimeout = TimeSpan.MaxValue;

        SignalClientOptions reconnect = LoopbackOptions.Client(HostEndPoint);
        reconnect.ReconnectDelay = TimeSpan.Zero;

        SignalClientOptions maxReconnect = LoopbackOptions.Client(HostEndPoint);
        maxReconnect.MaxReconnectDelay = TimeSpan.MaxValue;

        Assert.Throws<ArgumentOutOfRangeException>(
            nameof(SignalClientOptions.HeartbeatInterval),
            () => new SignalClient(heartbeat));
        Assert.Throws<ArgumentOutOfRangeException>(
            nameof(SignalClientOptions.DeadPeerTimeout),
            () => new SignalClient(deadPeer));
        Assert.Throws<ArgumentOutOfRangeException>(
            nameof(SignalClientOptions.ReconnectDelay),
            () => new SignalClient(reconnect));
        Assert.Throws<ArgumentOutOfRangeException>(
            nameof(SignalClientOptions.MaxReconnectDelay),
            () => new SignalClient(maxReconnect));
    }

    [Fact]
    public async Task DeadPeerTimeout_has_to_be_greater_than_HeartbeatInterval()
    {
        SignalClientOptions equal = LoopbackOptions.Client(HostEndPoint);
        equal.HeartbeatInterval = TimeSpan.FromSeconds(2);
        equal.DeadPeerTimeout = TimeSpan.FromSeconds(2);

        SignalClientOptions greater = LoopbackOptions.Client(HostEndPoint);
        greater.HeartbeatInterval = TimeSpan.FromSeconds(2);
        greater.DeadPeerTimeout = TimeSpan.FromSeconds(2) + TimeSpan.FromTicks(1);

        Assert.Throws<ArgumentException>(nameof(SignalClientOptions.DeadPeerTimeout), () => new SignalClient(equal));

        await using var client = new SignalClient(greater);
    }

    [Fact]
    public async Task MaxReconnectDelay_has_to_be_at_least_ReconnectDelay()
    {
        SignalClientOptions less = LoopbackOptions.Client(HostEndPoint);
        less.ReconnectDelay = TimeSpan.FromSeconds(1);
        less.MaxReconnectDelay = TimeSpan.FromSeconds(1) - TimeSpan.FromTicks(1);

        SignalClientOptions equal = LoopbackOptions.Client(HostEndPoint);
        equal.ReconnectDelay = TimeSpan.FromSeconds(1);
        equal.MaxReconnectDelay = TimeSpan.FromSeconds(1);

        Assert.Throws<ArgumentException>(nameof(SignalClientOptions.MaxReconnectDelay), () => new SignalClient(less));

        await using var client = new SignalClient(equal);
    }

    [Fact]
    public void The_time_provider_is_required()
    {
        SignalClientOptions options = LoopbackOptions.Client(HostEndPoint);
        options.TimeProvider = null!;

        Assert.Throws<ArgumentException>(nameof(SignalClientOptions.TimeProvider), () => new SignalClient(options));
    }
}
