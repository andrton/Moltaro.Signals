namespace Moltaro.Signals.Delivery;

// Signal delivery of one host: the pending signals and the members per (key, group), by the rules of section 3 of the
// concept.
//
// One per host. It is not thread-safe: the host calls it only under its lock. It performs no I/O, awaits nothing, runs
// no caller code, completes no waiter and knows nothing of sockets; its outcomes name the members it woke. The host
// answers a remote one with its SIG in the section under the lock that woke it, and completes the wait of an
// in-process one after the lock.
//
// It holds the invariant that a signal is never pending while a member of its (key, group) waits. A (key, group)
// without members is not held, nor a key without groups, so nothing is kept for a group with no members on a key
// (rule 3).
//
// The host checks its own dispose and the subscription's under its lock, in the same section that calls Open, Arm or
// Set, so that SignalDelivery never receives a closed member.
internal sealed class SignalDelivery
{
    // The groups with members on each key. Keys are compared exactly, case included.
    private readonly Dictionary<string, List<KeyGroup>> _keys = new(StringComparer.Ordinal);

    // The same keys, looked up by a span: the ancestors of a key are its prefixes, found without a string for each.
    private readonly Dictionary<string, List<KeyGroup>>.AlternateLookup<ReadOnlySpan<char>> _keysBySpan;

    // Every open member, so that CloseAll reaches the members that do not wait as well.
    private readonly HashSet<GroupMember> _members = [];

    internal SignalDelivery()
    {
        _keysBySpan = _keys.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    // The number of keys it holds.
    internal int KeyCount => _keys.Count;

    // The number of (key, group) pairs it holds, counted when it is asked for: only tests ask.
    internal int GroupCount => _keys.Values.Sum(groups => groups.Count);

    // Adds a new member of a group on a key. The member is Busy, because joining counts as a signal (rule 6). The
    // caller has checked that the key and the group are names.
    internal void Open(GroupMember member, string key, string group)
    {
        if (!_keys.TryGetValue(key, out List<KeyGroup>? groups))
        {
            groups = [];
            _keys.Add(key, groups);
        }

        KeyGroup? keyGroup = groups.Find(candidate => candidate.Group == group);

        if (keyGroup is null)
        {
            keyGroup = new KeyGroup(key, group);
            groups.Add(keyGroup);
        }

        keyGroup.MemberCount++;
        member.KeyGroup = keyGroup;
        member.State = GroupMemberStateEnum.Busy;
        _members.Add(member);
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
        int groupCount = 0;
        int coalesced = 0;
        GroupMember? firstWoken = null;
        GroupMember? lastWoken = null;
        ReadOnlySpan<char> level = key;

        while (true)
        {
            if (_keysBySpan.TryGetValue(level, out List<KeyGroup>? groups))
            {
                foreach (KeyGroup keyGroup in groups)
                {
                    groupCount++;

                    // A pending signal means that no member waits, so this set adds nothing.
                    if (keyGroup.IsPending)
                    {
                        coalesced++;
                        continue;
                    }

                    GroupMember? woken = Hand(keyGroup);

                    if (woken is not null)
                    {
                        Append(ref firstWoken, ref lastWoken, woken);
                    }
                }
            }

            // The segments are never empty, so the part before the last dot is the parent.
            int lastDot = level.LastIndexOf('.');

            if (lastDot < 0)
            {
                return new SetOutcome(groupCount, coalesced, firstWoken);
            }

            level = level[..lastDot];
        }
    }

    // Arms the wait of an open member that does not wait (rule 4). True when a signal was pending for its
    // (key, group): the member takes it and is Busy. False when none was: the member now waits, last in the queue of
    // its (key, group), and is Waiting.
    internal bool Arm(GroupMember member)
    {
        KeyGroup keyGroup = member.KeyGroup!;

        if (keyGroup.IsPending)
        {
            keyGroup.IsPending = false;
            member.State = GroupMemberStateEnum.Busy;

            return true;
        }

        keyGroup.Enqueue(member);
        member.State = GroupMemberStateEnum.Waiting;

        return false;
    }

    // Ends the wait of a member, at the one point where the end of a wait and a signal handed to it are decided. True
    // when the member waited: it is now Idle and holds no signal. False when a signal came first and the member is
    // Busy, or when it does not wait; then nothing changes.
    internal bool EndWait(GroupMember member)
    {
        if (member.State != GroupMemberStateEnum.Waiting)
        {
            return false;
        }

        member.KeyGroup!.Remove(member);
        member.State = GroupMemberStateEnum.Idle;

        return true;
    }

    // Removes a member, which is then Idle. Closing a closed member does nothing.
    //
    // A member that was Busy hands its signal on as a set of its own (key, group), without the ancestors of its key
    // (rule 7): when a member of that (key, group) waits, the signal goes to the Receiver, which is now Busy;
    // otherwise the signal becomes pending and Pending is true. Neither, when the member was Idle or waiting, or the
    // last of its group on its key, so that nothing is kept (rule 3).
    internal CloseOutcome Close(GroupMember member)
    {
        if (!_members.Remove(member))
        {
            return default;
        }

        KeyGroup keyGroup = member.KeyGroup!;
        GroupMemberStateEnum state = member.State;

        if (state == GroupMemberStateEnum.Waiting)
        {
            keyGroup.Remove(member);
        }

        member.State = GroupMemberStateEnum.Idle;
        member.KeyGroup = null;
        keyGroup.MemberCount--;

        if (keyGroup.MemberCount == 0)
        {
            Forget(keyGroup);

            return default;
        }

        if (state != GroupMemberStateEnum.Busy)
        {
            return default;
        }

        GroupMember? receiver = Hand(keyGroup);

        return new CloseOutcome(receiver, Pending: receiver is null);
    }

    // Removes every member, each then Idle, and returns the chain of those that waited, linked through NextWoken; null
    // when none waited. On its dispose the host ends the waits of the in-process members in the chain and does nothing
    // for the remote ones: it closes every connection anyway.
    internal GroupMember? CloseAll()
    {
        GroupMember? firstWaited = null;
        GroupMember? lastWaited = null;

        foreach (GroupMember member in _members)
        {
            if (member.State == GroupMemberStateEnum.Waiting)
            {
                member.KeyGroup!.Remove(member);
                Append(ref firstWaited, ref lastWaited, member);
            }

            member.State = GroupMemberStateEnum.Idle;
            member.KeyGroup = null;
        }

        _members.Clear();
        _keys.Clear();

        return firstWaited;
    }

    // Hands a signal to one (key, group), as rule 1 says: to the member that has waited longest, which is then Busy and
    // is returned; when no member waits, the signal is pending and null is returned.
    private static GroupMember? Hand(KeyGroup keyGroup)
    {
        GroupMember? receiver = keyGroup.Dequeue();

        if (receiver is null)
        {
            keyGroup.IsPending = true;

            return null;
        }

        receiver.State = GroupMemberStateEnum.Busy;

        return receiver;
    }

    // Puts a member last in a chain linked through NextWoken.
    private static void Append(ref GroupMember? first, ref GroupMember? last, GroupMember member)
    {
        member.NextWoken = null;

        if (last is null)
        {
            first = member;
        }
        else
        {
            last.NextWoken = member;
        }

        last = member;
    }

    // Drops a (key, group) whose last member has left, and its key when no group is left on it (rule 3).
    private void Forget(KeyGroup keyGroup)
    {
        List<KeyGroup> groups = _keys[keyGroup.Key];

        groups.Remove(keyGroup);

        if (groups.Count == 0)
        {
            _keys.Remove(keyGroup.Key);
        }
    }
}
