namespace Moltaro.Signals.Delivery;

// One member of a group on a key, as SignalDelivery holds it. The subscriptions of the host derive from it, in-process
// and remote: one object per subscription, so nothing has to be looked up from the delivery to the subscription.
internal class GroupMember
{
    // Where the member stands by the rules: busy, idle or waiting. Only SignalDelivery changes it; the setter is
    // internal to the assembly because C# has no access level for a folder.
    internal GroupMemberStateEnum State { get; set; }

    // The next member of the chain an outcome names: the chain of SetOutcome.FirstWoken, or the one
    // SignalDelivery.CloseAll returns. A chain ends with null.
    //
    // Only SignalDelivery.Set and SignalDelivery.CloseAll write it, for the members they take out of Waiting;
    // SignalDelivery.Close leaves it. The host reads a member's NextWoken before it completes that member's wait: once
    // completed, the member may wait and be woken again, which relinks it.
    internal GroupMember? NextWoken { get; set; }

    // The group on the key that the member belongs to while it is open; null before it is opened and after it was
    // closed. This and the two links below are fields of signal delivery, not of its contract: only SignalDelivery and
    // KeyGroup use them.
    internal KeyGroup? KeyGroup { get; set; }

    // The neighbours of the member in the queue of the waiting members of its KeyGroup; null when it stands first or
    // last, and when it does not wait.
    internal GroupMember? PreviousWaiting { get; set; }

    internal GroupMember? NextWaiting { get; set; }
}
