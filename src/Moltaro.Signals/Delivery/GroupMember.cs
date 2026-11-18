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
}
