# How a set wakes one member of each group on a key

An explanation for a developer who uses the library, not a specification. The rule itself
is [concept §3, "Semantics"](../concept.md#3-semantics). An animated version of this page
is [signal-delivery.html](signal-delivery.html).

**Status, 19 November 2026.** Built. Sprint S2 of the first epic built the in-process
host, `SignalHost` used as `ISignals`, with everything this page shows. Since sprint S3
the host also listens over TCP, and since sprint S5 `SignalClient` is the same `ISignals`
in other processes: its members belong to the same groups and see the same behavior. The
hosting package, `Moltaro.Signals.Hosting`, is not built yet.

## The idea

> A set gives each group on a key one signal: "look now". One waiting member of the group
> takes it. If no member waits, the signal stays pending until one does, and more sets
> change nothing.

A signal carries no data. The member that takes it reads the facts from its own storage,
so one pass is enough, however many sets came.

## The parts

- **Key.** The name of what changed, for example `orders`. A publisher sets the key with
  `SetAsync` after the change is stored.
- **Group.** Who competes for a key. Every group on a key gets its own signal; the members
  of one group share it.
- **Member.** One subscription, made with `Subscribe(key, group)`. A worker with two lanes
  makes two members of one group.
- **Signal.** What a set gives each group that has members on the key. It is handed to one
  waiting member, or it is pending.
- **Pending.** A signal that no member of the group has taken yet, because none was
  waiting. A group has at most one pending signal on a key.

A member runs one loop: it calls `WaitAsync`, does one pass over its storage, and calls
`WaitAsync` again. `WaitAsync` returns `Joined` the first time, `Signaled` when the member
received a signal, and `DeadlineReached` when the member's own deadline came first.
Whatever the result, the member does one full pass. After `Joined` or `Signaled` it is
*busy* until its next `WaitAsync`.

## Step by step

The steps follow one key, `orders`, with two groups: `billing` with the members A and B,
and `shipping` with the member C.

1. **Two groups on one key.** Key `orders` has two groups. `billing` has two members, A
   and B, for example two lanes of one worker; `shipping` has one, C. Each member's first
   `WaitAsync` returned `Joined`, it did one pass, and now it waits again. Nothing is
   pending.
2. **A set wakes one waiting member of each group.** An order is stored, and the publisher
   calls `SetAsync("orders")`. Each group gets its own signal, and one waiting member of
   each receives it: A in `billing`, C in `shipping`. B keeps waiting: one pass is enough.
   The set returns `Applied` with `Groups` 2. A and C are busy now, each doing one pass.
3. **A set while a member is busy leaves a signal pending.** Another order, another set.
   In `billing`, B still waits, so it receives the signal. In `shipping`, C is busy and
   nobody waits, so the signal becomes pending. It is there for C when C comes back.
4. **Twenty sets are one.** Twenty more orders, twenty more sets. The first makes a signal
   pending in `billing`, where A and B are both busy now. The other nineteen find a signal
   pending in both groups and do nothing. Each set still returns `Applied` with `Groups` 2.
5. **A wait that returns at once.** A and C finish their pass and call `WaitAsync` again.
   Each finds a signal pending for its group, takes it, and returns `Signaled` at once.
   Each does one more pass, which finds the new orders in storage.
6. **B waits, and nothing is pending.** B finishes its pass and calls `WaitAsync`. A has
   just taken the signal of `billing`, so nothing is pending there, and B waits.
7. **A busy member leaves while another waits.** A disposes its subscription while it is
   busy, before its next `WaitAsync`. Its pass may be unfinished, so A hands its signal on
   to its group on its key. B waits, so B receives it, returns `Signaled` and does the
   pass. `shipping` is not touched.
8. **C waits with a deadline.** C finishes its pass and calls `WaitAsync` with a deadline:
   10:05, when its next scheduled item falls due. Nothing is pending, so C waits for a
   signal or for 10:05, whichever comes first.
9. **A deadline that meets a signal: the signal first.** At 10:05 a set comes, at the same
   moment as the deadline. The host handles one of the two first; here it is the set. C
   receives the signal, and `WaitAsync` returns `Signaled`, not `DeadlineReached`. One
   result, one pass. In `billing`, B is busy, so the same set leaves a signal pending
   there.
10. **The same moment, the deadline first.** Back to 10:05, in the other order. `WaitAsync`
    returns `DeadlineReached`, and C does its pass without a signal. The set right after
    finds nobody waiting in `shipping`, so the signal becomes pending, and C's next
    `WaitAsync` returns at once. In either order the signal is not lost.

## What an application can rely on

- **One member of each group.** A set wakes at most one member of each group on the key.
  Which of several waiting members it wakes is not part of the rule.
- **Nothing piles up.** A set while a signal is pending does nothing. A group has at most
  one pending signal on a key, and a member at most one more pass to do.
- **The last set is not lost.** A set that finds no member waiting leaves a signal
  pending. A busy member that leaves hands its signal on. A wait that ends at its deadline
  takes no signal with it: a set that comes after it leaves a signal pending.
- **A set does not wait for the members.** In the host process, `SetAsync` returns at once
  with `Groups`, the number of groups that have members on the key; 0 means nobody
  listens. It does not wait for the member it wakes to do its pass.
- **No data, no order, no history.** The work stays in storage and is claimed there; a
  signal only says when to look.

## A key and its ancestors

A key is a path of segments separated by `.`: the ancestor of `orders.42` is `orders`. A
set of a key also sets each of its ancestors, by the same rule. `SetAsync("orders.42")`
wakes one waiting member of each group on `orders.42` and one of each group on `orders`,
and `Groups` counts each group on each of these keys once. A set of `orders` reaches no
member on `orders.42`. A busy member that leaves hands its signal on only on its own key.
