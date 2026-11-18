using System.Diagnostics.Metrics;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.InProcess;

// The State of the in-process ISignals of a host and the end of its in-process side when the host is disposed.
public sealed class HostLifetimeTests
{
    [Fact]
    public async Task The_host_is_Connected_from_its_construction_until_it_is_disposed()
    {
        var host = new SignalHost(LoopbackOptions.Host());
        SignalConnectionStateEnum beforeDispose = host.State;

        await host.DisposeAsync();

        Assert.Equal(SignalConnectionStateEnum.Connected, beforeDispose);
        Assert.Equal(SignalConnectionStateEnum.Disconnected, host.State);
    }

    [Fact]
    public async Task Disposing_the_host_ends_its_pending_waits_and_refuses_later_calls()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var host = new SignalHost(LoopbackOptions.Host());
        ISignalSubscription lane = host.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null, cancellationToken);
        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null, cancellationToken);

        await host.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await wait);
        Assert.Throws<ObjectDisposedException>(() => lane.WaitAsync(until: null, cancellationToken));
        Assert.Throws<ObjectDisposedException>(() => host.Subscribe("jobs", "workers"));
        Assert.Throws<ObjectDisposedException>(() => host.SetAsync("jobs", cancellationToken));

        // The subscription ended with the host; its own dispose does nothing, and so does a second dispose of the host.
        lane.Dispose();
        await host.DisposeAsync();
    }

    // The first dispose ends the pending waits, then disposes the host's meter, which ends the measurements of a
    // listener inside that dispose. The second dispose is made there, while the first has not completed.
    [Fact]
    public async Task A_second_dispose_completes_only_after_the_first_has_ended_the_pending_waits()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var host = new SignalHost(LoopbackOptions.Host());
        ISignalSubscription lane = host.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null, cancellationToken);
        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null, CancellationToken.None);
        Task? second = null;
        bool waitEndedBeforeSecond = false;
        bool secondCompletedAtOnce = false;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (ReferenceEquals(instrument.Meter.Scope, host))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
            MeasurementsCompleted = (instrument, state) =>
            {
                if (second is null)
                {
                    waitEndedBeforeSecond = wait.IsCompleted;
                    second = host.DisposeAsync().AsTask();
                    secondCompletedAtOnce = second.IsCompleted;
                }
            },
        };
        listener.Start();

        await host.DisposeAsync();

        Assert.True(waitEndedBeforeSecond);
        Assert.False(secondCompletedAtOnce);
        Assert.NotNull(second);
        await second;
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await wait);
    }
}
