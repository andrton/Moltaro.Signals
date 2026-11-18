namespace Moltaro.Signals.Delivery;

// What one SignalDelivery.Close did with the signal of the member it removed (rule 7), so that the host hands the signal
// to the receiver, with its SIG under its lock for a remote one and by completing its wait after the lock for an
// in-process one, and records the metrics after it has released the lock.
//
// Receiver: the waiting member of the same (key, group) that received the signal of a busy member that left, now
// Busy; null otherwise.
// Pending: true when the signal of a busy member that left became pending for its (key, group), because no member of
// it waited, whether or not a signal was pending already.
internal readonly record struct CloseOutcome(GroupMember? Receiver, bool Pending);
