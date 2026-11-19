# Moltaro.Signals — concept

Status: agreed concept, 18 November 2026. The library it describes is built: the host, the
client and the hosting package. [README.md](../README.md) says what works today.

## 1. Where it comes from

A typical business platform runs API processes and background worker processes over one
database. Durable work lives in database tables: jobs, outboxes, commands, deliveries.
Workers claim rows (for example with `FOR UPDATE SKIP LOCKED`) and process them. The only
thing a worker needs from the outside is a nudge: *"something new is there, look now"*.

Such platforms often use a message broker for that nudge. In practice it behaves badly:

- **Duplicates pile up.** Twenty writes produce twenty wake-up messages. A durable queue
  keeps them while the worker is down; after restart each one triggers its own pass over
  the database, although one pass was enough.
- **Every receiver wakes.** A wake-up is broadcast to every worker process; all of them
  run to the database and race for the same rows.
- **A wake-up can overtake the data.** It is published before the writing transaction
  commits; the worker looks, finds nothing and falls asleep.
- **Polling as a safety net.** Because wake-ups are unreliable, every loop also polls the
  database every one or two seconds. The database is loaded even when idle, and every hop
  of a processing chain adds up to one poll interval of latency.
- **Nobody knows whether the receiver is alive.** A publisher cannot tell that no worker is
  running; a caller waits for a timeout instead of getting an immediate answer.
- **An extra service** has to be shipped, configured, secured and monitored, only to carry
  messages whose content nobody reads.

The observation behind this project: **a wake-up is not a message, it is a signal that is
either pending or taken.** Twenty sets while a signal is pending are one set. A lost
wake-up is harmless if a receiver always looks once after it (re)connects. What matters is
only this: never miss the *last* set, never wake more receivers than needed, and know at
once whether the other side is alive.

## 2. What it is

A small .NET library with one primitive: **remote signals with consumer groups**.

- A **host** keeps the signals in memory. It is embedded in one application process (the
  process that owns the background work) and listens on a TCP port.
- **Clients** in other processes connect over one persistent TCP connection that speaks a
  line-based text protocol. Code inside the host process uses the same client interface
  without a socket.
- Signals carry **no data**. The key names what changed; the receiver reads the facts from
  its own storage.

It is not a message broker, a queue, a lock or a pub/sub bus with payloads.

## 3. Semantics

### Terms

| Term | Meaning |
| --- | --- |
| Key | Name of what changed, e.g. `jobs` or `outbox.mail`; its segments, separated by `.`, make its ancestors (`outbox` for `outbox.mail`) |
| Group | Who competes for a key: a consumer group. Members of one group on a key share each signal of that key; different groups each get their own |
| Subscription | One member of a group on one connection. A worker with four lanes opens four subscriptions |
| Signal | What a set gives each group that has members on a key: "look now". It carries no data |
| Pending signal | A signal that no member of the group on that key has taken yet, because none waited when the set came. At most one per (key, group); a set while one is pending does nothing |

Groups give both delivery styles with one rule:

- **Competing:** all worker lanes join group `workers` → one set wakes exactly one lane.
- **Broadcast:** every process joins with its own group name → one set wakes one lane in
  every process (for example to drop a local cache).

### Rules

1. **Set(key)** acts on the key and on each of its ancestors: for every (key, group) among
   these keys that currently has members, if a member is waiting, the signal is handed to
   exactly one waiting member; otherwise the signal becomes pending. A set while a signal
   is pending does nothing. **Set is idempotent.**
2. **Set returns** the number of (key, group) pairs over the key and its ancestors that
   have at least one live member, each counted once. Zero means nobody listens, so the
   caller can answer at once instead of waiting.
3. A group with **no members on a key** keeps no state for it: a set is not remembered for
   it. A member that joins later starts with one pass anyway (rule 6).
4. **Wait(subscription)** — if a signal is pending for its group on its key, the member
   takes it and Wait returns at once; otherwise the member waits until a signal is handed
   to it, until its own deadline, or until reconnect.
5. **Busy until the next Wait.** A member that received a signal is busy until it calls
   Wait again. A set that arrives meanwhile leaves a signal pending, so the member's next
   Wait returns at once: *"if it was set while I was working, it is waiting for me when I
   come back"*. At most one pass runs and at most one more is due per member, and at most
   one signal is pending per group on a key; nothing accumulates.
6. **Joining and reconnecting count as a signal.** The first Wait after subscribing or
   after a reconnect returns `Joined` as soon as the host has opened the subscription; at
   once when it is open. The member does one full pass. This is
   what makes the host stateless: after a host restart nothing is lost.
7. **Leaving while busy hands the signal on.** If a busy member disconnects or unsubscribes
   before its next Wait, the host hands its signal on to its group on its key, as rule 1
   does for that one (key, group) without its ancestors: a waiting member receives it,
   otherwise it becomes pending. So another member (or the same one after reconnect) does
   the pass.

No ordering, history, payload or exactly-once processing is provided. Work must stay in
durable storage and be claimed there; a signal only makes waiting free.

### Typical consumer loop

```csharp
using var lane = signals.Subscribe("jobs", "workers");
while (!ct.IsCancellationRequested)
{
    // Returns on a signal, on Joined (first call / reconnect) or at the next own deadline.
    await lane.WaitAsync(until: await store.GetNextDueTimeAsync(ct), ct);
    await ProcessEverythingClaimableAsync(ct);
}
```

A lane that finds more work than one batch can call `Set` itself to wake another lane.

### Typical publisher

```csharp
// After the writing transaction has committed — never inside it.
var result = await signals.SetAsync("jobs", ct);
// Applied with Groups 0   -> no receiver is running; the work waits in storage.
// Status is Unavailable   -> this client has no connection to the host right now.
```

## 4. Liveness

- The client sends `PING` when idle for one second; the host answers `PONG`. Either side
  declares the connection dead after three seconds without any traffic and closes it. The
  numbers are configurable. TCP keepalive is not relied on.
- The client exposes its **state** (`Connected` / `Disconnected`) and a change
  notification, so an application knows at once that the host process is gone.
- While disconnected, **Set never blocks and never queues**: it returns `Unavailable`
  immediately. Receivers do a full pass when the connection returns (rule 6).
- The client reconnects by itself with a short backoff (for example 0.2 s growing to 2 s).

## 5. Hosting

- The host is **embedded** in one process and started before that process's own workers.
  In a .NET generic host it is registered first; its `StartAsync` binds the port before
  later hosted services start.
- There is **one host per deployment**. No clustering, replication, persistence or
  failover: if the host process is down, its clients see `Disconnected` within seconds and
  sets are dropped; when it comes back, every member does one pass.
- Code inside the host process uses the host directly through the same `ISignals`
  interface, with no socket to itself.
- A standalone host executable is not part of v1; a ~30-line sample shows how to run one.

## 6. Protocol

UTF-8 text, one command per line, `\r\n`, debuggable with `telnet` or `nc`.
The exact grammar is fixed in [docs/protocol.md](protocol.md), which is its authority;
these principles are decided:

| Client → host | Meaning |
| --- | --- |
| `HELLO <protocol-version> <token>` | First line; wrong token or version closes the connection |
| `SUB <sid> <key> <group>` | Open subscription `<sid>` (a client-chosen number) |
| `WAIT <sid>` | Arm the subscription; ends its busy state |
| `UNWAIT <sid>` | End the wait of the subscription; answered after any `SIG` already sent |
| `UNSUB <sid>` | Close the subscription (hands its signal on if busy) |
| `SET <rid> <key>` | Set; `<rid>` is a request id echoed in the reply |
| `PING` | Heartbeat |

| Host → client | Meaning |
| --- | --- |
| `OK <rid> [<groups>]` / `ERR <rid> <code> <text>` | Reply to the request `<rid>` |
| `SIG <sid>` | Push: the signal is handed to subscription `<sid>` |
| `PONG` | Heartbeat answer |

Every reply names the request it answers; pushes are distinguishable by their verb.
Keys and groups use a restricted character set (`A–Z a–z 0–9 . _ : -`) and a length limit;
the host also limits line length, subscriptions per connection and connections.

## 7. .NET surface

```csharp
public interface ISignals
{
    SignalConnectionStateEnum State { get; }
    event EventHandler<SignalConnectionChangedEventArgs>? StateChanged;

    ValueTask<SignalSetResult> SetAsync(string key, CancellationToken cancellationToken = default);
    ISignalSubscription Subscribe(string key, string group);
}

public interface ISignalSubscription : IDisposable
{
    /// <summary>Joined (first call or after reconnect), Signaled or DeadlineReached.</summary>
    ValueTask<SignalWaitResultEnum> WaitAsync(DateTimeOffset? until, CancellationToken cancellationToken = default);
}
```

`SignalHost` implements it in the host process and `SignalClient` in other processes;
`Moltaro.Signals.Hosting` registers either in a generic host. Every public member is
documented in the code.

## 8. Decisions

| # | Decision | Why |
| --- | --- | --- |
| D1 | A signal has no payload, history or persistence | Receivers read facts from their storage; anything else re-creates a broker |
| D2 | Coalescing happens on the host per (key, group): at most one signal is pending; Set is idempotent; a set of a key also sets its ancestors | Duplicates must not exist anywhere, not only inside one process |
| D3 | One waiting member per (key, group) receives a signal; groups provide broadcast | No thundering herd, and both styles with one rule |
| D4 | Busy until the next Wait; leaving while busy hands the signal on | No lost work when a receiver dies mid-pass, without acknowledgements |
| D5 | Joining and reconnecting return `Joined` once | The host can be memory-only and restart freely |
| D6 | Embedded host, one per deployment, no cluster | The process that does the background work is the natural owner; nothing else to run |
| D7 | Persistent TCP connection, text line protocol, 1 s / 3 s heartbeats | Liveness is visible in seconds; trivial to debug and to implement in any language |
| D8 | Set returns the live group count; `Unavailable` at once when not connected; it never waits for a connection, and a connected client waits only for the host's answer, bounded by the dead-peer time | Callers answer at once instead of waiting for a host that is gone |
| D9 | Shared token in `HELLO`; bind address configurable | Enough for an internal network; TLS is planned, not v1 |
| D10 | Core package without dependencies plus a thin hosting/DI package | Same split as other Moltaro libraries; easy to embed |
| D11 | .NET 10, Apache-2.0, public GitHub repository, English documentation | Matches the sibling projects; usable and showable by anyone |
| D12 | Nothing product-specific in the repository | It is a general library; product adoption is documented by the product |
| D13 | Versions stay 0.x until a first production adopter runs on it; the protocol version is part of `HELLO` | Freedom to fix the design before promising compatibility |
| D14 | KISS: the scope is this primitive; requests outside it are declined or noted as planned | The value is that it is small enough to trust |
| D15 | Source on GitHub; packages on nuget.org, built and pushed by GitHub Actions from a version tag through nuget.org Trusted Publishing, with no stored API key. Releases are regular versions, not prereleases | nuget.org is free and needs no token to restore; GitHub Packages requires an access token even to install public packages. Trusted Publishing leaves no long-lived secret to leak |

## 9. Non-goals and possible later work

Not in v1: payloads, durable state, clustering or failover, ordering, exactly-once
delivery, locks or leases, browser clients, TLS, a standalone server binary, non-.NET
clients.

Possibly later, only with a real consumer: a signal that stays pending for all members
until it is cleared, wildcards in keys, setting the descendants of a key, watching a
group's presence, TLS, clients in other languages.

## 10. Existing tools and why they are not used

| Tool | Why not |
| --- | --- |
| Message brokers (RabbitMQ, Kafka) | Messages, not coalesced state; duplicates and an extra service |
| NATS core / queue groups | Closest in spirit, but no coalescing per key and still an external server |
| PostgreSQL `LISTEN/NOTIFY` | Coalesces only inside one transaction, takes a global lock at commit, ties signalling to the database |
| Redis pub/sub or lists | External service; pub/sub loses signals, lists keep duplicates |
| etcd / Consul watches | Raft clusters; far heavier than the problem |
| OS named events (`EventWaitHandle`) | One machine only |
| Distributed lock libraries | Locks and semaphores, not signals |

## 11. Quality bar

- **Tests.**
  - Signal delivery is a pure component with deterministic unit tests, covering
    every rule in section 3 with a fake clock.
  - Protocol parsing and formatting have their own unit tests.
  - Loopback TCP integration tests cover connect, heartbeat loss, host restart (`Joined`),
    leaving while busy (hand on) and limits.
  - No fixed sleeps in tests; they wait for observed events.
- **Performance targets** (measured by a separate benchmark program, not gating tests),
  judged as the owner decided on 18 November 2026:
  - in-process set to wake under 50 µs, judged at p50; p99 and the maximum are reported;
  - loopback TCP set to wake p99 under 1 ms, with the host and its clients in one
    process;
  - at least 100 000 sets per second on one core, most of them coalesced, judged
    in-process: one thread setting ten keys, each with one group whose member wakes for
    at most one set in a hundred; the rate over loopback is reported, not judged;
  - memory grows with keys × groups + subscriptions only: the bytes retained per
    (key, group) and per subscription are reported, and a test of signal delivery asserts
    that a group with no members keeps nothing.
- **Observability.** `System.Diagnostics.Metrics` counters: connections, subscriptions,
  sets, coalesced sets, deliveries, signals handed on. Log lines for connect and
  disconnect.
