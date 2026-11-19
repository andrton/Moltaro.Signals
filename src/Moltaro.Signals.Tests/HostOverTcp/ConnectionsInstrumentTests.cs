using System.Diagnostics.Metrics;
using Moltaro.Signals.Tests.RawProtocol;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.HostOverTcp;

// The instrument connections of the host's meter, as the Metrics table of the contract says: client connections whose
// HELLO was accepted and that are open, until their socket is closed. The listener takes only the instrument of the
// meter whose scope is the host of the test, so the hosts of other tests count nothing here.
public sealed class ConnectionsInstrumentTests : IDisposable
{
    private const string Connections = "moltaro.signals.connections";

    private readonly MeterListener _listener = new();

    // Released once for every measurement, so that a test can wait for the total it expects without missing one.
    private readonly SemaphoreSlim _measured = new(0);

    private Instrument? _instrument;

    private long _total;

    public void Dispose()
    {
        _listener.Dispose();
        _measured.Dispose();
    }

    // A connection that has not sent its HELLO is not counted: were it counted, the total would not come back to 0
    // while it stays open.
    [Fact]
    public async Task Connections_counts_a_connection_from_its_accepted_HELLO_until_its_socket_is_closed()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SignalHost host = LoopbackHost.Start(new FakeClock());
        Listen(host);
        using RawConnection withoutHello = await RawConnection.ConnectAsync(host, cancellationToken);
        RawConnection counted = await RawConnection.HelloAsync(host, cancellationToken);

        await TotalReachesAsync(1, cancellationToken);
        counted.Dispose();
        await TotalReachesAsync(0, cancellationToken);

        UpDownCounter<long> instrument = Assert.IsType<UpDownCounter<long>>(_instrument);
        Assert.Equal("{connection}", instrument.Unit);
    }

    // Started after the host exists: the listener then learns of the instruments the host has published.
    private void Listen(SignalHost host)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == SignalHost.MeterName
                && ReferenceEquals(instrument.Meter.Scope, host)
                && instrument.Name == Connections)
            {
                _instrument = instrument;
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            Interlocked.Add(ref _total, measurement);
            _measured.Release();
        });

        _listener.Start();
    }

    private async Task TotalReachesAsync(long expected, CancellationToken cancellationToken)
    {
        while (Interlocked.Read(ref _total) != expected)
        {
            await _measured.WaitAsync(cancellationToken);
        }
    }
}
