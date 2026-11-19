namespace Moltaro.Signals;

public sealed partial class SignalClient
{
    // Where the wait of a remote subscription stands, by the rules of WaitAsync. Guarded by the client's lock.
    private enum WaitStateEnum
    {
        // No wait is pending, or the lock has decided how the pending one ends and it is completed after the lock.
        None,

        // The wait is pending until the member is open on a host, and then returns Joined (rules 1 and 5).
        ForOpening,

        // The member is armed on the host: its WAIT is written, and no SIG has come for it.
        Armed,

        // The deadline or the token ends the wait with the host: its UNWAIT is written, and the host's answer has not
        // come. A SIG before that answer makes the wait return Signaled (rule 4).
        Ending,
    }
}
