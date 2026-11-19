namespace Moltaro.Signals.Hosting;

// The options of the client of one container, as the configure of AddSignalClient filled them when the client was
// first resolved. The client is created from them, and its hosted service takes the HostEndPoint of the log lines from
// them, since the client does not expose it.
internal sealed record ConfiguredClientOptions(SignalClientOptions Options);
