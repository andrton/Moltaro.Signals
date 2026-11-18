namespace Moltaro.Signals.Delivery;

// Where a member stands by the rules of section 3 of the concept. Only SignalDelivery moves a member from one state
// to another.
internal enum GroupMemberStateEnum
{
    // The member holds a signal and its pass runs, until its next wait (rule 5): after it was opened, because joining
    // counts as a signal (rule 6), and after a signal was handed to it or its wait took a pending one. A set meanwhile
    // leaves a signal pending when no other member of its (key, group) waits; closing the member hands its signal on
    // (rule 7).
    Busy,

    // The member holds no signal and does not wait: after its wait ended without a signal, and after it was closed.
    // Closing it hands nothing on.
    Idle,

    // The member waits in the queue of its (key, group), until a signal is handed to it, its wait is ended or it is
    // closed.
    Waiting,
}
