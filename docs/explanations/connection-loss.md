# Why nothing is lost when a connection drops or the host restarts

An explanation for a developer who uses the library, not a specification. The rules are
[concept §3, "Semantics"](../concept.md#3-semantics),
[concept §4, "Liveness"](../concept.md#4-liveness) and the wire format in
[docs/protocol.md](../protocol.md). An animated version of this page is
[connection-loss.html](connection-loss.html). How a set wakes a member while every
connection holds is the first explanation,
[How a set wakes one member of each group on a key](signal-delivery.md).

**Status, 19 November 2026.** Built: sprint S5 of the first epic built the remote client,
`SignalClient` used as `ISignals`, with everything this page shows; the host has listened
over TCP since sprint S3. Sprint S6 built the hosting package, `Moltaro.Signals.Hosting`,
which registers the host or a client in a .NET generic host.

## The idea

> The host keeps its signals only in memory, and any connection can fail. Nothing is lost
> anyway: a busy member that is lost hands its signal on, a client that is not connected
> says so at once, and after every reconnect each member does one full pass.

A signal carries no work. The work is stored before the set, so a pass finds it, whatever
happened to the signal.

## The parts

- **Host.** `SignalHost`, embedded in one process. It keeps the signals in its memory and
  nothing else, and listens on a TCP port.
- **Client.** `SignalClient`, in another process. It keeps one TCP connection to the host
  and is used as `ISignals`, by the same rules as code inside the host process.
- **Connection state.** A client's `State` is `Connected` or `Disconnected`. `StateChanged`
  reports each change, and gives a reason for `Disconnected`.
- **Heartbeat.** A `PING` line from the client, which the host answers with `PONG`, so that
  a quiet connection still carries traffic.
- **Member, signal, busy.** As in the [first explanation](signal-delivery.md): a member is
  one subscription; a set hands a signal to one waiting member of each group on the key;
  the member is then busy, doing one pass, until its next `WaitAsync`.

`WaitAsync` returns `Joined` for a member's first pass after subscribing and after every
reconnect, `Signaled` for a signal, and `DeadlineReached` at the member's own deadline.
`SetAsync` returns `Applied` with `Groups`, or `Unavailable` when the host did not confirm
the set.

## Step by step

The steps follow one host and three processes with a client each. The API stores orders
and sets the key `orders`. Worker 1 and worker 2 each run one member of the group `billing`
on `orders`: A in worker 1, B in worker 2. The times are the defaults of the options:
`HeartbeatInterval` 1 s, `DeadPeerTimeout` 3 s, and `ReconnectDelay` 0.2 s growing to
`MaxReconnectDelay` 2 s.

1. **Everyone is connected.** The API, worker 1 and worker 2 each have a client connected
   to the host, and each `State` is `Connected`. A and B returned `Joined`, did one pass
   each, and wait again: each worker sent `WAIT 1`. Nothing is pending.
2. **Heartbeats on a quiet connection.** No order comes for a while. A client that has sent
   nothing, or received nothing, for one second sends `PING`, and the host answers `PONG`.
   So while both sides are alive, each receives something about once a second. A side that
   receives nothing for three seconds declares its peer dead and closes the connection.
   TCP keepalive is not relied on.
3. **A set wakes A.** An order is stored, and the API calls `SetAsync("orders")`. Its
   client sends `SET 1 orders`. The host hands the signal to A with `SIG 1` to worker 1,
   and answers the API with `OK 1 1`. The set returns `Applied` with `Groups` 1. A is busy
   now, doing its pass; B keeps waiting.
4. **Worker 1's connection goes silent.** The network between worker 1 and the host fails.
   No line gets through in either direction, and no close either. A works on. Neither side
   knows it yet: worker 1 still reports `Connected`, and the host still counts A as busy.
5. **Three seconds of silence: the peer is dead.** The host has received nothing from
   worker 1 for three seconds. It closes the connection and removes A. A was busy, and its
   pass may be unfinished, so the host hands its signal on to `billing` on `orders`. B
   waits, so B receives it with `SIG 1` and does the pass. Worker 1 has received nothing
   for three seconds as well: its `State` becomes `Disconnected`, `StateChanged` reports it
   with a reason, and the client starts to reconnect. A may finish its pass too; two passes
   are harmless, because the work is claimed in storage.
6. **The host restarts.** The host process stops, for example to install a new version. It
   closes every connection, and its signals, which were only in its memory, are gone. The
   API and worker 2 see the end of their connections at once and report `Disconnected`. B
   works on, but no host knows of its signal any more. Every client tries to reconnect by
   itself.
7. **A set while not connected answers `Unavailable` at once.** Another order is stored,
   and the API calls `SetAsync("orders")`. Its client is not connected, so the call returns
   `Unavailable` at once. Nothing is sent, and nothing is kept to send later: a set never
   waits for a connection. The order is safe in storage; step 10 shows who finds it.
8. **The clients reconnect with backoff.** Each client tries again after a delay that
   doubles after each failed attempt: 0.2 s, 0.4 s, 0.8 s, 1.6 s, and then every 2 s. Every
   attempt fails while no host listens. Then the host process is back, with nothing in its
   memory, and the network of worker 1 works again. The next attempt of each client sends
   `HELLO` with the protocol version and the token, and the host accepts it with `OK 0`.
   The client's delay is back at 0.2 s for the next time.
9. **The subscriptions are opened again, then `Connected`.** On `OK 0`, each worker's
   client first sends a `SUB` for each of its subscriptions: worker 1 for A, worker 2 for B.
   Only then does each client report `Connected`, so any request made after `Connected`
   reaches the host after these `SUB`s. A and B have finished their passes and called
   `WaitAsync`. Each call waits for the host's `OK 1` to the `SUB` of its member. A member
   counts on the host only from that answer on; a `Joined` before it could let a set during
   the pass find nobody, and that set would be lost.
10. **`Joined`: one full pass for each member.** The host opens each member and answers
    `OK 1`. Joining counts as a signal, so each `WaitAsync` returns `Joined`, and A and B
    each do one full pass. Their passes find the order of step 7, whose set was answered
    `Unavailable`. A set during these passes leaves a signal pending, because the members
    are open and busy on the host. Nothing was lost.

## What an application can rely on

- **A dead peer is found in bounded time.** A connection that goes silent, because a
  network or a machine fails, is declared dead by each side after its `DeadPeerTimeout`,
  three seconds by default. Until then `State` may still say `Connected`. A connection that
  is closed, because a process stops or exits, ends as soon as its close arrives.
- **A set never waits for a connection.** A client that is not connected answers
  `Unavailable` at once, and never sends that set later. A connected client waits only for
  the host's answer, which carries `Groups`. If the connection is lost before that answer,
  the set returns `Unavailable`: it may have taken effect, and repeating it is harmless.
- **A busy member that is lost hands its signal on.** When a connection ends, the host
  removes its members. A busy one hands its signal on to its group on its key: a waiting
  member receives it, otherwise it becomes pending. If it was the last member of its group
  on that key, nothing is kept, and a member that joins later does a pass anyway.
- **After every reconnect, one full pass.** A `WaitAsync` while the client is not connected
  waits; its deadline and its token still end it, without asking a host. Once the host has
  answered the reopening of its member, the first `WaitAsync` returns `Joined`, as the
  first `WaitAsync` of a new subscription does. So the host may restart at any time and
  forget everything: every member looks once.
- **A wait that ends does not drop a signal.** A remote wait that ends at its deadline or
  by its token is ended with the host by `UNWAIT`. If the host handed a signal before that,
  the wait returns `Signaled`.
- **No queue and no replay.** A set answered `Unavailable` is not kept or repeated by the
  library. The work is in storage, and the pass after `Joined` finds it.
