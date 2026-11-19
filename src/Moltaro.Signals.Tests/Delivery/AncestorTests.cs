using Moltaro.Signals.Delivery;

namespace Moltaro.Signals.Tests.Delivery;

// Hierarchical keys (concept D2) and the rules acting per (key, group) (concept section 3, rules 1, 2 and 7), on signal
// delivery directly. A set of a key also sets each of its ancestors, the keys made of its leading segments, and never
// a descendant, and counts each (key, group) among these keys once; one group on a key and on an ancestor of it has
// separate members and separate pending signals on each.
public sealed class AncestorTests
{
    [Fact]
    public void A_set_of_a_key_wakes_a_waiting_member_of_its_ancestor()
    {
        var delivery = new SignalDelivery();
        GroupMember entity = delivery.Join("entity", "cache");
        delivery.Arm(entity);

        SetOutcome outcome = delivery.Set("entity.contacts.42");

        Assert.Equal(1, outcome.Groups);
        Assert.Same(entity, Assert.Single(outcome.FirstWoken.ToChain()));
    }

    [Fact]
    public void A_set_of_a_key_wakes_no_member_of_its_descendant()
    {
        var delivery = new SignalDelivery();
        GroupMember contacts = delivery.Join("entity.contacts", "cache");
        delivery.Arm(contacts);

        SetOutcome outcome = delivery.Set("entity");

        Assert.Equal(0, outcome.Groups);
        Assert.Null(outcome.FirstWoken);
        Assert.Equal(GroupMemberStateEnum.Waiting, contacts.State);
    }

    // "entity" is not an ancestor of "entity2.contacts", although the one begins with the characters of the other.
    [Fact]
    public void A_set_of_a_key_sets_only_ancestors_made_of_whole_segments()
    {
        var delivery = new SignalDelivery();
        GroupMember entity = delivery.Join("entity", "cache");
        delivery.Arm(entity);

        SetOutcome outcome = delivery.Set("entity2.contacts");

        Assert.Equal(0, outcome.Groups);
        Assert.Equal(GroupMemberStateEnum.Waiting, entity.State);
    }

    [Fact]
    public void Sets_of_two_children_coalesce_on_their_ancestor()
    {
        var delivery = new SignalDelivery();
        GroupMember entity = delivery.Join("entity", "cache");

        SetOutcome first = delivery.Set("entity.contacts");
        SetOutcome second = delivery.Set("entity.orders");

        Assert.Equal(0, first.Coalesced);
        Assert.Equal(1, second.Coalesced);
        Assert.True(delivery.Arm(entity));
        Assert.False(delivery.Arm(entity));
    }

    [Fact]
    public void A_set_counts_each_key_and_group_over_its_key_and_its_ancestors_once()
    {
        var delivery = new SignalDelivery();
        delivery.Join("entity", "cache");
        delivery.Join("entity", "cache");
        delivery.Join("entity.contacts", "cache");
        delivery.Join("entity.contacts", "search");
        delivery.Join("entity.contacts.42", "cache");

        SetOutcome outcome = delivery.Set("entity.contacts");

        Assert.Equal(3, outcome.Groups);
    }

    [Fact]
    public void A_set_of_a_key_wakes_one_waiting_member_of_a_group_on_the_key_and_one_on_its_ancestor()
    {
        var delivery = new SignalDelivery();
        GroupMember onKey = delivery.Join("entity.contacts", "cache");
        GroupMember onAncestor = delivery.Join("entity", "cache");
        delivery.Arm(onKey);
        delivery.Arm(onAncestor);

        List<GroupMember> woken = delivery.Set("entity.contacts").FirstWoken.ToChain();

        Assert.Equal(2, woken.Count);
        Assert.Contains(onKey, woken);
        Assert.Contains(onAncestor, woken);
    }

    [Fact]
    public void A_group_on_a_key_and_on_its_ancestor_has_a_pending_signal_of_its_own_on_each()
    {
        var delivery = new SignalDelivery();
        GroupMember onKey = delivery.Join("entity.contacts", "cache");
        GroupMember onAncestor = delivery.Join("entity", "cache");

        delivery.Set("entity.contacts");

        Assert.True(delivery.Arm(onAncestor));
        Assert.True(delivery.Arm(onKey));
    }

    [Fact]
    public void A_busy_member_that_leaves_hands_its_signal_on_only_on_its_own_key()
    {
        var delivery = new SignalDelivery();
        GroupMember leaving = delivery.Join("entity.contacts", "cache");
        GroupMember staying = delivery.Join("entity.contacts", "cache");
        GroupMember onAncestor = delivery.Join("entity", "cache");
        GroupMember onDescendant = delivery.Join("entity.contacts.42", "cache");
        delivery.Arm(onAncestor);
        delivery.Arm(onDescendant);

        CloseOutcome outcome = delivery.Close(leaving);

        Assert.Null(outcome.Receiver);
        Assert.True(outcome.Pending);
        Assert.Equal(GroupMemberStateEnum.Waiting, onAncestor.State);
        Assert.Equal(GroupMemberStateEnum.Waiting, onDescendant.State);
        Assert.True(delivery.Arm(staying));
    }
}
