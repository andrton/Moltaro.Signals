using Moltaro.Signals.Delivery;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.Delivery;

// A wait that ends by its deadline or by its cancellation, in both orders against a signal (rules 4 and 5), on signal
// delivery directly. The host ends either with EndWait, the one point where the end of a wait and a signal handed to
// it are decided, so a deadline and a cancellation are the same call here.
public sealed class EndOfWaitTests
{
    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_wait_ended_before_a_set_holds_no_signal_and_the_set_leaves_one_pending()
    {
        var delivery = new SignalDelivery();
        GroupMember lane = delivery.Join("jobs", "workers");
        delivery.Arm(lane);

        bool ended = delivery.EndWait(lane);
        SetOutcome outcome = delivery.Set("jobs");

        Assert.True(ended);
        Assert.Equal(GroupMemberStateEnum.Idle, lane.State);
        Assert.Null(outcome.FirstWoken);
        Assert.True(delivery.Arm(lane));
    }

    // The signal stays with the member, so leaving hands it on (rule 7).
    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_wait_ended_after_a_set_keeps_the_signal_it_was_handed()
    {
        var delivery = new SignalDelivery();
        GroupMember lane = delivery.Join("jobs", "workers");
        GroupMember other = delivery.Join("jobs", "workers");
        delivery.Arm(lane);
        delivery.Set("jobs");

        bool ended = delivery.EndWait(lane);
        GroupMemberStateEnum afterEnd = lane.State;
        delivery.Arm(other);
        CloseOutcome left = delivery.Close(lane);

        Assert.False(ended);
        Assert.Equal(GroupMemberStateEnum.Busy, afterEnd);
        Assert.Same(other, left.Receiver);
    }
}
