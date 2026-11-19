using System.Threading.Channels;

namespace Moltaro.Signals.Tests.ClientOverTcp;

// The notifications of a client, kept in the order its handler receives them, so that a test can await them one at a
// time. Attached before the client is started, it sees every notification.
internal sealed class StateChanges
{
    private readonly Channel<SignalConnectionChangedEventArgs> _changes =
        Channel.CreateUnbounded<SignalConnectionChangedEventArgs>();

    public StateChanges(SignalClient client)
    {
        client.StateChanged += (_, change) => _changes.Writer.TryWrite(change);
    }

    public async Task<SignalConnectionChangedEventArgs> NextAsync(CancellationToken cancellationToken)
    {
        return await _changes.Reader.ReadAsync(cancellationToken);
    }
}
