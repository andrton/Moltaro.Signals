using System.Net;

namespace Moltaro.Signals;

/// <summary>
/// The options of a <see cref="SignalHost"/>.
/// </summary>
/// <remarks>
/// The constructor of <see cref="SignalHost"/> validates the options and copies them: a later change of this object
/// has no effect on the host. A number or a <see cref="TimeSpan"/> outside its range gives
/// <see cref="ArgumentOutOfRangeException"/>; any other wrong value, such as a required property left
/// <see langword="null"/> or a token that breaks its rule, gives <see cref="ArgumentException"/>. Its
/// <see cref="ArgumentException.ParamName"/> is the name of the property. A <see cref="TimeSpan"/> option has to be
/// greater than zero and at most <c>TimeSpan.FromMilliseconds(uint.MaxValue - 1)</c>, the longest timer of
/// <see cref="System.TimeProvider"/>.
/// </remarks>
public sealed class SignalHostOptions
{
    /// <summary>
    /// The address and port the host listens on. Required.
    /// </summary>
    /// <remarks>
    /// Port 0 lets the system choose a free port; <see cref="SignalHost.LocalEndPoint"/> then reports it.
    /// </remarks>
    public IPEndPoint? ListenEndPoint { get; set; }

    /// <summary>
    /// The shared token every client sends in its <c>HELLO</c>. Required.
    /// </summary>
    /// <remarks>
    /// 1 to 256 characters, each from U+0021 to U+007E: printable ASCII without the space. The host compares it
    /// exactly with the token of a client and refuses another one with the code <c>token</c>.
    /// </remarks>
    public string? Token { get; set; }

    /// <summary>
    /// The most client connections open at the same time, counted from their accept until their socket is closed.
    /// At least 1; 1000 by default.
    /// </summary>
    /// <remarks>
    /// One more connection is answered with the code <c>connections</c> and closed.
    /// </remarks>
    public int MaxConnections { get; set; } = 1000;

    /// <summary>
    /// The most open subscriptions of one client connection. At least 1; 1000 by default.
    /// </summary>
    /// <remarks>
    /// One more is refused with the code <c>subscriptions</c>, the connection stays open, and the
    /// <see cref="ISignalSubscription.WaitAsync"/> of that subscription reports
    /// <see cref="InvalidOperationException"/>. In-process subscriptions have no limit.
    /// </remarks>
    public int MaxSubscriptionsPerConnection { get; set; } = 1000;

    /// <summary>
    /// How long a client connection may stay without traffic from the client before the host closes it. 3 seconds
    /// by default.
    /// </summary>
    /// <remarks>
    /// A connection that has not sent its <c>HELLO</c> within this time is refused with the code <c>protocol</c>.
    /// It has to be longer than the <see cref="SignalClientOptions.HeartbeatInterval"/> of the clients; the host
    /// cannot check this.
    /// </remarks>
    public TimeSpan DeadPeerTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The clock of in-process deadlines and of dead-peer detection. Not <see langword="null"/>;
    /// <see cref="System.TimeProvider.System"/> by default.
    /// </summary>
    /// <remarks>
    /// Tests use a fake one.
    /// </remarks>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
