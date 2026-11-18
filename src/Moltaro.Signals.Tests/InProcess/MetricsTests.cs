using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.InProcess;

// The instruments of the host's meter, as the Metrics table of the contract says, through the in-process ISignals of
// a host. The listener takes only the instruments of the meter whose scope is the host of the test, so the hosts of
// other tests that run at the same time count nothing here. A member is busy after Subscribe, until its first wait.
public sealed class MetricsTests : IDisposable
{
    private const string Subscriptions = "moltaro.signals.subscriptions";
    private const string Sets = "moltaro.signals.sets";
    private const string Coalesced = "moltaro.signals.coalesced";
    private const string Deliveries = "moltaro.signals.deliveries";
    private const string HandedOn = "moltaro.signals.handed_on";

    private readonly MeterListener _listener = new();

    // Written by the listener on the threads that publish instruments and record measurements, read by the test.
    private readonly ConcurrentDictionary<string, Instrument> _instruments = new();
    private readonly ConcurrentDictionary<string, long> _totals = new();

    public void Dispose()
    {
        _listener.Dispose();
    }

    [Fact]
    public async Task The_host_publishes_its_instruments_with_their_kinds_and_units_on_a_meter_of_its_own()
    {
        await using var host = new SignalHost(LoopbackOptions.Host());

        Listen(host);

        Assert.IsType<UpDownCounter<long>>(Published(Subscriptions));
        Assert.IsType<Counter<long>>(Published(Sets));
        Assert.IsType<Counter<long>>(Published(Coalesced));
        Assert.IsType<Counter<long>>(Published(Deliveries));
        Assert.IsType<Counter<long>>(Published(HandedOn));
        Assert.Equal("{subscription}", Published(Subscriptions).Unit);
        Assert.Equal("{set}", Published(Sets).Unit);
        Assert.Equal("{signal}", Published(Coalesced).Unit);
        Assert.Equal("{signal}", Published(Deliveries).Unit);
        Assert.Equal("{signal}", Published(HandedOn).Unit);
    }

    [Fact]
    public async Task Subscriptions_counts_the_subscriptions_that_are_open()
    {
        await using var host = new SignalHost(LoopbackOptions.Host());
        Listen(host);
        ISignalSubscription closing = host.Subscribe("jobs", "workers");
        using ISignalSubscription staying = host.Subscribe("mail", "senders");
        long whileBothAreOpen = Total(Subscriptions);

        closing.Dispose();
        closing.Dispose();

        Assert.Equal(2, whileBothAreOpen);
        Assert.Equal(1, Total(Subscriptions));
    }

    // Both members are busy, so the first set leaves a signal pending on the key and on its ancestor, and the second
    // finds one pending on each. A set that nobody listens to is applied as well.
    [Fact]
    public async Task Sets_counts_each_applied_set_once_and_coalesced_each_key_and_group_that_found_a_signal_pending()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        Listen(host);
        using ISignalSubscription onKey = host.Subscribe("entity.contacts", "cache");
        using ISignalSubscription onAncestor = host.Subscribe("entity", "cache");

        await host.SetAsync("entity.contacts", cancellationToken);
        long coalescedAfterFirstSet = Total(Coalesced);
        await host.SetAsync("entity.contacts", cancellationToken);
        await host.SetAsync("files", cancellationToken);

        Assert.Equal(3, Total(Sets));
        Assert.Equal(0, coalescedAfterFirstSet);
        Assert.Equal(2, Total(Coalesced));
    }

    // A signal is delivered when a set wakes a waiting member, when a wait takes a pending signal, and when a busy
    // member that leaves hands its signal to a waiting member. Joined is no delivery.
    [Fact]
    public async Task Deliveries_counts_each_signal_handed_to_a_member_and_no_Joined()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        Listen(host);
        ISignalSubscription leaving = host.Subscribe("jobs", "workers");
        using ISignalSubscription staying = host.Subscribe("jobs", "workers");
        await leaving.WaitAsync(until: null, cancellationToken);
        await staying.WaitAsync(until: null, cancellationToken);
        long afterJoined = Total(Deliveries);

        ValueTask<SignalWaitResultEnum> wokenBySet = leaving.WaitAsync(until: null, cancellationToken);
        await host.SetAsync("jobs", cancellationToken);
        await wokenBySet;
        long afterWake = Total(Deliveries);

        await host.SetAsync("jobs", cancellationToken);
        await leaving.WaitAsync(until: null, cancellationToken);
        long afterPendingTaken = Total(Deliveries);

        ValueTask<SignalWaitResultEnum> handedOn = staying.WaitAsync(until: null, cancellationToken);
        leaving.Dispose();
        await handedOn;

        Assert.Equal(0, afterJoined);
        Assert.Equal(1, afterWake);
        Assert.Equal(2, afterPendingTaken);
        Assert.Equal(3, Total(Deliveries));
    }

    // One busy member hands its signal to the member that waits, another leaves it pending where one is pending
    // already, which counts as no coalesced set; neither counts as a set. An idle member, and the last member of its
    // group on a key, hand nothing on.
    [Fact]
    public async Task Handed_on_counts_each_signal_a_busy_member_hands_on_to_the_members_that_stay()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        Listen(host);
        using ISignalSubscription staying = host.Subscribe("jobs", "workers");
        ISignalSubscription toWaiting = host.Subscribe("jobs", "workers");
        ISignalSubscription toPending = host.Subscribe("jobs", "workers");
        ISignalSubscription idle = host.Subscribe("jobs", "workers");
        ISignalSubscription last = host.Subscribe("mail", "senders");
        await staying.WaitAsync(until: null, cancellationToken);
        await idle.WaitAsync(until: null, cancellationToken);
        using var stopWaiting = new CancellationTokenSource();
        ValueTask<SignalWaitResultEnum> idleWait = idle.WaitAsync(until: null, stopWaiting.Token);
        await stopWaiting.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await idleWait);
        ValueTask<SignalWaitResultEnum> stayingWait = staying.WaitAsync(until: null, cancellationToken);

        toWaiting.Dispose();
        await stayingWait;
        await host.SetAsync("jobs", cancellationToken);
        toPending.Dispose();
        idle.Dispose();
        last.Dispose();

        Assert.Equal(2, Total(HandedOn));
        Assert.Equal(0, Total(Coalesced));
        Assert.Equal(1, Total(Sets));
    }

    // Started after the host exists: the listener then learns of the instruments the host has published.
    private void Listen(SignalHost host)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == SignalHost.MeterName && ReferenceEquals(instrument.Meter.Scope, host))
            {
                _instruments[instrument.Name] = instrument;
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, state) => _totals.AddOrUpdate(
                instrument.Name,
                measurement,
                (name, total) => total + measurement));

        _listener.Start();
    }

    private Instrument Published(string name)
    {
        Assert.True(_instruments.TryGetValue(name, out Instrument? instrument), $"The host publishes no {name}.");

        return instrument;
    }

    private long Total(string name)
    {
        return _totals.GetValueOrDefault(name);
    }
}
