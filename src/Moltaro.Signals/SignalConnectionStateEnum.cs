namespace Moltaro.Signals;

/// <summary>
/// Whether an <see cref="ISignals"/> can reach its host, as <see cref="ISignals.State"/> describes.
/// </summary>
/// <remarks>
/// While a client is <see cref="Disconnected"/>, its sets return <see cref="SignalSetStatusEnum.Unavailable"/> and
/// its waits wait.
/// </remarks>
public enum SignalConnectionStateEnum
{
    /// <summary>
    /// A client has no connection that the host accepted, for example while it tries to connect; the in-process
    /// <see cref="ISignals"/> of a host has been disposed.
    /// </summary>
    Disconnected = 0,

    /// <summary>
    /// The host accepted the <c>HELLO</c> of a client and the connection has not ended; the in-process
    /// <see cref="ISignals"/> of a host has not been disposed.
    /// </summary>
    Connected = 1,
}
