namespace Moltaro.Signals.Delivery;

// Signal delivery of one host: the pending signals and the members per (key, group), by the rules of section 3 of the
// concept.
//
// One per host. It is not thread-safe: the host calls it only under its lock. It performs no I/O, awaits nothing, runs
// no caller code, completes no waiter and knows nothing of sockets; its outcomes name the members whose waits the host
// completes.
//
// It holds the invariant that a signal is never pending while a member of its (key, group) waits. A (key, group)
// without members is not held, nor a key without groups, so nothing is kept for a group with no members on a key
// (rule 3).
//
// The host checks its own dispose and the subscription's under its lock, in the same section that calls Open, Arm or
// Set, so that SignalDelivery never receives a closed member.
internal sealed class SignalDelivery
{
    // The number of keys it holds.
    internal int KeyCount => throw new NotSupportedException();

    // The number of (key, group) pairs it holds.
    internal int GroupCount => throw new NotSupportedException();

    // Adds a new member of a group on a key. The member is Busy, because joining counts as a signal (rule 6). The
    // caller has checked that the key and the group are names.
    internal void Open(GroupMember member, string key, string group)
    {
        throw new NotSupportedException();
    }

    // Applies a set to a key and to each of its ancestors (rules 1 and 2): a set of a.b.c acts on a.b.c, a.b and a.
    // The caller has checked that the key is a name. The key is a span because its ancestors are its prefixes, looked
    // up without allocating.
    //
    // For each (key, group) held among these keys: if a member waits, the one that has waited longest becomes Busy
    // and is chained into FirstWoken; otherwise the signal becomes pending, and Coalesced counts it if one was pending
    // already. Groups is the number of those (key, group) pairs. FirstWoken is null when the set woke nobody. Waking
    // the member that has waited longest is a choice of this library, not a rule of the concept.
    internal SetOutcome Set(ReadOnlySpan<char> key)
    {
        throw new NotSupportedException();
    }

    // Arms the wait of an open member that does not wait (rule 4). True when a signal was pending for its
    // (key, group): the member takes it and is Busy. False when none was: the member now waits, last in the queue of
    // its (key, group), and is Waiting.
    internal bool Arm(GroupMember member)
    {
        throw new NotSupportedException();
    }

    // Ends the wait of a member, at the one point where the end of a wait and a signal handed to it are decided. True
    // when the member waited: it is now Idle and holds no signal. False when a signal came first and the member is
    // Busy, or when it does not wait; then nothing changes.
    internal bool EndWait(GroupMember member)
    {
        throw new NotSupportedException();
    }

    // Removes a member, which is then Idle. Closing a closed member does nothing.
    //
    // A member that was Busy hands its signal on as a set of its own (key, group), without the ancestors of its key
    // (rule 7): when a member of that (key, group) waits, the signal goes to the Receiver, which is now Busy;
    // otherwise the signal becomes pending and Pending is true. Neither, when the member was Idle or waiting, or the
    // last of its group on its key, so that nothing is kept (rule 3).
    internal CloseOutcome Close(GroupMember member)
    {
        throw new NotSupportedException();
    }

    // Removes every member, each then Idle, and returns the chain of those that waited, linked through NextWoken, so
    // that the host ends their waits; null when none waited.
    internal GroupMember? CloseAll()
    {
        throw new NotSupportedException();
    }
}
