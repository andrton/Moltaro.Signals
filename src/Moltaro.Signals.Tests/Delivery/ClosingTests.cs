using Moltaro.Signals.Delivery;

namespace Moltaro.Signals.Tests.Delivery;

// Members that leave without a signal to hand on, the last member of a group on a key (rules 3 and 7), and the end of
// every member when the host is disposed, on signal delivery directly.
public sealed class ClosingTests
{
    private static readonly CloseOutcome NothingHandedOn = new(Receiver: null, Pending: false);

    // A set afterwards finds the member that stays still waiting, and does not hand the signal to the one that left.
    [Fact]
    public void A_member_that_leaves_while_idle_or_waiting_hands_nothing_on()
    {
        var delivery = new SignalDelivery();
        GroupMember idle = delivery.Join("jobs", "workers");
        GroupMember waiting = delivery.Join("jobs", "workers");
        GroupMember staying = delivery.Join("jobs", "workers");
        delivery.Arm(idle);
        delivery.EndWait(idle);
        delivery.Arm(waiting);
        delivery.Arm(staying);

        CloseOutcome idleLeft = delivery.Close(idle);
        CloseOutcome waitingLeft = delivery.Close(waiting);
        SetOutcome outcome = delivery.Set("jobs");

        Assert.Equal(NothingHandedOn, idleLeft);
        Assert.Equal(NothingHandedOn, waitingLeft);
        Assert.Equal(GroupMemberStateEnum.Idle, waiting.State);
        Assert.Same(staying, Assert.Single(outcome.FirstWoken.ToChain()));
    }

    [Fact]
    public void The_last_member_of_a_group_on_a_key_that_leaves_hands_nothing_on_and_nothing_is_kept()
    {
        var delivery = new SignalDelivery();
        GroupMember worker = delivery.Join("jobs", "workers");
        GroupMember cache = delivery.Join("jobs", "cache");
        delivery.Set("jobs");

        CloseOutcome workerLeft = delivery.Close(worker);
        int groupsWithCache = delivery.GroupCount;
        int keysWithCache = delivery.KeyCount;
        CloseOutcome cacheLeft = delivery.Close(cache);
        int groupsLeft = delivery.GroupCount;
        int keysLeft = delivery.KeyCount;
        GroupMember joined = delivery.Join("jobs", "workers");

        Assert.Equal(NothingHandedOn, workerLeft);
        Assert.Equal(NothingHandedOn, cacheLeft);
        Assert.Equal(1, groupsWithCache);
        Assert.Equal(1, keysWithCache);
        Assert.Equal(0, groupsLeft);
        Assert.Equal(0, keysLeft);
        Assert.False(delivery.Arm(joined));
    }

    [Fact]
    public void Closing_a_closed_member_does_nothing()
    {
        var delivery = new SignalDelivery();
        GroupMember leaving = delivery.Join("jobs", "workers");
        GroupMember staying = delivery.Join("jobs", "workers");
        delivery.Arm(staying);
        delivery.Close(leaving);

        CloseOutcome again = delivery.Close(leaving);

        Assert.Equal(NothingHandedOn, again);
        Assert.Equal(GroupMemberStateEnum.Idle, leaving.State);
        Assert.Equal(1, delivery.GroupCount);
        Assert.False(delivery.Arm(staying));
    }

    [Fact]
    public void Closing_every_member_names_those_that_waited_and_keeps_nothing()
    {
        var delivery = new SignalDelivery();
        GroupMember waitingWorker = delivery.Join("jobs", "workers");
        GroupMember busyWorker = delivery.Join("jobs", "workers");
        GroupMember waitingSender = delivery.Join("mail", "senders");
        GroupMember idleSender = delivery.Join("mail", "senders");
        delivery.Arm(waitingWorker);
        delivery.Arm(waitingSender);
        delivery.Arm(idleSender);
        delivery.EndWait(idleSender);

        List<GroupMember> waited = delivery.CloseAll().ToChain();

        GroupMember[] members = [waitingWorker, busyWorker, waitingSender, idleSender];
        Assert.Equal(2, waited.Count);
        Assert.Contains(waitingWorker, waited);
        Assert.Contains(waitingSender, waited);
        Assert.All(members, member => Assert.Equal(GroupMemberStateEnum.Idle, member.State));
        Assert.Equal(0, delivery.KeyCount);
        Assert.Equal(0, delivery.GroupCount);
    }
}
