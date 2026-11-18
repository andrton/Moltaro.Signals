using System.Net;

namespace Moltaro.Signals;

/// <summary>
/// The options of a <see cref="SignalClient"/>.
/// </summary>
/// <remarks>
/// The constructor of <see cref="SignalClient"/> validates the options and copies them: a later change of this
/// object has no effect on the client. A number or a <see cref="TimeSpan"/> outside its range gives
/// <see cref="ArgumentOutOfRangeException"/>; any other wrong value, such as a required property left
/// <see langword="null"/>, a token that breaks its rule or two options that contradict each other, gives
/// <see cref="ArgumentException"/>. Its <see cref="ArgumentException.ParamName"/> is the name of the property; for
/// two options that contradict each other, <see cref="DeadPeerTimeout"/> or <see cref="MaxReconnectDelay"/>. A
/// <see cref="TimeSpan"/> option has to be greater than zero and at most
/// <c>TimeSpan.FromMilliseconds(uint.MaxValue - 1)</c>, the longest timer of <see cref="System.TimeProvider"/>.
/// </remarks>
public sealed class SignalClientOptions
{
    /// <summary>
    /// The host to connect to, as an <see cref="IPEndPoint"/> or a <see cref="DnsEndPoint"/>. Required.
    /// </summary>
    /// <remarks>
    /// A name is resolved again for every attempt to connect.
    /// </remarks>
    public EndPoint? HostEndPoint { get; set; }

    /// <summary>
    /// The shared token the client sends in its <c>HELLO</c>. Required.
    /// </summary>
    /// <remarks>
    /// 1 to 256 characters, each from U+0021 to U+007E: printable ASCII without the space. The host compares it
    /// exactly with its own and refuses another one with the code <c>token</c>.
    /// </remarks>
    public string? Token { get; set; }

    /// <summary>
    /// The client sends <c>PING</c> when it has sent nothing, or received nothing, for this long, and at most one
    /// <c>PING</c> in this time. 1 second by default.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// With nothing received for this long, the connection is declared dead and closed. 3 seconds by default; it
    /// has to be greater than <see cref="HeartbeatInterval"/>.
    /// </summary>
    /// <remarks>
    /// It also bounds one attempt to connect: the TCP connect and the <c>HELLO</c> together. A set that waits for
    /// the host's answer ends at the latest when the connection is declared dead.
    /// </remarks>
    public TimeSpan DeadPeerTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The delay before the first attempt to connect after a lost connection; it doubles after each failed
    /// attempt. 0.2 seconds by default.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// The longest delay between attempts to connect. 2 seconds by default; it has to be at least
    /// <see cref="ReconnectDelay"/>.
    /// </summary>
    /// <remarks>
    /// A connection the host accepts sets the delay back to <see cref="ReconnectDelay"/>.
    /// </remarks>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The clock of deadlines, heartbeats, dead-peer detection and backoff. Not <see langword="null"/>;
    /// <see cref="System.TimeProvider.System"/> by default.
    /// </summary>
    /// <remarks>
    /// Tests use a fake one.
    /// </remarks>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
