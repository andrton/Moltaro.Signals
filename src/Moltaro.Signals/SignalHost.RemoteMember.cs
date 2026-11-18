using Moltaro.Signals.Delivery;

namespace Moltaro.Signals;

public sealed partial class SignalHost
{
    // One member that a client opened with SUB: one per open <sid> of a connection, and the member that the host's
    // signal delivery holds. It knows its connection and its <sid>, so that a signal handed to it becomes its SIG on
    // that connection.
    private sealed class RemoteMember(Connection connection, long sid) : GroupMember
    {
        // Under the host's lock, in the section that woke the member: writes its SIG, so that the output of its
        // connection follows the order of the decisions.
        internal void Signal()
        {
            connection.Signal(sid);
        }
    }
}
