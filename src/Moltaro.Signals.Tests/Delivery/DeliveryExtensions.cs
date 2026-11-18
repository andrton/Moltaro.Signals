using Moltaro.Signals.Delivery;

namespace Moltaro.Signals.Tests.Delivery;

// What the tests of signal delivery need beyond its contract: a member that joins in one call, and the members of a
// chain that an outcome names.
internal static class DeliveryExtensions
{
    // Opens a new member of a group on a key. It is Busy, because joining counts as a signal (rule 6).
    internal static GroupMember Join(this SignalDelivery delivery, string key, string group)
    {
        var member = new GroupMember();

        delivery.Open(member, key, group);

        return member;
    }

    // The members of a chain linked through NextWoken, in its order. A chain names each member once and ends with
    // null, so a member that comes twice fails the test instead of looping.
    internal static List<GroupMember> ToChain(this GroupMember? first)
    {
        var chain = new List<GroupMember>();

        for (GroupMember? member = first; member is not null; member = member.NextWoken)
        {
            Assert.DoesNotContain(member, chain);
            chain.Add(member);
        }

        return chain;
    }
}
