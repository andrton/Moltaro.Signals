using System.Net;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.Options;

// The rules of the options, the token and the range of a TimeSpan are stated here in full. The client applies the
// same token and TimeSpan checks; its tests show that it applies them to each of its options.
public sealed class SignalHostOptionsTests
{
    // TimeSpan.FromMilliseconds(uint.MaxValue - 1), the longest timer of TimeProvider, as the contract states it.
    private static readonly TimeSpan LongestTimer = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    public static TheoryData<string> BrokenTokens =>
    [
        "",
        new string('t', 257),
        "two words",
        "delete\u007F",
    ];

    public static TheoryData<string> GoodTokens =>
    [
        "!",
        "~",
        new string('t', 256),
    ];

    public static TheoryData<TimeSpan> DeadPeerTimeoutsOutOfRange =>
    [
        TimeSpan.Zero,
        -TimeSpan.FromTicks(1),
        LongestTimer + TimeSpan.FromTicks(1),
    ];

    public static TheoryData<TimeSpan> DeadPeerTimeoutsInRange =>
    [
        TimeSpan.FromTicks(1),
        LongestTimer,
    ];

    [Fact]
    public async Task Options_with_only_the_required_properties_set_are_accepted()
    {
        var options = new SignalHostOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            Token = "test-token",
        };

        await using var host = new SignalHost(options);
    }

    [Fact]
    public void Null_options_are_refused()
    {
        Assert.Throws<ArgumentNullException>("options", () => new SignalHost(null!));
    }

    [Fact]
    public void The_listen_end_point_is_required()
    {
        SignalHostOptions options = LoopbackOptions.Host();
        options.ListenEndPoint = null;

        Assert.Throws<ArgumentException>(nameof(SignalHostOptions.ListenEndPoint), () => new SignalHost(options));
    }

    [Fact]
    public void The_token_is_required()
    {
        SignalHostOptions options = LoopbackOptions.Host();
        options.Token = null;

        Assert.Throws<ArgumentException>(nameof(SignalHostOptions.Token), () => new SignalHost(options));
    }

    [Theory]
    [MemberData(nameof(BrokenTokens))]
    public void A_token_that_is_not_1_to_256_characters_from_U0021_to_U007E_is_refused(string token)
    {
        SignalHostOptions options = LoopbackOptions.Host();
        options.Token = token;

        Assert.Throws<ArgumentException>(nameof(SignalHostOptions.Token), () => new SignalHost(options));
    }

    [Theory]
    [MemberData(nameof(GoodTokens))]
    public async Task A_token_of_1_to_256_characters_from_U0021_to_U007E_is_accepted(string token)
    {
        SignalHostOptions options = LoopbackOptions.Host();
        options.Token = token;

        await using var host = new SignalHost(options);
    }

    [Fact]
    public void MaxConnections_below_1_is_refused()
    {
        SignalHostOptions options = LoopbackOptions.Host();
        options.MaxConnections = 0;

        Assert.Throws<ArgumentOutOfRangeException>(nameof(SignalHostOptions.MaxConnections), () => new SignalHost(options));
    }

    [Fact]
    public void MaxSubscriptionsPerConnection_below_1_is_refused()
    {
        SignalHostOptions options = LoopbackOptions.Host();
        options.MaxSubscriptionsPerConnection = 0;

        Assert.Throws<ArgumentOutOfRangeException>(
            nameof(SignalHostOptions.MaxSubscriptionsPerConnection),
            () => new SignalHost(options));
    }

    [Fact]
    public async Task Limits_of_1_are_accepted()
    {
        SignalHostOptions options = LoopbackOptions.Host();
        options.MaxConnections = 1;
        options.MaxSubscriptionsPerConnection = 1;

        await using var host = new SignalHost(options);
    }

    [Theory]
    [MemberData(nameof(DeadPeerTimeoutsOutOfRange))]
    public void A_DeadPeerTimeout_not_greater_than_zero_or_longer_than_the_longest_timer_is_refused(TimeSpan timeout)
    {
        SignalHostOptions options = LoopbackOptions.Host();
        options.DeadPeerTimeout = timeout;

        Assert.Throws<ArgumentOutOfRangeException>(nameof(SignalHostOptions.DeadPeerTimeout), () => new SignalHost(options));
    }

    [Theory]
    [MemberData(nameof(DeadPeerTimeoutsInRange))]
    public async Task A_DeadPeerTimeout_greater_than_zero_up_to_the_longest_timer_is_accepted(TimeSpan timeout)
    {
        SignalHostOptions options = LoopbackOptions.Host();
        options.DeadPeerTimeout = timeout;

        await using var host = new SignalHost(options);
    }

    [Fact]
    public void The_time_provider_is_required()
    {
        SignalHostOptions options = LoopbackOptions.Host();
        options.TimeProvider = null!;

        Assert.Throws<ArgumentException>(nameof(SignalHostOptions.TimeProvider), () => new SignalHost(options));
    }
}
