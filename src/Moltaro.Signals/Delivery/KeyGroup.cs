namespace Moltaro.Signals.Delivery;

// One group on one key, for as long as it has members: how many, whether a signal is pending, and the queue of the
// members that wait, the one that has waited longest first. Part of SignalDelivery, which alone uses it; the host's
// lock guards it.
internal sealed class KeyGroup(string key, string group)
{
    // The ends of the queue of waiting members, linked through GroupMember.PreviousWaiting and NextWaiting.
    private GroupMember? _firstWaiting;
    private GroupMember? _lastWaiting;

    internal string Key { get; } = key;

    internal string Group { get; } = group;

    // The open members of the group on the key, whether they wait or not.
    internal int MemberCount { get; set; }

    // A signal that no member has taken yet; there is at most one. Never true while a member waits, because a signal
    // goes to a waiting member instead.
    internal bool IsPending { get; set; }

    // Puts a member last in the queue of those that wait.
    internal void Enqueue(GroupMember member)
    {
        member.PreviousWaiting = _lastWaiting;
        member.NextWaiting = null;

        if (_lastWaiting is null)
        {
            _firstWaiting = member;
        }
        else
        {
            _lastWaiting.NextWaiting = member;
        }

        _lastWaiting = member;
    }

    // Takes the member that has waited longest out of the queue; null when no member waits.
    internal GroupMember? Dequeue()
    {
        GroupMember? first = _firstWaiting;

        if (first is not null)
        {
            Remove(first);
        }

        return first;
    }

    // Takes a waiting member out of the queue, wherever it stands in it.
    internal void Remove(GroupMember member)
    {
        if (member.PreviousWaiting is null)
        {
            _firstWaiting = member.NextWaiting;
        }
        else
        {
            member.PreviousWaiting.NextWaiting = member.NextWaiting;
        }

        if (member.NextWaiting is null)
        {
            _lastWaiting = member.PreviousWaiting;
        }
        else
        {
            member.NextWaiting.PreviousWaiting = member.PreviousWaiting;
        }

        member.PreviousWaiting = null;
        member.NextWaiting = null;
    }
}
