using Moltaro.Signals.Delivery;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.Delivery;

// The rules 1 to 7 of concept section 3, on signal delivery directly; each test is named for the rule it states. A
// member is Busy once it has joined (rule 6), and it waits once it is armed while no signal is pending. No test depends
// on which of two waiting members of a group receives a signal: that is not a rule of the concept.
public sealed class RuleTests
{
    [Fact(Skip = SkipReason.NotImplemented)]
    public void Rule_1_a_set_hands_the_signal_to_exactly_one_waiting_member_of_each_group_on_its_key()
    {
        var delivery = new SignalDelivery();
        GroupMember worker1 = delivery.Join("jobs", "workers");
        GroupMember worker2 = delivery.Join("jobs", "workers");
        GroupMember cache = delivery.Join("jobs", "cache");
        GroupMember sender = delivery.Join("mail", "senders");
        delivery.Arm(worker1);
        delivery.Arm(worker2);
        delivery.Arm(cache);
        delivery.Arm(sender);

        List<GroupMember> woken = delivery.Set("jobs").FirstWoken.ToChain();

        GroupMember wokenWorker = woken.Contains(worker1) ? worker1 : worker2;
        GroupMember otherWorker = wokenWorker == worker1 ? worker2 : worker1;
        Assert.Equal(2, woken.Count);
        Assert.Contains(wokenWorker, woken);
        Assert.Contains(cache, woken);
        Assert.Equal(GroupMemberStateEnum.Busy, wokenWorker.State);
        Assert.Equal(GroupMemberStateEnum.Busy, cache.State);
        Assert.Equal(GroupMemberStateEnum.Waiting, otherWorker.State);
        Assert.Equal(GroupMemberStateEnum.Waiting, sender.State);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void Rule_1_a_set_while_no_member_waits_leaves_one_signal_pending_and_a_set_while_it_is_pending_does_nothing()
    {
        var delivery = new SignalDelivery();
        GroupMember lane1 = delivery.Join("jobs", "workers");
        GroupMember lane2 = delivery.Join("jobs", "workers");

        SetOutcome first = delivery.Set("jobs");
        SetOutcome second = delivery.Set("jobs");

        Assert.Null(first.FirstWoken);
        Assert.Equal(0, first.Coalesced);
        Assert.Null(second.FirstWoken);
        Assert.Equal(1, second.Coalesced);
        Assert.True(delivery.Arm(lane1));
        Assert.False(delivery.Arm(lane2));
    }

    // The host completes the wait of every member an outcome names, so a set names the members it woke and no other:
    // here the member that came first in the chain of an earlier set is woken alone.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void Rule_1_a_set_names_only_the_members_it_woke()
    {
        var delivery = new SignalDelivery();
        GroupMember worker = delivery.Join("jobs", "workers");
        GroupMember cache = delivery.Join("jobs", "cache");
        delivery.Arm(worker);
        delivery.Arm(cache);
        GroupMember firstWoken = delivery.Set("jobs").FirstWoken!;
        delivery.Arm(firstWoken);

        SetOutcome outcome = delivery.Set("jobs");

        Assert.Same(firstWoken, Assert.Single(outcome.FirstWoken.ToChain()));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void Rule_2_a_set_counts_each_group_with_members_on_its_key_once()
    {
        var delivery = new SignalDelivery();
        GroupMember waitingWorker = delivery.Join("jobs", "workers");
        delivery.Join("jobs", "workers");
        delivery.Join("jobs", "cache");
        delivery.Join("mail", "senders");
        delivery.Arm(waitingWorker);

        Assert.Equal(2, delivery.Set("jobs").Groups);
        Assert.Equal(0, delivery.Set("files").Groups);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void Rule_3_a_group_with_no_members_on_a_key_keeps_no_state_for_it()
    {
        var delivery = new SignalDelivery();
        GroupMember cache = delivery.Join("jobs", "cache");
        delivery.Set("jobs");
        delivery.Set("files");

        GroupMember worker = delivery.Join("jobs", "workers");

        Assert.Equal(1, delivery.KeyCount);
        Assert.Equal(2, delivery.GroupCount);
        Assert.False(delivery.Arm(worker));
        Assert.True(delivery.Arm(cache));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void Rule_4_a_wait_takes_a_pending_signal_at_once()
    {
        var delivery = new SignalDelivery();
        GroupMember lane = delivery.Join("jobs", "workers");
        delivery.Set("jobs");

        bool tookSignal = delivery.Arm(lane);

        Assert.True(tookSignal);
        Assert.Equal(GroupMemberStateEnum.Busy, lane.State);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void Rule_4_a_wait_with_no_signal_pending_waits_until_a_set_hands_it_one()
    {
        var delivery = new SignalDelivery();
        GroupMember lane = delivery.Join("jobs", "workers");

        bool tookSignal = delivery.Arm(lane);
        GroupMemberStateEnum whileWaiting = lane.State;
        SetOutcome outcome = delivery.Set("jobs");

        Assert.False(tookSignal);
        Assert.Equal(GroupMemberStateEnum.Waiting, whileWaiting);
        Assert.Same(lane, Assert.Single(outcome.FirstWoken.ToChain()));
        Assert.Equal(GroupMemberStateEnum.Busy, lane.State);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void Rule_5_a_member_handed_a_signal_is_busy_until_its_next_wait_and_sets_meanwhile_give_it_one_more_pass()
    {
        var delivery = new SignalDelivery();
        GroupMember lane = delivery.Join("jobs", "workers");
        delivery.Arm(lane);
        delivery.Set("jobs");

        SetOutcome meanwhile = delivery.Set("jobs");
        delivery.Set("jobs");

        Assert.Equal(GroupMemberStateEnum.Busy, lane.State);
        Assert.Null(meanwhile.FirstWoken);
        Assert.True(delivery.Arm(lane));
        Assert.False(delivery.Arm(lane));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void Rule_6_a_member_that_joins_is_busy_so_a_set_during_its_first_pass_is_not_lost()
    {
        var delivery = new SignalDelivery();
        GroupMember lane = delivery.Join("jobs", "workers");
        GroupMemberStateEnum afterJoining = lane.State;

        SetOutcome outcome = delivery.Set("jobs");

        Assert.Equal(GroupMemberStateEnum.Busy, afterJoining);
        Assert.Equal(1, outcome.Groups);
        Assert.True(delivery.Arm(lane));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void Rule_7_a_busy_member_that_leaves_hands_its_signal_to_a_member_of_its_group_that_already_waits()
    {
        var delivery = new SignalDelivery();
        GroupMember leaving = delivery.Join("jobs", "workers");
        GroupMember staying = delivery.Join("jobs", "workers");
        delivery.Arm(staying);

        CloseOutcome outcome = delivery.Close(leaving);

        Assert.Same(staying, outcome.Receiver);
        Assert.False(outcome.Pending);
        Assert.Equal(GroupMemberStateEnum.Busy, staying.State);
        Assert.Equal(GroupMemberStateEnum.Idle, leaving.State);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void Rule_7_a_busy_member_that_leaves_while_no_member_of_its_group_waits_leaves_its_signal_pending()
    {
        var delivery = new SignalDelivery();
        GroupMember leaving = delivery.Join("jobs", "workers");
        GroupMember staying = delivery.Join("jobs", "workers");

        CloseOutcome outcome = delivery.Close(leaving);

        Assert.Null(outcome.Receiver);
        Assert.True(outcome.Pending);
        Assert.True(delivery.Arm(staying));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void Rule_7_a_signal_handed_on_while_one_is_pending_leaves_one_signal_pending()
    {
        var delivery = new SignalDelivery();
        GroupMember leaving = delivery.Join("jobs", "workers");
        GroupMember staying = delivery.Join("jobs", "workers");
        delivery.Set("jobs");

        CloseOutcome outcome = delivery.Close(leaving);

        Assert.True(outcome.Pending);
        Assert.True(delivery.Arm(staying));
        Assert.False(delivery.Arm(staying));
    }
}
