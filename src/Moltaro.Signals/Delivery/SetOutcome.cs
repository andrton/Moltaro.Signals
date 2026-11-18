namespace Moltaro.Signals.Delivery;

// What one SignalDelivery.Set did, so that the host completes the woken waits and records the metrics after it has
// released its lock.
//
// Groups: the number of (key, group) pairs over the key and its ancestors that the delivery holds, each counted once
// (rule 2); 0 means nobody listens.
// Coalesced: the number of those (key, group) pairs in which no member waited and a signal was pending already.
// FirstWoken: the first of the members the set woke, each now Busy and linked to the next through
// GroupMember.NextWoken; the chain ends with null, and FirstWoken is null when the set woke nobody.
internal readonly record struct SetOutcome(int Groups, int Coalesced, GroupMember? FirstWoken);
