# Epic 1: First release

**Status:** the plan and its contract were approved by the owner on 18 November 2026 (O4);
S1 to S4 are done; S5 is next.

## Goal

The library as the [concept](../concept.md) describes it, published on nuget.org as its
first version: a host embedded in one process that keeps remote signals with consumer
groups in memory, a client for other processes over one TCP connection, the same
`ISignals` interface for code inside the host process, and a thin package that registers
either of them in a .NET generic host.

It is for applications whose workers keep their work in durable storage and need only a
nudge to look now: a publisher that learns at once whether anybody listens, and workers
that never miss the last set, never wake more than one member of a group on a key and know
within seconds whether the host is alive.

## Scope

**Included.** Everything sections 2 to 6 and 11 of the concept describe for v1: the rules
of section 3, liveness, the embedded host and its in-process `ISignals`, the remote client
with reconnection, the line protocol with its version in `HELLO`, the shared token and the
configurable bind address (D9), metrics and log lines, the hosting package (D10), the
samples, the measurement program for the performance targets, the explanations of
decision [O3](#decisions-of-the-owner-18-november-2026), and the first release (D15).

**Left out**, as the concept's section 9 says: payloads, durable state, clustering or
failover, ordering, exactly-once delivery, locks or leases, browser clients, TLS, a
standalone server binary (a sample shows one), clients in other languages, a signal that
stays pending for all members, watching a group's presence. Also left out, because nothing
needs them now: logging inside `Moltaro.Signals` (it has no package dependency; the
hosting package writes the log lines), overloads that bind options from `IConfiguration`,
health checks, a benchmark package. Left out by D9, which puts the library on an internal
network behind a shared token: resistance to denial of service beyond the limits the
options state (decision O5).

## Decisions of the owner, 18 November 2026

| # | Decision |
| --- | --- |
| O1 | **Standing permission for this epic.** The coordinator has, for this epic, the standing permission that [branches and releases](branches.md#who-does-what), "Who does what", describes, within the limits it names there |
| O2 | **Few sprints, the contract first.** The library is small, so the epic is not split into many sprints. The plan comes first, and with it the contract: the whole public interface of both packages is written out exactly in this plan, every public type and member with its signature and what it guarantees, because it is meant to stay. Later it changes only in individual cases, through the owner. The plan therefore has one contract stage that covers the entire public surface, and a sprint is split only where one agent and one review could not carry it |
| O3 | **Explanations in two forms.** The documentation that explains the architecture and how the library works, small as it is, is written both as Markdown and as HTML. In the main cases the HTML version is animated, so that a person can easily watch how it works. The rule this gives is in `AGENTS.md`, "Documentation"; requirement R15 names the explanations of this epic |
| O4 | **Every contract is agreed with the owner before it is committed**: the public contract in this plan, any change of it, and the contract a sprint puts into code; narrowed by O12 |
| O5 | **A refusal is one rule, and flood resistance is out of scope.** A refusal that closes is answered with the `ERR` line, then the connection is closed normally, with no closing phase, draining or bound beyond what the options state. Refusals raise no notification and write no log line. Resistance to denial of service beyond the stated limits is out of scope for v1, by D9 |
| O6 | **Remove everything that is not needed.** No `Refused` state, no name-length option, no refusal exception, no host connection event, no refusal counter, no ordered sids, no allocation assertions, and the smaller cuts the contract below reflects |
| O7 | **Hierarchical keys, ancestors only.** A key is a path of segments separated by `.`. A set of a key also sets each of its ancestors: a set of `entity.contacts.42` acts as a set of `entity.contacts.42`, `entity.contacts` and `entity`, each by rule 1, for every (key, group) among these keys that has members. No wildcards, no setting of descendants; anything more is a separate decision later |
| O8 | **A refactoring sprint in the middle of every epic**, on the refactoring track of the [development process](process.md#a-refactoring-sprint); in this epic S4, after S3 |
| O9 | **Three contract wordings.** The rules act per (key, group); an attempt that ends without the host's answer to its `HELLO` raises no `StateChanged`; the client logs a refused attempt in its Warning line, with no Error line |
| O10 | **O1 covers this epic's documents too.** The standing permission of O1 also covers the pull requests of this epic's documents — the plan, the contracts of later stages, light-track changes — once they are reviewed and, where the process asks for it, approved by the owner. It never covers the release change, which stays with the owner, or what O1 excludes |
| O11 | **The one term is signal.** A set gives each group with members on a key a *signal*; one that no member has taken yet is *pending* (at most one per (key, group)); a busy member that leaves *hands its signal on*. "Event" for that state, "raised", "re-raise" and "auto-reset event" leave the documentation, the contract and the code |
| O12 | **Narrows O4.** The owner agrees only the public contract before it is committed, and any change of it: the public API of both packages, the protocol (`docs/protocol.md`), the metric names and the log categories. Internal contracts — the types the tests address that are not public — are approved by the coordinator after the independent review. The owner arbitrates what the coordinator cannot settle and decides any change of a guarantee or a decision of the concept |
| O13 | **CI also runs on Linux ARM64** (19 November 2026), because the memory ordering of x64 hides faults of the reads the library makes without a lock. The job is not a required check of `main`; like every job, it must pass before a sprint is merged |

## Requirements

Levels are those of the [work README](README.md). In this epic **Release** is part of the
acceptance: the level only says that the sprints before it can be developed and tested
without it. "Today" is the state on 18 November 2026: no code exists.

| # | Requirement | From | Level | Today | Sprints |
| --- | --- | --- | --- | --- | --- |
| R1 | One `ISignals` contract for code in the host process and in other processes, with the same semantics; application code does not branch on where the host is | Principle 6; concept §2, §5 | Must | Absent | S1, S2, S5 |
| R2 | The rules 1–7 of concept §3, held by the host's signal delivery, per (key, group), with a set of a key also setting its ancestors (O7). A group with no members on a key keeps no state for it; memory grows only with keys × groups and subscriptions | Concept §3, D1–D5 | Must | Absent | S2 |
| R3 | A wait that ends by its deadline or by cancellation never loses a signal that was handed to it and never turns it into a second due pass; a busy member that leaves hands its signal to a member that waits | Principle 3; rules 1, 5, 7; [PQ1](#pq1-ending-a-remote-wait) | Must | Absent | S2, S3, S5 |
| R4 | The in-process `ISignals` of a host, without a socket | Concept §5 | Must | Absent | S2 |
| R5 | The host over TCP: bind address and port configurable, port 0 allowed and the bound endpoint known; `HELLO` with the protocol version and the shared token; refusals with a code; a protocol violation closes the connection with a reason; the limits of concept §6 as options | Concept §6, D9, D13; principle 9 | Must | Absent | S3 |
| R6 | The remote client: one persistent connection; a set never waits for a connection, never queues and answers `Unavailable` at once while not connected (the wording of [PQ3](#pq3-sentences-of-the-concept-and-of-agentsmd-that-the-contract-makes-untrue), rows e to g); subscriptions survive a reconnect and return `Joined` | Concept §4, D5, D8; principle 2 | Must | Absent | S5 |
| R7 | Liveness: `PING`/`PONG`, a dead peer detected on both sides, the state and its change notification, a refused `HELLO` reported with its code, reconnection with backoff | Concept §4, D7; principle 5 | Must | Absent | S3, S5 |
| R8 | Every timeout and limit an application can observe is an option with a default, validated when the host or client is created; time comes from `TimeProvider` | Principle 8 | Must | Absent | S1, S2, S3, S5 |
| R9 | The protocol is written exactly in `docs/protocol.md`, with its version | `AGENTS.md`, "The Contract Comes First" | Must | Absent | S3 |
| R10 | The hosting package: registration of the host or the client in a generic host and a container; the host registered first binds its port before later hosted services start; connect and disconnect log lines | Concept §5, §11, D10 | Must | Absent | S1, S6 |
| R11 | Metrics through `System.Diagnostics.Metrics`: connections, subscriptions, sets, coalesced sets, deliveries, signals handed on | Concept §11 | Release | Absent | S2, S3 |
| R12 | The samples project shows intended use, includes a standalone host of about 30 lines and runs to the end with `dotnet run` | Concept §5; `AGENTS.md` | Must | Absent | S1, S6 |
| R13 | Tests as concept §11 and `AGENTS.md` require: a deterministic test of signal delivery for every rule of §3, unit tests of the codec, loopback TCP tests of connect, heartbeat loss, host restart, leaving while busy, bad input and limits; a fake clock; no sleeps | Concept §11; `AGENTS.md` | Must | Absent | S1–S6 |
| R14 | The measurement program `Moltaro.Signals.Measurements` for the targets of concept §11, with the figures recorded under [Measured results](#measured-results) | Concept §11; principle 11 | Release | Absent | S2, S3, S5, S6 |
| R15 | The explanations of decision O3: E1 "How a set wakes one member of each group on a key" (animated), E2 "Why nothing is lost when a connection drops or the host restarts" (animated), E3 "How the library is built" (a still figure); each in `docs/explanations` as Markdown and HTML | O3 | Release | Absent | S2, S5, S6 |
| R16 | Two packages: `Moltaro.Signals` with no package dependency, `Moltaro.Signals.Hosting` depending only on it and on the hosting abstractions of .NET; `dotnet pack` produces exactly these two; published from a version tag through nuget.org Trusted Publishing | D10, D15; principle 10 | Release | The workflows exist; no package | S1, S6 |
| R17 | `README.md`, the concept, `docs/protocol.md` and the explanations say what exists after every sprint; the sentences of [PQ3](#pq3-sentences-of-the-concept-and-of-agentsmd-that-the-contract-makes-untrue) are edited as the owner answered | `AGENTS.md`, "Documentation" | Release | Holds: nothing exists and they say so | S1–S6 |
| R18 | The public contract of this plan in code, every public member with its XML documentation | O2; `AGENTS.md` | Must | Absent | S1 |

## Acceptance

The epic is done when all of the following hold.

1. Every requirement above is delivered. No entry point throws `NotSupportedException`
   and no test is skipped.
2. The tests written in S1 pass with their assertions unchanged, and so do the tests
   written in every later sprint, except corrections of a wrong test made by the rule of
   the [development process](process.md#a-sprint-on-the-full-track), each with its reason.
3. The public surface in code is the one written in [The public contract](#the-public-contract),
   with the changes the owner agreed to during the epic and nothing else.
4. `dotnet build` and `dotnet test` pass from `src` on both CI jobs, and
   `dotnet format --verify-no-changes` on the `build` job. The samples run to the end, and
   the code in `README.md` is the code of the samples.
5. Every figure of [Measured results](#measured-results) is recorded on the final build,
   and every target of concept §11 is met, or its miss was handled by the rule of the
   [development process](process.md#when-to-stop-and-ask-the-owner) and decided by the
   owner.
6. The three explanations exist in both forms, agree with each other and with what was
   delivered, and their status lines say "built".
7. Everything [The first release](#the-first-release) lists holds, so that the owner can
   tag the release.

### Measured results

Filled by the coordinator when a sprint closes. The program prints the processor, the
operating system and the .NET version with every figure, never a host name; raw output is
not committed. S6 moves the final figures into `README.md`.

| Sprint | Scenario | Figure | Target | Machine | Date |
| --- | --- | --- | --- | --- | --- |
| S2 | In-process set to wake, 100,000 wakes | p50 1.5 µs, p99 3.4 µs, maximum 176 µs | p50 under 50 µs: met | Intel Core i9-13900H, 20 logical processors, Windows 10.0.26300, .NET 10.0.15, Release | 18 November 2026 |
| S2 | Allocated per wake of a waiting member | 96 bytes | Reported | The same | 18 November 2026 |
| S2 | Sets per second on one thread, mostly coalesced | 10.6 million | At least 100,000: met | The same | 18 November 2026 |
| S2 | Retained per (key, group), without subscriptions / per subscription | 190 / 142 bytes | Reported | The same | 18 November 2026 |
| S2 | Change of retained memory over 1,000,000 sets | 0 bytes | Flat: met | The same | 18 November 2026 |
| S2 | Allocated per wait, without deadline or token / with both | 96 / 232 bytes | Reported | The same | 18 November 2026 |
| S2 | Set of a key of one / three segments, members on one level | 181 / 328 ns, 0 bytes allocated | Reported | The same | 18 November 2026 |
| S2 | Sets per second on 2 / 4 / 8 threads, in total | 3.3 / 2.4 / 2.8 million | Reported | The same | 18 November 2026 |
| S3 | `SET` lines applied per second from one connection, pipelined over loopback | 0.73 million | Reported | The same | 18 November 2026 |
| S3 | Allocated per `SET` over loopback | 0.7 bytes | Reported | The same | 18 November 2026 |
| S3 | In-process set to wake, again on the build of S3 | p50 1.4 µs, p99 2.4 µs | p50 under 50 µs: met | The same | 18 November 2026 |
| S5 | Loopback set to wake, from one client to a member on another, 50,000 wakes | p50 41 µs, p99 100 µs, maximum 1.1 ms | p99 under 1 ms: met | The same | 19 November 2026 |
| S5 | Allocated per remote wake, with its set and the next wait, the host's included | 768 bytes | Reported | The same | 19 November 2026 |
| S5 | Sets per second from one client with 1 / 8 / 64 concurrent callers | 21,000 / 63,000 / 521,000 | Reported | The same | 19 November 2026 |
| S5 | Allocated per remote set with 1 / 8 / 64 callers | 384 / 164 / 106 bytes | Reported | The same | 19 November 2026 |

Rates and latencies on one laptop with mixed core types vary between runs, by a factor of
five in the review of S2; every judged target was met in every run. The allocation and
memory figures do not vary.

## Open questions

None is open. The questions of this plan are numbered PQ1, PQ2, … so that they are not
taken for the entries Q1, Q2, … of the [open questions](open-questions.md) register. Each
answer is written where it outlives this plan, which is removed after the release.

### Answered

#### PQ1. Ending a remote wait

**Answered by the owner, 18 November 2026: option (a).** The verb `UNWAIT <sid>`, answered
by the host; `WaitAsync` completes after that answer and returns `Signaled` if the signal
came first. The contract below states it, S3 builds it, and PQ3, row d, brings it into the
concept. Without it, a member that returned at its deadline would stay armed on the host,
and two passes could be due for one member, which rule 5 excludes.

#### PQ2. How the performance targets of concept §11 are judged

**Answered by the owner, 18 November 2026.**

- The in-process set to wake, under 50 µs, is judged at p50; p99 and the maximum are
  reported.
- The 100,000 sets per second on one core are judged in-process: one thread calling
  `SetAsync` on ten keys, each with one group whose member wakes for at most one set in a
  hundred. The rate over loopback is reported, not judged.
- The loopback set to wake is judged at p99 under 1 ms, with the host and its clients in
  one process.
- Memory is reported as the bytes retained per (key, group) and per subscription, and a
  test of signal delivery asserts that a group with no members keeps nothing.

The documents step of S2 writes this into concept §11, next to the targets.

#### PQ3. Sentences of the concept and of `AGENTS.md` that the contract makes untrue

**Answered by the owner, 18 November 2026: every row as proposed.** The sprint named makes
each edit in its documents step, in the same change as the code that follows it
(`docs/README.md`).

| # | Where | Now | New | Why | Edited by |
| --- | --- | --- | --- | --- | --- |
| a | Concept §3, rule 6 | "The first Wait after subscribing or after a reconnect returns at once with `Joined`" | "The first Wait after subscribing or after a reconnect returns `Joined` as soon as the host has opened the subscription; at once when it is open" | A `Joined` returned before the host counts the member lets a set made during the pass go unremembered (rule 3): that set is lost | S5 |
| b | Concept §3, rule 7 | "the host raises its group's event again" | "the host hands its signal on to its group on its key, as rule 1 does for that one (key, group) without its ancestors: a waiting member receives it, otherwise it becomes pending" | A signal handed on while another member of the group on that key waits must reach that member | Done on the branch of S2, in the words of O11 |
| c | Concept §3, the consumer loop and the publisher | `await using var lane = await signals.SubscribeAsync("jobs", "workers", ct);` and the comments `result.Groups == 0` and `result is Unavailable` | `using var lane = signals.Subscribe("jobs", "workers");` and the comments "`Applied` with `Groups` 0" and "`Status` is `Unavailable`" | The examples have to compile against the contract | S1 |
| d | Concept §6, the client verbs | No verb ends a wait | A row `UNWAIT <sid>`: "End the wait of the subscription; answered after any `SIG` already sent" | PQ1 | S3 |
| e | `AGENTS.md`, principle 2 | "Set is idempotent and never waits for the network." | "Set is idempotent and never waits for a connection. A set while a signal is pending does nothing. A client that is not connected answers `Unavailable` at once; it never blocks and never queues a set for later. A connected client waits only for the host's answer, which carries the group count; that wait ends at the latest when a silent connection is declared dead." | A connected client waits for the host's answer, which carries the group count (D8) | Done in S1, in the words of O11 |
| f | Concept D8 | "never blocks", because "callers answer immediately instead of waiting for timeouts" | "Set returns the live group count; `Unavailable` at once when not connected; it never waits for a connection, and a connected client waits only for the host's answer, bounded by the dead-peer time", because "callers answer at once instead of waiting for a host that is gone" | The same | S5 |
| g | `README.md`, "What it is" | "a set never blocks" | "a set never waits for a connection" | The same | S5 |
| h | Concept §3, the terms "Key" and "Group", rules 1, 2 and 3; D2; D3; §9; `README.md`, line 4 and "What it is" | Key: "Opaque to the host". Group: "Members of one group share each signal". Rule 3: "A group with no members keeps no state". `README.md`, line 4: "one waiter per group wakes". Rule 1: "for every group that currently has members on this key". Rule 2: "the number of groups that have at least one live member". D2: "Coalescing happens on the host per (key, group); Set is idempotent". D3: "One waiting member per group receives a signal". `README.md`: "one set wakes exactly one waiting member of a group" | Key: "Name of what changed, e.g. `jobs` or `outbox.mail`; its segments, separated by `.`, make its ancestors (`outbox` for `outbox.mail`)". Group: "Members of one group on a key share each signal of that key". Rule 3: "A group with no members on a key keeps no state for it". `README.md`, line 4: "one waiter per group on a key wakes". Rule 1: "Set(key) acts on the key and on each of its ancestors: for every (key, group) among these keys that currently has members …". Rule 2: "the number of (key, group) pairs over the key and its ancestors that have at least one live member; each counted once". D2 adds: "a set of a key also sets its ancestors". D3: "One waiting member per (key, group) receives a signal". `README.md`: "one set wakes exactly one waiting member of a group on a key". §9, "Possibly later": "wildcards in keys, and setting the descendants of a key" | O7 | Done on the branch of S2, in the words of O11 |

## The public contract

The whole public surface of both packages, as decision O2 requires. S1 puts it into code
exactly as written here; a name, a signature or a behavior that is not here goes back to
the planner and, as a change of the contract, to the owner. Concept §7 is a sketch;
where this contract differs from it, the reason is given below. The sentences of the
concept and of `AGENTS.md` that this contract makes untrue are listed in
[PQ3](#pq3-sentences-of-the-concept-and-of-agentsmd-that-the-contract-makes-untrue). The
words are those of concept §3; a *group* is a consumer group, written `group` in code.

### Rules for every member

- **Thread safety.** Every public member may be called from any thread at any time,
  except where a member says otherwise.
- **Errors.** An argument error (`ArgumentNullException`, `ArgumentException`,
  `ArgumentOutOfRangeException`), `ObjectDisposedException` after a dispose and
  `InvalidOperationException` for a misuse are thrown by the call itself, before anything
  happens. An outcome or a failure of the operation is reported by the returned task:
  `OperationCanceledException`, `InvalidOperationException` for a refused subscription,
  and `ObjectDisposedException` when the dispose happens while the operation waits.
- **Cancellation.** A token that is already cancelled when a method is called gives a
  cancelled task, and the method changes nothing.
- **Completions.** Application code that continues after awaiting a task of this library
  never runs inside another call of the library, and never on the thread that reads a
  connection. A `SetAsync` that wakes a waiting member does not run that member's code:
  the member continues on the thread pool, and the set returns without waiting for it. A
  remote member's pass, however long, never holds up the replies, heartbeats and signals
  of its client's connection. A task that completes synchronously, such as an in-process
  set, returns to its own caller as any call does.
- **Results and exceptions.** Expected outcomes are results (`Unavailable`, zero groups,
  `Joined`, `DeadlineReached`); exceptions are for failures and mistakes. A default value
  of a result never claims a success.
- **Per (key, group).** The rules of concept §3 act on one (key, group) at a time:
  members of one group on a key and on an ancestor of it are separate members with
  separate pending signals, so a set of `a.b` wakes one waiting member of group `g` on
  `a.b` and one on `a`.
- **Names.** A *name* is a key or a group, by one rule for both: 1 to 128 characters, each
  one of `A–Z a–z 0–9 . _ : -`, where `.` separates segments and no segment is empty (not
  `a..b`, `.a` or `a.`). Names are compared exactly, case included. The segments of a key
  make its ancestors (O7); those of a group mean nothing. `null` gives
  `ArgumentNullException`; any other name that breaks the rule gives `ArgumentException`;
  `ParamName` is `key` or `group`.
- **Token.** The shared token of `HELLO` (D9): 1 to 256 characters, each from U+0021 to
  U+007E (printable ASCII without the space), compared exactly.
- **Limits.** The protocol fixes the limits of names and of the token, so that both sides
  check the same ones (`AGENTS.md`, principle 8). Each check lives once, in
  [Internal types of S1](#internal-types-of-s1).
- **Options** are plain classes with settable properties and defaults. The constructor of
  `SignalHost` or `SignalClient` validates them and copies them: a later change of the
  options object has no effect. `null` options give `ArgumentNullException`. A number or
  a `TimeSpan` outside its range gives `ArgumentOutOfRangeException`; any other wrong
  value — a required property left `null`, a token that breaks its rule, two options
  that contradict each other — gives `ArgumentException`; `ParamName` is the name of the
  property, for two options the one the table names. A `TimeSpan` option has to be
  greater than zero and at most `TimeSpan.FromMilliseconds(uint.MaxValue - 1)`, the
  longest timer of `TimeProvider`.
- **Time.** Deadlines, heartbeats, dead-peer detection and backoff use the `TimeProvider`
  of the options; tests use a fake one.
- **`ValueTask`.** A returned `ValueTask` is awaited once, as any `ValueTask`.

### Notifications

`ISignals.StateChanged` follows one rule (principle 8):

- A handler runs on a thread-pool thread, never synchronously inside a call that an
  application made to this library, and never while the library holds a lock.
- The notifications of one object are delivered one at a time, in the order of the
  changes. A handler that blocks delays only the later notifications of that object;
  heartbeats, sets and waits go on.
- The arguments carry the state of the change they report; by the time a handler runs,
  `State` may have moved on. A handler that needs the state from the start attaches
  itself, then reads `State`; `Start` comes after both.
- An exception thrown by a handler is not caught by the library: it is an unhandled
  exception of the process.
- After `DisposeAsync` has been called, no notification starts; one that is already
  running may finish. A dispose raises no notification of its own, and it does not wait
  for a handler that is running: a handler may dispose the object that raised it, and a
  handler that blocks does not hold up a dispose.

### Namespace `Moltaro.Signals` (package `Moltaro.Signals`)

#### `ISignals`

```csharp
public interface ISignals
{
    SignalConnectionStateEnum State { get; }

    event EventHandler<SignalConnectionChangedEventArgs>? StateChanged;

    ValueTask<SignalSetResult> SetAsync(string key, CancellationToken cancellationToken = default);

    ISignalSubscription Subscribe(string key, string group);
}
```

The one interface of both transports (principle 6). `SignalHost` implements it for code
inside the host process, `SignalClient` for code in other processes. It is not
disposable: whoever created the object disposes it, a container included.

- **`State`** answers from local memory. For a client: `Connected` once the host accepted
  its `HELLO`, `Disconnected` otherwise, while it tries to connect. For the in-process
  `ISignals` of a host: `Connected` from its construction until `DisposeAsync`,
  `Disconnected` afterwards. A connection whose peer died is reported `Connected` only
  until the dead peer is detected, at most `DeadPeerTimeout` (principle 5).
- **`StateChanged`**, by the rule of [Notifications](#notifications), is raised by a
  client with `Connected` when the host accepts its `HELLO`, and with `Disconnected` when
  a connection ends or the host refuses an attempt with its code. An attempt that ends
  without the host's answer to its `HELLO` raises nothing: one that reaches no host, gets
  no answer within `DeadPeerTimeout`, or sees a plain end of the connection. A dispose
  raises nothing, and the in-process `ISignals` of a host never raises it.
- **`SetAsync(key, cancellationToken)`** sets `key` and each of its ancestors, for every
  (key, group) among these keys that has members (rule 1, O7): a set of `a.b.c` acts as a
  set of `a.b.c`, `a.b` and `a`. It sets no descendant and knows no wildcard. A set while
  a signal is pending does nothing, and coalescing stays per (key, group); sets are never
  accumulated, queued or replayed.
  - *Results.* `Applied` with `Groups`, the number of (key, group) pairs over the key and
    its ancestors that had at least one live member when the host applied the set, each
    counted once (rule 2); 0 means nobody listens.
    `Unavailable` when the set was not confirmed by the host: the client was not
    connected, or its connection was lost before the host's answer. In the second case
    the set may have taken effect; repeating it is harmless (rule 1).
  - *Waiting.* In-process, and on a client that is not connected, the call completes
    synchronously: no network, no queue, no retry. On a connected client it sends the set
    and waits for the host's answer, which `Groups` needs (D8); that wait ends at the
    latest when the connection is declared dead, within `DeadPeerTimeout`. A set never
    waits for a connection to come. How this reads against principle 2 of `AGENTS.md`
    is PQ3, rows e to g.
  - *Errors.* Thrown by the call: `ArgumentNullException` and `ArgumentException` for the
    key, `ObjectDisposedException`. Through the task: `OperationCanceledException` (the
    set may have reached the host); `ObjectDisposedException` when the client is disposed
    while the call waits for the host's answer.
- **`Subscribe(key, group)`** creates one member of `group` on `key` and returns it at
  once (concept §3, "Subscription"). Several subscriptions with the same key and group
  are several members, for example the lanes of one worker. It answers from local
  memory and never fails because a host is unreachable: a remote subscription is opened
  on the host while the client is connected, and opened again after every reconnect; a
  refusal by the host shows in `WaitAsync`. A new subscription is busy until its first
  `WaitAsync`, which returns `Joined` (rule 6). Sets of the key's descendants reach the
  member too, because a set also sets the ancestors of its key (O7). Errors, thrown by
  the call: `ArgumentNullException` and `ArgumentException` for the key or the group,
  `ObjectDisposedException`.

*Why it differs from the sketch.* `Subscribe` is synchronous and its subscription is
`IDisposable`, because neither waits for anything (`AGENTS.md`, "Shape of the public
API"); `StateChanged` carries event arguments so that a log line can say why a
connection ended.

#### `ISignalSubscription`

```csharp
public interface ISignalSubscription : IDisposable
{
    ValueTask<SignalWaitResultEnum> WaitAsync(DateTimeOffset? until, CancellationToken cancellationToken = default);
}
```

One member of a group on one key. Only one `WaitAsync` of a subscription may be pending at
a time.

- **`WaitAsync(until, cancellationToken)`** returns when the member should do one pass.
  `until` is the member's own deadline, an instant compared with the `TimeProvider` of
  the object that created the subscription; `null` means no deadline, and so does a
  deadline further away than the longest timer of `TimeProvider` (about 49.7 days). The
  rules, in this order:
  1. **`Joined`** — if a `Joined` is due and the subscription is open on a host, the call
     returns `Joined` at once. A `Joined` is due after `Subscribe` and after every
     reopening on a new connection, until a `WaitAsync` returns it. A remote subscription
     is open only once the host has answered its `SUB`; until then the call waits as rule
     4 says. It does not return `Joined` before that answer, because only an open member
     makes a set during the pass leave a signal pending (rule 5); a set to a group without
     members is not remembered (rule 3).
  2. **A deadline already passed** — the call returns `DeadlineReached` at once, without
     asking the host, and changes nothing.
  3. **`Signaled`** — otherwise the call asks the host. If a signal is pending for its
     group on its key, the member takes it and the call returns `Signaled` (rule 4);
     otherwise the member waits until a signal is handed to it.
  4. **Deadline and cancellation** — when `until` is reached or the token is cancelled,
     the wait is ended with the host: atomically in-process, and remotely by asking the
     host and waiting for its answer ([PQ1](#pq1-ending-a-remote-wait)). If the host had
     already handed a signal, the call returns `Signaled`, even though cancellation was
     requested. Otherwise it returns `DeadlineReached`, or ends with
     `OperationCanceledException`.
  5. **Not connected** — while the client is not connected, or the subscription is not
     open yet, the call waits until the subscription is open on a host and then returns
     `Joined`. Its deadline ends it at once with `DeadlineReached`, its token at once with
     `OperationCanceledException`; there is no host to ask. A connection lost while the
     member waits leads here: the host ended the member, and handed its signal on if the
     member was busy (rule 7).

  After `Joined` or `Signaled` the member is busy until its next `WaitAsync` (rule 5): a
  set meanwhile leaves a signal pending, so the next call returns at once, and leaving
  meanwhile hands the signal on. After a wait that ended under rule 4 without a signal,
  the member holds no signal: a set leaves a signal pending, and leaving hands nothing
  on. A call that ends under rule 2 or 5, or whose token was already cancelled, leaves
  the member as it was.

  *Handing a signal on* (rule 7) is handled as a set of that one (key, group), without its
  ancestors (rule 1), and the instrument `sets` does not count it: a member of the group
  on that key that waits receives it; otherwise it becomes pending; when the leaving
  member was the last of its group on that key, nothing is kept (rule 3). So a signal is
  never pending while a member of its group on that key waits.

  *Errors.* Thrown by the call: `InvalidOperationException` when another `WaitAsync` of
  this subscription has not completed (that one is not affected);
  `ObjectDisposedException` when the subscription or the object that created it was
  disposed. Through the task: `OperationCanceledException` only when no signal was handed
  to the wait; `ObjectDisposedException` when either is disposed while the call waits;
  `InvalidOperationException` with the host's text when the host refused to open the
  subscription (`MaxSubscriptionsPerConnection`). A refused subscription is closed for
  good: every later `WaitAsync` reports the same exception through its task, and it is
  not opened again after a reconnect.
- **`Dispose()`** ends the membership and returns at once, without waiting for the host.
  If the member is busy, the host hands its signal on (rule 7): in-process at once,
  remotely when it receives the unsubscription or loses the connection. A pending
  `WaitAsync` ends with `ObjectDisposedException`; a signal the host handed to it at that
  moment makes the member busy on the host, so the host hands it on; it is not lost.
  Calling it again does nothing. Disposing the `SignalHost` or `SignalClient` that
  created the subscription ends it in the same way; its `Dispose` then does nothing.

#### `SignalSetResult` and `SignalSetStatusEnum`

```csharp
public readonly record struct SignalSetResult
{
    public SignalSetStatusEnum Status { get; }

    public int Groups { get; }
}

public enum SignalSetStatusEnum
{
    Unavailable = 0,
    Applied = 1,
}
```

- **`Status`**: `Applied` when the host applied the set, `Unavailable` when it was not
  confirmed, as `SetAsync` says.
- **`Groups`**: for `Applied`, the number of (key, group) pairs over the key and its
  ancestors with at least one live member, 0 or more; for `Unavailable`, 0.
- It has no public constructor beyond the parameterless one every struct has, which gives
  `default(SignalSetResult)`: `Unavailable` with 0 groups, so a default never claims a
  success. An application that tests its own code uses a `SignalHost` in-process, which
  needs no socket, instead of a fake result.

#### `SignalWaitResultEnum`

```csharp
public enum SignalWaitResultEnum
{
    Joined = 1,
    Signaled = 2,
    DeadlineReached = 3,
}
```

`Joined`: the first pass after subscribing or after a reconnect (rule 6). `Signaled`: a
signal was handed to the member, or its wait found one pending.
`DeadlineReached`: `until` came first. The value 0 is never returned. Every result is
followed by one full pass of the application; the result only says why.

#### `SignalConnectionStateEnum`

```csharp
public enum SignalConnectionStateEnum
{
    Disconnected = 0,
    Connected = 1,
}
```

As `ISignals.State` describes. While a client is `Disconnected`, its sets return
`Unavailable` and its waits wait.

#### `SignalConnectionChangedEventArgs`

```csharp
public sealed class SignalConnectionChangedEventArgs : EventArgs
{
    public SignalConnectionStateEnum State { get; }

    public string? Reason { get; }
}
```

- **`State`**: the state this notification reports.
- **`Reason`**: `null` for `Connected`. For `Disconnected`, why: when the host refused the
  attempt, its code (`version`, `token`, `connections` or `protocol`), then its text;
  otherwise a sentence such as "closed by the host" or "no traffic for
  `DeadPeerTimeout`". The text after the code is not part of the contract.
- No public constructor.

#### `SignalHostOptions`

```csharp
public sealed class SignalHostOptions
{
    public IPEndPoint? ListenEndPoint { get; set; }

    public string? Token { get; set; }

    public int MaxConnections { get; set; } = 1000;

    public int MaxSubscriptionsPerConnection { get; set; } = 1000;

    public TimeSpan DeadPeerTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
```

| Option | Default | Valid | Meaning |
| --- | --- | --- | --- |
| `ListenEndPoint` | none, required | not `null` | Address and port the host listens on (D9). Port 0 lets the system choose a free port; `SignalHost.LocalEndPoint` then reports it |
| `Token` | none, required | the token rule | The shared token every client sends in `HELLO` |
| `MaxConnections` | 1000 | at least 1 | Client connections at the same time, counted from accept until the socket is closed. One more is answered with `connections` and closed |
| `MaxSubscriptionsPerConnection` | 1000 | at least 1 | Open subscriptions of one connection; one more is refused with `subscriptions`. In-process subscriptions have no limit |
| `DeadPeerTimeout` | 3 s | the `TimeSpan` rule | A connection with no traffic from the client for this long is closed; one that has not sent `HELLO` within it is refused with `protocol` (concept §4, D7). It has to be longer than the clients' `HeartbeatInterval`; the host cannot check this |
| `TimeProvider` | `TimeProvider.System` | not `null` | The clock of in-process deadlines and of dead-peer detection |

#### `SignalHost`

```csharp
public sealed class SignalHost : ISignals, IAsyncDisposable
{
    public const string MeterName = "Moltaro.Signals";

    public SignalHost(SignalHostOptions options);

    public IPEndPoint? LocalEndPoint { get; }

    public SignalConnectionStateEnum State { get; }

    public event EventHandler<SignalConnectionChangedEventArgs>? StateChanged;

    public void Start();

    public ValueTask<SignalSetResult> SetAsync(string key, CancellationToken cancellationToken = default);

    public ISignalSubscription Subscribe(string key, string group);

    public ValueTask DisposeAsync();
}
```

The host of one deployment (D6), embedded in the process that owns the background work.
Several hosts may live in one process; they share nothing (principle 7).

- **Constructor** validates and copies the options and creates its signal delivery in
  memory. The
  in-process `ISignals` works from here on, before `Start`: the port matters only to
  remote clients. Errors: those of the options.
- **`Start()`** binds `ListenEndPoint`, starts accepting clients and returns once the
  socket is bound; binding does not wait, so the method is synchronous. A host is started
  once; after a failed bind it is disposed and a new one created. Errors:
  `SocketException` when the address cannot be bound; `InvalidOperationException` on a
  second call; `ObjectDisposedException`.
- **`LocalEndPoint`** is `null` until `Start` has bound the socket; then the bound
  address with its actual port, also when `ListenEndPoint` asked for port 0. It does not
  change afterwards. Tests and samples connect clients to it.
- **`State`** is `Connected` from the constructor until `DisposeAsync` and `Disconnected`
  afterwards. **`StateChanged`** is never raised.
- **`SetAsync`** and **`Subscribe`** are the in-process `ISignals`: `SetAsync` completes
  synchronously with `Applied`; members of in-process and remote subscriptions are
  members of the same groups. Errors as `ISignals` says.
- **`DisposeAsync()`** stops listening, closes every client connection, and ends the
  in-process side: pending in-process waits end with `ObjectDisposedException`, later
  calls throw it, `State` becomes `Disconnected`. Nothing is kept: the signals are memory
  only (principle 4). Remote clients see their connection end, report `Disconnected`,
  reconnect with backoff, and their members return `Joined` from a host that comes back.
  It completes when the listener and every connection are closed, so the port is free. A
  second call waits for the first and completes with it.
- **`MeterName`**: the name of the `Meter` of every host, for a listener such as
  OpenTelemetry. Each host creates its own `Meter` of that name with the host as its
  scope and disposes it with the host; the instruments are listed under
  [Metrics](#metrics).

#### `SignalClientOptions`

```csharp
public sealed class SignalClientOptions
{
    public EndPoint? HostEndPoint { get; set; }

    public string? Token { get; set; }

    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan DeadPeerTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);

    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
```

| Option | Default | Valid | Meaning |
| --- | --- | --- | --- |
| `HostEndPoint` | none, required | an `IPEndPoint` or a `DnsEndPoint` | The host to connect to. A name is resolved again for every attempt |
| `Token` | none, required | the token rule | Sent in `HELLO` |
| `HeartbeatInterval` | 1 s | the `TimeSpan` rule | The client sends `PING` when it has sent nothing, or received nothing, for this long, and at most one `PING` in this time (concept §4) |
| `DeadPeerTimeout` | 3 s | the `TimeSpan` rule and greater than `HeartbeatInterval` | With nothing received for this long, the connection is declared dead and closed. It also bounds one attempt to connect: TCP connect and `HELLO` together |
| `ReconnectDelay` | 0.2 s | the `TimeSpan` rule | The delay before the first attempt after a lost connection; it doubles after each failed attempt |
| `MaxReconnectDelay` | 2 s | the `TimeSpan` rule and at least `ReconnectDelay` | The longest delay between attempts. A connection the host accepts sets the delay back to `ReconnectDelay` |
| `TimeProvider` | `TimeProvider.System` | not `null` | The clock of deadlines, heartbeats, dead-peer detection and backoff |

#### `SignalClient`

```csharp
public sealed class SignalClient : ISignals, IAsyncDisposable
{
    public SignalClient(SignalClientOptions options);

    public SignalConnectionStateEnum State { get; }

    public event EventHandler<SignalConnectionChangedEventArgs>? StateChanged;

    public void Start();

    public ValueTask<SignalSetResult> SetAsync(string key, CancellationToken cancellationToken = default);

    public ISignalSubscription Subscribe(string key, string group);

    public ValueTask DisposeAsync();
}
```

The `ISignals` of a process other than the host's, over one persistent TCP connection.

- **Constructor** validates and copies the options. It does not connect: `Start` does, so
  that handlers can be attached first.
- **`Start()`** begins connecting in the background and returns at once. The first
  attempt is immediate. An attempt connects, sends `HELLO` with the protocol version and
  the token and ends within `DeadPeerTimeout`. When the host accepts, the client sends the
  reopening of every open subscription and only then reports `Connected`, so a request
  made after the change to `Connected` reaches the host after those subscriptions; each
  subscription is open, and its wait returns `Joined`, once the host answered it.
  `State` is `Connected` before any `Joined` of that connection; the notification of
  `Connected` is not ordered against it. When the host refuses or no host answers, the client stays `Disconnected` and tries again
  with backoff. Errors: `InvalidOperationException` when started before;
  `ObjectDisposedException`.
- **Liveness.** While connected, the client sends `PING` when it has sent nothing, or
  received nothing, for `HeartbeatInterval`, and at most one `PING` in that time; it
  declares the connection dead when it has received nothing for `DeadPeerTimeout`. A
  connection that ends for any reason gives `Disconnected` with its reason, then
  reconnection after `ReconnectDelay`. TCP keepalive is not relied on.
- **Order.** While connected, the requests of the client reach the host in the order in
  which the calls were made.
- **`State`**, **`StateChanged`**, **`SetAsync`**, **`Subscribe`**: as `ISignals` says.
  Concurrent `SetAsync` calls share the one connection; none waits for another.
- **`DisposeAsync()`** stops reconnecting and closes the connection; the host then ends
  this client's members and hands on the signals of those that were busy (rule 7).
  Pending `SetAsync` and `WaitAsync` calls end with `ObjectDisposedException`, later calls
  throw it, `State` becomes `Disconnected`. It completes when the connection is closed
  and the client's background work has ended, except a notification handler that is
  running, which it does not wait for. A second call waits for the first and completes
  with it.

### Namespace `Moltaro.Signals.Hosting` (package `Moltaro.Signals.Hosting`)

#### `SignalServiceCollectionExtensions`

```csharp
public static class SignalServiceCollectionExtensions
{
    public static IServiceCollection AddSignalHost(this IServiceCollection services, Action<SignalHostOptions> configure);

    public static IServiceCollection AddSignalClient(this IServiceCollection services, Action<SignalClientOptions> configure);
}
```

A container serves one `ISignals`: the host of the process, or a client of a host
elsewhere; registering both leaves the last one. `SignalHost` and `SignalClient` are only
`IAsyncDisposable`, so a container that resolved one has to be disposed asynchronously,
as the generic host does.

- **`AddSignalHost(services, configure)`** registers `SignalHost` as a singleton, whose
  options `configure` fills when the host is first resolved; `ISignals` as that same
  host; and a hosted service whose `StartAsync` resolves the host, calls `Start` and
  writes the host's log line. Registered before the other hosted services of the
  process, the port is therefore bound before they start (concept §5); a port that
  cannot be bound fails the start of the application. The order matters only for when
  the port is bound: the in-process `ISignals` works from the host's construction. The
  hosted service does nothing when the application stops: the container disposes the
  host after every hosted service has stopped, so workers of the process keep their host
  until they are done. Returns `services`.
- **`AddSignalClient(services, configure)`** registers `SignalClient` as a singleton
  configured the same way, `ISignals` as that client, and a hosted service whose
  `StartAsync` resolves the client, attaches its log lines and calls `Start`. The
  container disposes the client. Returns `services`.
- *Errors* of both, thrown by the call: `ArgumentNullException` for `services` or
  `configure`. Wrong options throw when the object is resolved, as its constructor says.

#### Log lines

Written by the hosted services through the container's `ILoggerFactory`, with the
category of the type that reports. The named values are structured properties; the
message text is not part of the contract. The connect and disconnect lines of concept §11
are the client's; the host writes one line. S6, which makes them work, writes this table
into `README.md`, next to the metrics.

| Category | Level | When | Values |
| --- | --- | --- | --- |
| `Moltaro.Signals.SignalHost` | Information | The host listens | `LocalEndPoint` |
| `Moltaro.Signals.SignalClient` | Information | Connected to the host | `HostEndPoint` |
| `Moltaro.Signals.SignalClient` | Warning | The connection to the host ended, or the host refused an attempt; then `Reason` begins with its code | `HostEndPoint`, `Reason` |

`Moltaro.Signals` itself writes no log: it has no package dependency (D10, principle 10),
and the notifications carry what a line needs.

### Metrics

One `Meter` per host, named `SignalHost.MeterName` (`Moltaro.Signals`), with the host as
its scope. The instruments carry no tags. This table outlives the plan in `README.md`:
the sprint that makes an instrument work adds its row there: S2 `subscriptions`, `sets`,
`coalesced`, `deliveries` and `handed_on`, S3 `connections`.

| Instrument | Kind | Unit | Counts |
| --- | --- | --- | --- |
| `moltaro.signals.connections` | `UpDownCounter<long>` | `{connection}` | Client connections whose `HELLO` was accepted and that are open; a connection leaves it when its socket is closed |
| `moltaro.signals.subscriptions` | `UpDownCounter<long>` | `{subscription}` | Subscriptions open on the host, in-process and remote |
| `moltaro.signals.sets` | `Counter<long>` | `{set}` | Sets the host applied, in-process and remote |
| `moltaro.signals.coalesced` | `Counter<long>` | `{signal}` | Once per (key, group), ancestors included: a set that found a signal already pending. A signal handed on is not counted here |
| `moltaro.signals.deliveries` | `Counter<long>` | `{signal}` | Signals handed to a member: by a set or a signal handed on to a waiting member, or a pending signal taken by a wait. `Joined` is not counted |
| `moltaro.signals.handed_on` | `Counter<long>` | `{signal}` | Signals handed on because a busy member left while its group on that key kept at least one member: to a waiting member, or as a pending signal. A busy member that was the last of its group on that key hands nothing on (rule 3) and is not counted |

### Internal types of S1

The rules of names, of the token and of the range of a `TimeSpan`, which `SignalHost` and
`SignalClient` share, live once (`AGENTS.md`, "No duplicated checks"); a one-line `null`
check of an option stays in each constructor. Two internal static classes, because the
name and token rules are the protocol's, which the codec of S3 uses as well, and the range
of a `TimeSpan` is not:

```csharp
// src/Moltaro.Signals/Protocol/ProtocolRules.cs
namespace Moltaro.Signals.Protocol;

internal static class ProtocolRules
{
    internal const int MaxNameLength = 128;

    internal const int MaxTokenLength = 256;

    internal static bool IsName(ReadOnlySpan<char> value);

    internal static bool IsToken(ReadOnlySpan<char> value);
}

// src/Moltaro.Signals/SignalChecks.cs
namespace Moltaro.Signals;

internal static class SignalChecks
{
    internal static readonly TimeSpan LongestTimer = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    internal static void CheckName(string? name, string paramName);

    internal static void CheckToken(string? token, string paramName);

    internal static void CheckTimeSpan(TimeSpan value, string paramName);
}
```

- **`ProtocolRules`** states the rules of names and of the token as the protocol fixes
  them. `IsName` is true for 1 to `MaxNameLength` characters of the name rule with no
  empty segment; `IsToken` for 1 to `MaxTokenLength` characters from U+0021 to U+007E.
  It knows nothing of signal delivery or sockets.
- **`SignalChecks`** holds the checks of the public members. `CheckName` throws
  `ArgumentNullException` for `null` and `ArgumentException` when `IsName` is false;
  `CheckToken` throws `ArgumentException` for `null` or when `IsToken` is false;
  `CheckTimeSpan` throws `ArgumentOutOfRangeException` for a value not greater than
  zero or greater than `LongestTimer`. Each takes the `ParamName` to report.
  `LongestTimer` is also the bound beyond which a deadline of `WaitAsync` counts as none.

### What S1 puts into code

S1 writes every type and member above with its XML documentation, and the checks with
them. Until the sprint that implements a member, it behaves as follows; everything else
throws `NotSupportedException`.

- The constructors of `SignalHost` and `SignalClient` validate the options and succeed.
- `Subscribe` and `SetAsync` check their names, then whether the object was disposed,
  and then throw `NotSupportedException` from the call.
- `State` returns `Disconnected`; `LocalEndPoint` returns `null`; the event accepts
  handlers and raises nothing; `DisposeAsync` completes.
- `AddSignalHost` and `AddSignalClient` validate their arguments, then throw
  `NotSupportedException`.

## The protocol

The protocol is part of the contract (`AGENTS.md`, "The Contract Comes First"). Its exact
grammar is written in `docs/protocol.md` by S3 after its study. What is fixed now, from
concept §6 and the contract above:

| Topic | Fixed now |
| --- | --- |
| Text | ASCII, which is UTF-8; one command or reply per line, ended by `\r\n`; debuggable with `telnet` or `nc` |
| Version | `1`, sent in `HELLO`. A host speaks one version and refuses another with `version` |
| Client → host | `HELLO <protocol-version> <token>`, `SUB <sid> <key> <group>`, `WAIT <sid>`, `UNWAIT <sid>` ([PQ1](#pq1-ending-a-remote-wait), answered by the owner), `UNSUB <sid>`, `SET <rid> <key>`, `PING`. `<sid>` is a number the client chooses for a subscription, `<rid>` a request id |
| Host → client | `OK <rid> [<groups>]` and `ERR <rid> <code> <text>` as replies, `SIG <sid>` and `PONG` |
| Replies | Every reply names the request it answers; pushes are told apart by their verb. `HELLO`, `SUB`, `UNWAIT` and `SET` are answered; `WAIT` is answered only by a `SIG`; `PING` by `PONG` |
| Subscription ids | A client never uses a `<sid>` twice on one connection: it takes them from a counter. The host ignores `UNSUB`, `WAIT` and `UNWAIT` for a `<sid>` that is not open, so an `UNSUB` sent before its `SUB` is answered is safe, and the client ignores a `SIG` for a `<sid>` it has closed: the host handed that signal on when it removed the member |
| Order | The first line is `HELLO`; nothing else is accepted before its `OK`, and a correct client sends nothing else, `PING` included, until its `HELLO` is answered. The host handles the lines of one connection in order, and writes what it sends on one connection in the order of what causes it, so a `SIG` written before an answer is read before it |
| A member on the host | Busy once its `SUB` is answered (`Joined` counts as a signal, rule 6). `WAIT` arms it; `SIG` makes it busy; `UNWAIT` makes an armed member idle and leaves a busy one busy; `UNSUB` or the end of its connection removes it and, if it was busy, hands its signal on as a set of that one (key, group) does, without its ancestors (rules 1 and 7). A signal is never pending while a member of its group on that key waits |
| Names, token | Names of 1 to 128 characters from `A–Z a–z 0–9 . _ : -`, with no empty segment between the `.`s; a token of 1 to 256 characters from U+0021 to U+007E; both limits fixed by the protocol. A `SET` of a key also sets its ancestors (O7); the line does not change. The line length bound follows from them |
| Limits, as options | `MaxConnections`, `MaxSubscriptionsPerConnection`, `DeadPeerTimeout` of the host; `HeartbeatInterval`, `DeadPeerTimeout`, `ReconnectDelay`, `MaxReconnectDelay` of the client |
| Liveness | The client sends `PING` when it has sent nothing, or received nothing, for `HeartbeatInterval`, and at most one `PING` in that time; the host answers `PONG`. Each side closes a connection on which it received nothing for its `DeadPeerTimeout` |
| Refusals that close | For `version`, `token`, `connections` and `protocol` the host writes the `ERR` line, after which the connection takes no more output; it removes the connection's members, handing on the signals of busy ones (rule 7), and closes the connection normally. After `version` or `token` the host has read the `HELLO` and a correct client has sent nothing more, so the client reads the `ERR`. A connection refused with `connections` is answered before its `HELLO` is read, so a reset can discard the `ERR`, and that client may see a plain end of the connection instead, which raises no `StateChanged`; either way it stays `Disconnected` and tries again with backoff. Resistance to floods of connections is out of scope (O5) |

The error codes, all fixed now:

| Code | When | The connection |
| --- | --- | --- |
| `version` | `HELLO` with a protocol version the host does not speak | Closed |
| `token` | `HELLO` with another token | Closed |
| `connections` | A connection accepted while `MaxConnections` connections are open. The host answers at once, before reading `HELLO` | Closed |
| `protocol` | A line that breaks the grammar or is too long; a line before `HELLO`; a `SUB` for a `<sid>` that is open; a `WAIT` for an armed member; a name that breaks the name rule; no `HELLO` within `DeadPeerTimeout` | Closed, with the text as the reason. A correct client never causes it |
| `subscriptions` | A `SUB` beyond `MaxSubscriptionsPerConnection` | Stays open. The subscription's `WaitAsync` reports `InvalidOperationException` through its task |

**Fixed by S3 after its study**, in [`docs/protocol.md`](../protocol.md), version 1: the exact grammar of every line;
the form and range of `<sid>` and `<rid>`; how the answers to `SUB` and `UNWAIT` name the
request they answer; whether `UNSUB` is answered; what the `OK` of `HELLO` carries;
the form of `<text>`; whether the host writes anything before it closes a silent
connection; the case of verbs; and example sessions that can be typed into `nc`.

**How it reaches the owner.** After the study the planner writes the grammar into
`docs/protocol.md` on the branch of S3, uncommitted, together with the internal contract
of the codec in the brief of S3. An independent reviewer reviews both until no P1, P2 or
P3 finding is left, and the owner approves them; they are committed in the contract step
of S3, as its brief says. A study that shows that something of the tables above, or a
public member, should change does not change it: the planner writes the change with its
reason, and it goes to the owner as a change of the contract.

## Sprints

### Rules for every sprint

- The [development process](process.md) applies. S4 is on the refactoring track (O8).
  Every other sprint is on the full track, because each delivers behavior of the public
  contract: contract, then tests by another agent, then implementation, each its own
  commit. S1 has no implementation step; S5, unless its study gives it internal types,
  and S6 have no contract step. Their briefs say so.
- One sprint at a time, on a branch `epic-1/sprint-NN-name` cut from `main`, as
  [branches and releases](branches.md) says. Decision O1 covers the push, the pull request
  and the merge of a closed sprint.
- Every document under **To read** is read at its state of 18 November 2026 with the edits
  this plan prescribes, and this plan at the state the owner approved. `docs/protocol.md`
  is read at the state S3 committed. If a decision or a rule in one of them changes, the
  coordinator stops and has the plan corrected, as the process says. The edits this plan
  prescribes are no such change: the corrections of status and names in a documents step,
  the edits of
  [PQ3](#pq3-sentences-of-the-concept-and-of-agentsmd-that-the-contract-makes-untrue) as
  the owner answered, and the corrections S1 makes.
- **The public contract is fixed by this plan.** No sprint adds, renames or changes a
  public member. A sprint that finds the contract should change stops, and the change
  goes to the owner.
- **Internal contracts after the study.** S2, S3 and S5 build mechanisms and begin with
  the study that the [development process](process.md#a-study-comes-before-a-design)
  requires, answering the questions their brief names. The internal types their tests
  address are then written into the brief by the planner, with what the study taught;
  an independent reviewer reviews them until no P1, P2 or P3 finding is left, and the
  coordinator approves them before the contract step (O12). The owner sees them only when
  the study shows that the public contract or the protocol has to change. The public
  contract these sprints implement is not reopened by the study.
- **Studies.** The implementer of the sprint carries out its study, before any code.
  Two to four established systems per study, read in their source at pinned
  commits, the licence checked first by the rule of the process; Redis is not opened
  (its current licences are not permissive), Valkey can stand in for it. The report is
  brief, given in the conversation and not written to a file; the planner and the
  coordinator read it before the contract step ends.
- **Tests.** One test lane: `dotnet test` from `src`, no service needed, on both CI jobs.
  The test framework is xUnit.net v3 (`xunit.v3`) on Microsoft.Testing.Platform, which S1
  names in `src/global.json` as `"test": { "runner": "Microsoft.Testing.Platform" }`; the
  fake clock is `FakeTimeProvider` of `Microsoft.Extensions.TimeProvider.Testing`. Loopback
  tests bind port 0 and use `SignalHost.LocalEndPoint`. A test waits for an observed
  event and never sleeps; a test with the fake clock waits until the library has armed its
  timer before it advances time, through a wrapper of the fake clock in
  `Moltaro.Signals.TestSupport` that reports every timer created or changed. A test of a
  behavior that is not implemented yet is skipped with the one shared reason, which lives
  in `Moltaro.Signals.TestSupport` because both test projects use it (`AGENTS.md`).
- **Measurements** run by hand, never in CI and never in the test lane, from the program
  `src/Moltaro.Signals.Measurements`. The test author of S2 creates it in the tests step:
  a console program in `Moltaro.Signals.slnx` with `IsPackable` set to `false`, not a test
  project, adding no package and using the public API. The test authors of S3 and S5 add
  their scenarios in their tests steps, so the implementer never writes the measurement
  that judges its own code. The coordinator runs the program and enters each figure under
  [Measured results](#measured-results).
- **Explanations** (O3, R15). An explanation is documentation, not code or tests. It is
  written on the sprint's branch after the implementation step and before the
  independent review, by one agent the coordinator assigns that has no other role in the
  sprint, so that it describes the rule and not the code its author wrote. The
  independent reviewer of the sprint reviews it with the rest; when a fix changes what it
  describes, the same agent corrects it in that round. Its status line says what is built
  and what is intended.
- **Samples.** S1 writes every sample; a sprint that makes a behavior work makes its
  sample run further. The samples always build and run, reporting where they stop.
- **Documents.** In the documents step of every sprint the coordinator brings
  `README.md`, the concept, `docs/protocol.md` and the explanations that exist up to what
  exists, both forms of an explanation together, as `AGENTS.md` and the process say;
  makes the edits of PQ3 that name the sprint; and writes into `README.md` the instruments
  and log lines the sprint made work, as [Metrics](#metrics) and [Log lines](#log-lines)
  say.

### Order and why these splits

```text
S1 contract ── S2 signal delivery, in-process ── S3 protocol, host over TCP ── S4 refactoring ── S5 remote client ── S6 hosting, release
```

- **S1 alone** is the one contract stage of O2: the whole public surface, its validation,
  the scenario tests of every public behavior and the samples. It implements nothing but
  checks, so its review is a review of the contract.
- **S2 apart from S3**, because they are different zones of `AGENTS.md` with different
  priorities, and signal delivery is where correctness matters most. S2 needs no
  socket: it finishes the in-process `ISignals`, so the rules of §3 are reviewed alone.
- **S3 apart from S5**, because the host side and the client side are each a concurrent
  mechanism with failure modes of their own. S3 tests the host with raw protocol lines;
  S5 tests the client against the finished host. One review over both would be one
  review of the whole remote path, codec, host, client and liveness at once. The codec
  goes with the host: it is the same zone, and the protocol has to be fixed before
  either side is built.
- **S4** is the refactoring sprint of O8, after the literal middle of the epic and before
  the client builds on the codec and the transport.
- **S6** carries the thin hosting package together with the release work: both need the
  finished host and client, and neither is large enough for a sprint of its own.

### Status

| Sprint | Result | Track | Status |
| --- | --- | --- | --- |
| S1 | The public contract, the projects and the test lane | Full | Done, 18 November 2026: 3 review rounds |
| S2 | Signal delivery and the in-process host | Full | Done, 18 November 2026: internal contract approved by the owner after 4 review rounds; sprint review 3 rounds |
| S3 | The protocol, its codec and the host over TCP | Full | Done, 18 November 2026: protocol and internal contract reviewed in 3 rounds and approved by the owner; sprint review 2 rounds |
| S4 | Refactoring of the code base after S3 | Refactoring | Done, 18 November 2026: 1 review round |
| S5 | The remote client: liveness and reconnection | Full | Not started |
| S6 | The hosting package, the samples and the first release | Full | Not started |

### S1 · The public contract, the projects and the test lane

**Track:** full · **Branch:** `epic-1/sprint-01-contract` · **Needs:** the owner's
approval of this plan, with PQ2 and PQ3 answered.

- **Goal.** The whole public contract of both packages exists in code, documented and
  refusing where it is not implemented; the checks that need no implementation work; the
  scenario tests of every public behavior are written and skipped; the samples are
  written; CI runs the first tests.
- **Contract.** Every type and member of [The public contract](#the-public-contract),
  exactly, with XML documentation that says what each member does, guarantees, does not
  do and which errors it reports, and behaving as
  [What S1 puts into code](#what-s1-puts-into-code) says. The checks of options, names,
  the token and the arguments of the hosting methods belong to the contract (`AGENTS.md`,
  "The Contract Comes First") and are written in this step, working. The contract
  author's work is shown to the owner and committed only after the owner approved it
  (O4).
- **Steps and projects.** Each commit leaves the build and `dotnet test` green.
  - *Contract step*, the contract author: `Moltaro.Signals` (packed, no package reference)
    and `Moltaro.Signals.Hosting` (packed, referencing `Moltaro.Signals` and
    `Microsoft.Extensions.Hosting.Abstractions`, whose version goes into
    `Directory.Packages.props`), each with a package description and
    `GenerateDocumentationFile` set, so that the build reports a missing or broken
    documentation comment and the package carries the XML documentation; in
    `Moltaro.Signals.slnx`, with the public types in the namespaces the contract gives
    them, one type per file, and the two types of [Internal types of
    S1](#internal-types-of-s1). The contract step commits the code the owner approved on
    18 November 2026 (O4, O9), as it stands; the build and format are checked again before
    the commit. No test project yet: `dotnet test` finds nothing to run and passes, as it
    does today.
  - *Tests step*, the test author: `Moltaro.Signals.TestSupport` (not packed, no tests),
    which holds the one shared skip reason; `Moltaro.Signals.Tests` and
    `Moltaro.Signals.Hosting.Tests` (not packed); `Moltaro.Signals.Samples` (not packed);
    the runner line in `src/global.json`; the versions of `xunit.v3`,
    `Microsoft.Extensions.TimeProvider.Testing` and `Microsoft.Extensions.Hosting` in
    `Directory.Packages.props`. Each test project has tests that run and pass in this
    commit, the checks of the contract step, because Microsoft.Testing.Platform fails a
    test project in which no test runs.
  - *No implementation step.* What S1 makes work is written with the contract; after the
    tests step the sprint goes to verification and review. The fixes of the review are
    made by the contract author in the contract's files, shown to the owner again before
    they are committed (O4), and by the test author in the tests and samples. A finding
    that changes a check and the test of it lands as one commit with both changes, so that
    the build stays green. The measurement program is created by S2, where it is first
    needed.
- **To read.** [Concept](../concept.md), §3–§7 and §11 (18 November 2026); `AGENTS.md`
  (18 November 2026); [development process](process.md) and
  [branches and releases](branches.md) (18 November 2026); this plan, with the owner's
  answers to its questions.
- **Acceptance.**
  - Tests that pass: every validation rule of the options of the host and of the client;
    the rule of names for keys and groups, in `Subscribe` and `SetAsync` of both, empty
    segments (`a..b`, `.a`, `a.`) included; the
    token rule; the argument checks of `AddSignalHost` and
    `AddSignalClient`. A test of the built `Moltaro.Signals` asserts that it references
    only assemblies of .NET (principle 10).
  - Tests that are written and skipped with the shared reason, one area per folder, each
    a sentence that states a behavior of the contract through the public API:
    - *In-process, un-skipped by S2:* rules 1–7; a member that waits receives the signal
      of a busy member that leaves; the results of `WaitAsync`; its deadline with a fake
      clock; a deadline already passed; cancellation;
      a signal handed before a cancellation; a second concurrent wait; dispose while
      waiting and while busy; dispose of the host; `State`.
    - *The host's socket, un-skipped by S3:* `Start`; `LocalEndPoint` with port 0; a
      second `Start`; `DisposeAsync` freeing the port.
    - *Remote, un-skipped by S5:* between a `SignalHost` and `SignalClient`s over
      loopback: `Unavailable` before a host exists; `Joined` after connecting; one member
      per group on a key across clients; a busy member's signal handed on when its client
      disconnects or unsubscribes; a host disposed and a new one on the same port giving
      `Joined`; a wrong token giving `Disconnected` with `token` at the start of `Reason`;
      a subscription beyond `MaxSubscriptionsPerConnection` whose `WaitAsync` reports
      `InvalidOperationException` through its task; dispose of the client.
    - *Hosting, un-skipped by S6:* `ISignals` resolves to the registered host or client;
      a hosted service registered after `AddSignalHost` finds `LocalEndPoint` bound in
      its `StartAsync`; a port that cannot be bound fails the start of the application;
      the host is disposed after the hosted services stopped; the client connects when the
      application starts; the log lines of [Log lines](#log-lines) with their categories
      and levels. They run in a generic host, or dispose
      the provider with `await using`.

    A folder holds one of these areas, so that a sprint un-skips whole folders.
  - The samples, written by the test author in the tests step: a standalone host of
    about 30 lines; a host process with two lanes of one group and a publisher in-process;
    a host with remote clients over loopback, including a host restart; a generic host
    with `AddSignalHost` and a worker, and one with `AddSignalClient`. Run without
    arguments, they run in order and end; each reports where it stops while its features
    are not implemented. With the argument `host` the standalone host runs until Ctrl+C.
  - `dotnet pack -c Release` from `src` produces exactly the two packages.
  - The documents step: `README.md` says that the contract exists and only the checks
    work; in `AGENTS.md`, "Project", the sentence "no code is written yet", and in
    "Layout", the sentence that the solution is empty, are corrected, and principle 2 is
    edited as the owner answered PQ3, row e; the bullets of the [work
    README](README.md) on the repository as it is set up are corrected; in the concept,
    the examples of §3 are edited as PQ3, row c, says, and the sketch of §7 takes the
    names of the contract.
- **Small changes made on this branch** (`process.md`, "Small changes during a sprint"):
  O10, decided by the owner on 18 November 2026; the notes in the briefs of S2 (a test of
  S1 to tighten) and of S6 (the standalone host as one piece), decided by the coordinator
  from the independent review of S1.
- **Out of scope.** Any behavior beyond the checks; internal types other than those of
  [Internal types of S1](#internal-types-of-s1); the measurement program;
  `docs/protocol.md`; explanations.

### S2 · Signal delivery and the in-process host

**Track:** full · **Branch:** `epic-1/sprint-02-event-state-machine` · **Needs:** S1.

- **Goal.** Code in the host process uses `ISignals` with every rule of concept §3: sets
  coalesce per key and group, one waiting member of each group on each key it sets wakes,
  a member is busy until its next wait, joining returns `Joined`, a busy member that
  leaves hands its signal on, and a deadline or a cancellation never loses a signal.
- **Contract.**
  - *Public, from S1, now working:* the constructor, `State`, `StateChanged`, `SetAsync`,
    `Subscribe` and `DisposeAsync` of `SignalHost` for the in-process side; `WaitAsync` and
    `Dispose` of its subscriptions; the instruments `subscriptions`, `sets`, `coalesced`,
    `deliveries` and `handed_on`. `Start` still refuses.
  - *Internal:* signal delivery in the folder `src/Moltaro.Signals/Delivery`,
    namespace `Moltaro.Signals.Delivery`, one type per file, tested directly through
    `InternalsVisibleTo` for `Moltaro.Signals.Tests`. Written after the study, as
    [Rules for every sprint](#rules-for-every-sprint) says. The contract author's work is
    shown to the owner and committed only after the owner approved it (O4).

    ```csharp
    namespace Moltaro.Signals.Delivery;

    internal sealed class SignalDelivery
    {
        internal int KeyCount { get; }

        internal int GroupCount { get; }

        internal void Open(GroupMember member, string key, string group);

        internal SetOutcome Set(ReadOnlySpan<char> key);

        internal bool Arm(GroupMember member);

        internal bool EndWait(GroupMember member);

        internal CloseOutcome Close(GroupMember member);

        internal GroupMember? CloseAll();
    }

    internal class GroupMember
    {
        internal GroupMemberStateEnum State { get; set; }

        internal GroupMember? NextWoken { get; set; }
    }

    internal enum GroupMemberStateEnum { Busy, Idle, Waiting }

    internal readonly record struct SetOutcome(int Groups, int Coalesced, GroupMember? FirstWoken);

    internal readonly record struct CloseOutcome(GroupMember? Receiver, bool Pending);
    ```

    - **`SignalDelivery`**, one per host, is not thread-safe: the host calls it only under
      its lock. It holds the pending signals and the members per (key, group), and the
      invariant that a signal is never pending while a member of its (key, group) waits. A
      (key, group) without members is not held, nor a key without groups. It performs no
      I/O, awaits nothing, runs no caller code, completes no waiter and knows nothing of
      sockets.
    - **`KeyCount`**, **`GroupCount`**: the keys and the (key, group) pairs it holds.
    - **`Open(member, key, group)`** adds a new member, `Busy` (rule 6). The caller has
      checked the names, for `Set` too.
    - **`Set(key)`** applies a set to the key and to each of its ancestors (rules 1 and 2,
      O7). For each (key, group) held among them: if a member waits, the one that has
      waited longest becomes `Busy` and is chained into `FirstWoken`; otherwise the signal
      becomes pending, and `Coalesced` counts it if one was pending already. `Groups` is
      the number of those (key, group) pairs. `key` is a span because the ancestors are
      its prefixes, looked up without allocating.
    - **`Arm(member)`**, for a member that does not wait: `true` when a signal was
      pending, which the member takes, and the member is `Busy`; `false` when the member
      now waits, last in its queue.
    - **`EndWait(member)`**: `true` when the member waited and is now `Idle`; `false`
      when a signal came first and it is `Busy`, or when it does not wait; then nothing
      changes.
    - **`Close(member)`** removes the member, which is then `Idle`. A `Busy` member hands
      its signal on as a set of its own (key, group) without ancestors (rule 7): to the
      `Receiver`, now `Busy`, when a member waits; otherwise `Pending` is `true`, whether
      or not a signal was pending already. Neither
      when the member was `Idle` or waiting, or the last of its group on that key, so that
      nothing is kept (rule 3). `Close` of a closed member does nothing.
    - **`CloseAll()`** removes every member, each then `Idle`, and returns the chain of
      those that waited, so that the host ends their waits.
    - Both classes have parameterless constructors; `GroupMember` is not sealed. A chain
      ends with `null`, so `FirstWoken` and the result of `CloseAll()` are `null` when
      nobody was woken. The setters are internal to the assembly, as C# has no access
      level for a folder; the comments state who writes them.
    - **`GroupMember`** is one member. The in-process subscription derives from it, and in
      S3 the remote member: one object per subscription, with nothing to look up from the
      delivery to the subscription. Only `SignalDelivery` changes `State`. `NextWoken`
      links the members of an outcome's chain. Only `Set` and `CloseAll` write
      `NextWoken`, for the members they take out of `Waiting`; `Close` leaves it. The host
      reads a member's `NextWoken` before it completes that member's wait: once completed,
      the member may wait and be woken again, which relinks it. The links of the waiting
      queue are fields of the folder, not of this contract.
    - **The host and the delivery.** The host checks its own dispose and the
      subscription's under the lock, in the same section that calls `Open`, `Arm` or
      `Set`, so that `SignalDelivery` never receives a closed member.
- **Study** (done, nothing adapted, `THIRD-PARTY-NOTICES.md` unchanged; the public
  contract is not reopened). Sources at pinned commits: dotnet/runtime v10.0.0
  (`60629d14374c56f1cb51819049ad1fa529307f8d`) and Microsoft.VisualStudio.Threading
  (`6e20009d3a2232208dd067cf820bab331e0ffb45`), both MIT; the NATS server v2.12.9
  (`5919e2bb31ba21cd3f3da4f148f5f5cd44e1faf7`), Apache-2.0. Decisions the implementer and
  the test author share:
  - **FIFO.** The member that has waited longest in its (key, group) receives a signal,
    so that signals spread over the lanes. It is this brief's choice, not a rule of the
    concept: tests do not assert it, and a test with two waiting members does not depend
    on which one wakes.
  - **One `System.Threading.Lock` per host** guards `SignalDelivery`, the wait fields of
    each subscription, and the timer calls `TimeProvider.CreateTimer` and `ITimer.Change`.
    Never under it: completing a wait, `Counter.Add`, registering a cancellation, or any
    other code of the application. The timer calls are the one exception: they are safe
    because the due time is at least 1 ms and a `TimeProvider` does not call back inside
    them; the timer's own lock is taken inside the host's, never the other way round.
  - **Completion after the lock.** The host completes the members an outcome names after
    it released the lock, each wait through its own `TaskCompletionSource` created with
    `RunContinuationsAsynchronously`. A wait that returns at once allocates nothing. Every
    completer uses the `Try` form, so whichever comes first wins; a dispose completes a
    wait that has not completed yet with `ObjectDisposedException`. A woken member is
    `Busy` in `SignalDelivery` either way, so its signal is handed on (rule 7).
  - **One decision point.** A set, a signal handed on, the deadline timer, the
    cancellation callback and a dispose each take the lock and move the member out of
    `Waiting`; only the one that finds it `Waiting` completes the wait, except a dispose,
    which also completes a wait that a set has woken but that has not completed yet.
    Cancellation is registered after `Arm`, outside the lock, and released by the wait
    itself with `Unregister` once its task has completed. Its callback
    acts only on the wait it was registered for: under the lock it compares its own
    `TaskCompletionSource` with the member's current one and does nothing on a mismatch.
  - **Deadlines.** One `ITimer` per subscription, created on its first wait with a
    deadline with the flow of the execution context suppressed; every later wait with a
    deadline arms it with `Change`. The due time is `until`
    minus now, rounded up to whole milliseconds; above `SignalChecks.LongestTimer` it
    counts as none, at most 0 is rule 2. When the timer fires, under the lock: a member
    that waits and whose deadline has come is ended; one that waits with a later
    deadline is re-armed for the rest; otherwise nothing happens. Closing a subscription
    disposes its timer, outside the lock. Limitation: after the host's dispose, a timer
    armed for an earlier wait may fire once and does nothing, and a wait that a set has
    just woken may still return `Signaled`.
  - **Ancestors** through a `Dictionary` with `StringComparer.Ordinal` and its
    `GetAlternateLookup<ReadOnlySpan<char>>()` kept in a field, walked up with
    `LastIndexOf('.')`.
  - **Metrics from the outcomes**, recorded after the lock: `subscriptions` +1 after
    `Open` and −1 after a `Close` of an open member; `sets` one per `Set`; `coalesced` its
    `Coalesced`; `deliveries` the members it woke, an `Arm` that returned `true`, and a
    `Close` with a `Receiver`; `handed_on` a `Close` with a `Receiver` or `Pending`.
  - **`Joined`** is a flag of the subscription, not of `SignalDelivery`.
- **To read.** [Concept](../concept.md), §3, §5, §11 (18 November 2026); `AGENTS.md`,
  principles 1–4, 6–8, 11 and "Tests And Samples"; this plan, "The public contract".
- **Acceptance.**
  - A deterministic test of signal delivery for every rule 1–7 of §3, each named for the
    rule it states; for a wait that ends by deadline or cancellation, in both orders
    against a signal; for a busy member that leaves while another member of its group on
    its key already waits, in that order, which hands the waiting member the signal; for
    an idle member that leaves (nothing handed on); for a group whose last member leaves
    (the delivery keeps nothing for it, and nothing is counted as handed on).
  - Tests of ancestors (O7): a set of a child wakes a waiting member of its ancestor; a set
    of an ancestor wakes no member of a child; two sets of different children coalesce on
    their ancestor; `Groups` counts each (key, group) over the levels once; with one
    group waiting on a key and on its ancestor, a set of the key wakes one member on
    each; a busy member that leaves hands its signal on only on its own key.
  - The in-process tests of S1 pass unskipped, with the fake clock for deadlines.
  - A test that a `SetAsync` which wakes a waiting member returns while the code after
    that member's `await` is still blocked (contract, "Completions").
  - The metrics count as [Metrics](#metrics) says, checked with a `MeterListener` on the
    meter whose scope is the host.
  - *Reported*, by the new program `Moltaro.Signals.Measurements`, its scenarios written
    by the test author: in-process set to wake, p50, p99 and maximum; the bytes allocated
    per wake of a waiting member; sets per second on one thread with most of them
    coalesced; the bytes retained per (key, group) and per subscription, and that they
    stay flat over a million sets; allocations of a wait with a deadline and a cancellable
    token; a set of a key of three segments with members on one level only; and, reported
    only, N = 2, 4 and 8 threads, each running the workload of PQ2 on ten single-segment
    keys of its own (one group per key, whose member wakes for at most one set in a
    hundred) for a fixed time after warm-up, the total rate printed with the processor
    count. A finer lock comes up only if a target of §11 is missed, by the rule of the
    process on a measured fall. The targets are judged as the owner answered PQ2.
  - **Explanation E1**, "How a set wakes one member of each group on a key", in
    `docs/explanations` as Markdown and as animated HTML, written as [Rules for every
    sprint](#rules-for-every-sprint) says: two groups on one key, a set that wakes one
    waiting member of each, a set that leaves a signal pending while a member is busy,
    twenty sets that are one, a wait that takes a pending signal at once, a busy member
    that leaves and hands its signal on to one that waits, and a deadline that meets a
    signal; it may show in one step a set of a child that also sets its ancestor. It links
    to concept §3 as the rule.
  - The documents step writes the answer to PQ2 into concept §11; PQ3, rows b and h, are
    done on this branch with O11. The in-process samples run to the end; `README.md` says
    the in-process host works and lists the instruments of this sprint.
- **A test of S1 to tighten** (from the review of S1). In
  `src/Moltaro.Signals.Tests/Names/NameRuleTests.cs`, the test
  `A_name_that_keeps_the_rule_is_accepted_as_a_key_and_as_a_group` accepts any exception
  except `ArgumentException`, because `Subscribe` refuses until S2. In the tests step, the
  test author of S2 replaces its `Record.Exception` with a plain call and skips the test
  with the shared reason, as a correction of a wrong test (`process.md`), with the reason
  in the commit message; the implementer removes the skip.
- **Small changes made on this branch** (`process.md`, "Small changes during a sprint"):
  the time limit of the CI jobs (Q2 of the [open questions](open-questions.md), the
  coordinator); O11, which edited `docs/concept.md`, `README.md`, `AGENTS.md`,
  `docs/work/process.md` and this plan (the owner); the rule of `AGENTS.md` that internal
  members are commented with `//` (the coordinator, from the contract step of S2); the
  `Outer.Nested.cs` form of a nested type in `AGENTS.md`, "Organization" (the coordinator,
  from the independent review of S2).
- **Out of scope.** Sockets, `Start`, the protocol, the client, the instrument
  `connections`.

### S3 · The protocol, its codec and the host over TCP

**Track:** full · **Branch:** `epic-1/sprint-03-host-over-tcp` · **Needs:** S2.

- **Goal.** A host listens and serves the protocol: a client that speaks it connects with
  its token, subscribes, waits, ends a wait, sets and pings; the host refuses with codes,
  closes on bad input with a reason, closes a silent connection, enforces its limits and
  counts its connections.
- **Contract.**
  - *Public, from S1, now working:* `Start`, `LocalEndPoint`, the remote side of
    `DisposeAsync`, the instrument `connections`.
  - *The protocol:* `docs/protocol.md`, version 1, with `UNWAIT` as the owner answered
    PQ1, written and approved as [The protocol](#the-protocol) says.
  - *Internal:* the codec in the folder `src/Moltaro.Signals/Protocol`, namespace
    `Moltaro.Signals.Protocol`, one type per file, tested directly through
    `InternalsVisibleTo`. Written after the study, as
    [Rules for every sprint](#rules-for-every-sprint) says. The contract author's work,
    with `docs/protocol.md`, is shown to the owner and committed only after the owner
    approved it (O4).

    ```csharp
    namespace Moltaro.Signals.Protocol;

    internal static class ProtocolRules // gains, beside its members of S1:
    {
        internal const int Version = 1;

        internal const int MaxIdLength = 19;

        internal const int MaxTextLength = 128;

        internal const int MaxLineLength = 282;
    }

    internal enum ProtocolErrorEnum { Version, Token, Connections, Protocol, Subscriptions }

    internal enum LineStatusEnum { Incomplete, Complete, Refused }

    internal enum ClientVerbEnum { Hello, Sub, Wait, Unwait, Unsub, Set, Ping }

    internal enum HostVerbEnum { Ok, Err, Sig, Pong }

    internal readonly ref struct ClientLine
    {
        internal static ClientLine Read(ReadOnlySpan<byte> buffer);

        internal LineStatusEnum Status { get; }

        internal int Length { get; }

        internal ClientVerbEnum Verb { get; }

        internal long Id { get; }

        internal ReadOnlySpan<byte> Key { get; }

        internal ReadOnlySpan<byte> Group { get; }

        internal ReadOnlySpan<byte> Token { get; }

        internal ProtocolErrorEnum Error { get; }

        internal string? Reason { get; }
    }

    internal readonly ref struct HostLine
    {
        internal static HostLine Read(ReadOnlySpan<byte> buffer);

        internal LineStatusEnum Status { get; }

        internal int Length { get; }

        internal HostVerbEnum Verb { get; }

        internal long Id { get; }

        internal int? Groups { get; }

        internal ProtocolErrorEnum Error { get; }

        internal ReadOnlySpan<byte> Text { get; }

        internal string? Reason { get; }
    }

    internal static class LineFormatter
    {
        internal static int Ok(Span<byte> destination, long id);

        internal static int Ok(Span<byte> destination, long rid, int groups);

        internal static int Error(Span<byte> destination, long id, ProtocolErrorEnum code, string text);

        internal static int Signal(Span<byte> destination, long sid);

        internal static int Pong(Span<byte> destination);

        internal static int Hello(Span<byte> destination, string token);

        internal static int Sub(Span<byte> destination, long sid, string key, string group);

        internal static int Wait(Span<byte> destination, long sid);

        internal static int Unwait(Span<byte> destination, long sid);

        internal static int Unsub(Span<byte> destination, long sid);

        internal static int Set(Span<byte> destination, long rid, ReadOnlySpan<char> key);

        internal static int Ping(Span<byte> destination);
    }
    ```

    - **`ClientLine.Read(buffer)`**, used by the host, and **`HostLine.Read(buffer)`**,
      used by the client in S5, read the first line of `buffer` where it lies, without
      copying or allocating. `Incomplete` when it holds no `\n` yet and fewer than
      `MaxLineLength` + 2 bytes; `Refused` with `Protocol` for a line that breaks the
      grammar of `docs/protocol.md` or is too long, and with `Version` for a `HELLO` of
      another version, `Reason` holding in both cases the fixed sentence the host writes
      as `<text>`; otherwise `Complete`, with `Length` counting the
      `\r\n`. Only the fields of the verb read are set; `Id` is `0` for `ERR 0` and
      `OK 0`, and `Groups` is set only for the answer to a `SET`. `Read` checks a name by
      widening its bytes onto the stack and calling `ProtocolRules.IsName`; the host widens
      `Key` the same way for `SignalDelivery.Set`, and makes the two strings `Open` takes
      for a `SUB`. What depends on the state of a connection — a line before
      `HELLO`, a `SUB` for an open `<sid>`, a `WAIT` for an armed member, the limits, the
      token — is the host's; so is answering `protocol` to a `Version` refusal of a line
      after `HELLO`. The fixed sentences of those refusals live in the host's transport.
    - **`LineFormatter`** writes one line with its `\r\n` into `destination`, which has
      room for `MaxLineLength` + 2 bytes, and returns its length. It allocates nothing.
      The code names the code word of `docs/protocol.md`; `text` is a fixed sentence of at
      most `MaxTextLength` characters.
    - The codec knows nothing of signal delivery or sockets.
- **Study** (done, ideas only, `THIRD-PARTY-NOTICES.md` unchanged; the public contract and
  the fixed tables of [The protocol](#the-protocol) are not reopened). Sources at pinned
  commits: the NATS server v2.12.9 (`5919e2bb31ba21cd3f3da4f148f5f5cd44e1faf7`,
  Apache-2.0); memcached 1.6.45 (`2d51e364799bc9698bd4b11728ea978cea12da6e`,
  BSD-3-Clause); Kestrel of aspnetcore v10.0.0
  (`7387de91234d3ef751fa50b3d1bfede4130213ff`) and `System.IO.Pipelines` of dotnet/runtime
  v10.0.0 (`60629d14374c56f1cb51819049ad1fa529307f8d`), MIT. Decisions the implementer
  and the test author share:
  - **The grammar** is that of `docs/protocol.md`: one space between fields, verbs
    case-sensitive, lines read where they lie. The id `0` names the connection, as the
    request that `OK 0` and `ERR 0` answer; every `ERR 0` closes.
  - **Where the transport lives.** In nested types of `SignalHost`, in the files
    `SignalHost.Connection.cs` and `SignalHost.RemoteMember.cs`, as
    `InProcessSubscription` is; this replaces the folder `Transport` the plan proposed,
    which would depend on `SignalHost` while `SignalHost` depends on it (principle 10).
    `RemoteMember` derives from `GroupMember`, one per open `<sid>`, and knows its
    connection and its `<sid>`.
  - **Reading.** One reader per connection owns its life: a fixed buffer of 4096 bytes,
    lines read where they lie and only the incomplete tail moved to the front. It stores
    the time of each receive, then handles each complete line in one section under the
    host's lock, together with signal delivery, and appends the reply there. It receives
    again only after the connection's output is sent, so a client that does not read
    leaves the host holding the replies to one receive and at most one `SIG` per armed
    member: a bound from the protocol and `MaxSubscriptionsPerConnection`, with no new
    option.
  - **Writing.** Each connection has two output buffers, one pending and one being sent,
    under the host's lock. A line is appended inside the section that decided it, so the
    output's order is the order of the decisions; a remote member gets its `SIG` in the
    section that woke it, whether that is the walk of `Set`, a `Close` or the end of a
    connection. From the section that ends it, a connection takes no more output; a
    member woken meanwhile is `Busy` and hands its signal on when it is closed. A flush is started under the lock when
    a line goes to a connection with no flush running; it runs on the thread pool, never
    inline: it swaps the buffers under the lock, sends outside it, and ends under the lock
    when nothing is pending. `NoDelay` is on.
  - **Timers.** One `ITimer` per connection from its accept, re-armed as in S2. Before
    `HELLO` its deadline is the accept plus `DeadPeerTimeout`, which gives
    `ERR 0 protocol`; after it, the last receive plus `DeadPeerTimeout`, and a silent
    connection is then closed with nothing written.
  - **Counting.** The host's set of connections, under its lock, is the count of
    `MaxConnections`, from accept until the socket is closed; `DisposeAsync` closes the
    same set, after the lock. One connection beyond it gets `ERR 0 connections` in one
    synchronous send, then a close, both after the lock, with no reader and no timer. The token is compared with
    `CryptographicOperations.FixedTimeEquals`.
  - **The end of a connection** runs once, started by whichever comes first under the
    lock; disposing the socket wakes the reader. Under the lock it takes no more lines and
    appends the `ERR 0` if it is refused. It then closes its members one at a time
    (rule 7): a remote member that receives the signal gets its `SIG` under the lock, an
    in-process one is completed after the lock. Last, after a refusal, it arms the
    connection's timer for `DeadPeerTimeout` and waits until the flush has sent the
    `ERR 0` or the timer fires, then shuts the socket down. Every end then disposes the
    socket, which ends a pending send and receive, and leaves the set. After a dead peer,
    the client's close, a failed receive or send, or the host's dispose, it disposes the
    socket at once. The timer of an ending connection disposes its socket.
- **Changes to the fixed tables of [The protocol](#the-protocol)**, for the owner's
  approval with the contract of S3, from finding 10 of the review of `docs/protocol.md`
  (the coordinator) and its round 2:
  - "Subscription ids" adds that an `UNSUB` sent before its `SUB` is answered is safe,
    since the host ignores `UNSUB` for a `<sid>` that is not open; so S5's `Dispose` needs
    no deferred `UNSUB`.
  - The `protocol` row adds "a `WAIT` for an armed member", which a correct client never
    sends (principle 9).
  - "Refusals that close" puts the `ERR` line first: after it the connection takes no
    more output, and the host removes its members before it closes the connection.
- **Changes to the internal contract of S2**, for the owner's approval with that of S3:
  - **`NextWoken`.** Besides `Set` and `CloseAll`, the host writes it under its lock: it
    walks a chain there, answers the remote members with their `SIG`, and relinks the
    in-process members into a chain of their own, which it completes after the lock. A
    remote member answered under the lock can be woken again on another connection
    before a walk after the lock would reach it; that walk would skip the members behind
    it.
  - **Under the lock** the host may also append a line to a connection's output and start
    its flush, which runs on the thread pool; the send itself stays outside.
  - **Remote members in the host's walks.** A remote member gets its `SIG` in the
    section under the lock that woke it: the walk of `Set`, a `Close`, or the end of a
    connection. After the lock, only in-process waits are completed and the metrics
    recorded. On the host's dispose nothing is done for remote members: `DisposeAsync`
    closes every connection anyway.
- **To read.** [Concept](../concept.md), §4, §6, §11 (18 November 2026); `AGENTS.md`,
  principles 5, 7–10 and "Tests And Samples"; this plan, "The public contract" and "The
  protocol".
- **Acceptance.**
  - Tests of the codec: every line form, parsed and formatted, and every refusal with its
    code; each states a rule of `docs/protocol.md`.
  - Tests of the host over loopback with raw protocol lines, written by the test author
    after `docs/protocol.md` is approved: `HELLO` accepted; refused for `version` and
    `token`, the client reading the `ERR` line; `connections` answered to a connection
    beyond the limit whose client sends nothing before it reads; no `HELLO` within
    `DeadPeerTimeout` (fake clock); `SUB`, `WAIT`, `SIG`, `UNWAIT` in both orders against
    a `SIG`, `UNSUB` and `SET` with its group count; `subscriptions` refused with the
    connection kept; `UNSUB`, `WAIT` and `UNWAIT` for a `<sid>` that is not open
    ignored; an `UNSUB` sent before its `SUB` is answered; a line too long, an unknown
    verb, a line before `HELLO`, a name longer than 128, a `SUB` for an open `<sid>` and a
    `WAIT` for an armed member closing with `protocol`; the host's dispose closing every
    connection with nothing more written; the members of a refused
    connection removed when it is closed; `PING` answered; a silent connection closed
    after `DeadPeerTimeout`; a busy member whose connection ends hands its signal to an
    in-process member that already waits; `DisposeAsync` closing every connection and
    freeing the port; the instrument `connections`.
  - A test that an in-process member woken by a `SET` of a connection continues off that
    connection's reader: while the member's code blocks, the host answers the next line
    of that connection (contract, "Completions").
  - The S1 tests of the host's socket pass unskipped.
  - *Reported*, by scenarios the test author adds to the measurement program, through the
    host over loopback with a raw socket, since the program uses the public API only: the
    `SET` lines per second the host applies from one connection with pipelined lines, and
    the bytes it allocates per `SET`.
  - The documents step adds `UNWAIT` to concept §6 as the owner answered PQ3, row d. The
    standalone host sample runs; `README.md` says the host listens and which protocol it
    speaks, and lists the instrument `connections`.
- **Small changes made on this branch** (`process.md`, "Small changes during a sprint"):
  two clarifications of the grammar of `docs/protocol.md`, from the tests step of S3 (the
  coordinator): a `0` is not an id of a client line, and the host writes it only in
  `OK 0` and in `ERR 0` with a connection code; `<text>` has no space at its start or end.
  O12, which edited `docs/work/process.md` and this plan (the owner). From the independent
  review of S3 (the coordinator): the naming rule of `AGENTS.md` asks a `CancellationToken`
  only of a method a caller awaits; E1's status line names the host over TCP.
- **Out of scope.** `SignalClient`; reconnection; explanations.

### S4 · Refactoring

**Track:** refactoring · **Branch:** `epic-1/sprint-04-refactoring` · **Needs:** S3.

- **Goal.** The code base after S3 has no technical debt worth removing before S5 and S6
  build on it.
- **Contract.** None; nothing public or asserted changes.
- **To read.** `AGENTS.md`; [development process](process.md#a-refactoring-sprint), "A
  refactoring sprint"; the whole code base; the briefs of S5 and S6.
- **Acceptance.** Build, format and every test green after every commit. No public member
  and no assertion changed. Each change makes the code simpler to read or to change. The
  documents and the explanations still agree with the code (E1, if the structure it
  shows changed).
- **Out of scope.** Behavior, the contract, anything for a future need.
- KISS (`AGENTS.md`, "Keep It Simple") holds here as everywhere. The task is to remove
  technical debt where it is worth it, so that the code stays clean, easy to understand,
  easy to change and free of duplication. It is always a balance: a change that makes the
  code larger, more abstract or more general than today's case needs is not a
  refactoring.

### S5 · The remote client: liveness and reconnection

**Track:** full · **Branch:** `epic-1/sprint-05-remote-client` · **Needs:** S4.

- **Goal.** Code in another process uses `ISignals` with the same semantics as in the host
  process: a client connects, sets without ever waiting for a connection, subscribes and
  waits, knows within seconds when the host is gone, reconnects by itself and returns
  `Joined` to every member afterwards.
- **Contract.**
  - *Public, from S1, now working:* every member of `SignalClient` and of its
    subscriptions, by the rules of [Notifications](#notifications) for `StateChanged`.
  - *Internal:* none. The tests use the public API, and raw protocol lines for a test
    host that misbehaves on purpose or holds back its answers. So S5 has no contract
    step: the tests step comes first. The implementer writes in the documentation of
    `SignalClient.Start` the decided sentence of Q1 of the
    [open questions](open-questions.md): `State` is `Connected` before any `Joined` of
    that connection, and the notification of `Connected` is not ordered against it.
- **Study** (done, ideas only, `THIRD-PARTY-NOTICES.md` unchanged; the public contract and
  the protocol need no change). Sources at pinned commits: nats-io/nats.net v3.4.0
  (`b268b35f9cbd0537088781a78596badcfe5c174c`, Apache-2.0); StackExchange.Redis 3.4.35
  (`510f791e70158665d7e8207c84d32d0a2b2a2792`, MIT); dotnet/SqlClient v7.1.2
  (`b1dd9db72cd9e6eb9fc83ca308558e73b57dd449`, MIT). Decisions the implementer and the
  test author share:
  - **Replies** are matched by `<rid>` and `<sid>`, taken from one counter of the client
    and never reused; a subscription keeps its `<sid>` across connections.
  - **The gate.** Replies are matched, and requests added, only under the client's lock
    while the connection is not ending. The section that begins the end takes out the
    pending sets, which complete with `Unavailable` after the lock.
  - **Completions** go through a `TaskCompletionSource` with
    `RunContinuationsAsynchronously`, after the lock.
  - **Unknown ids.** An id the client does not know is ignored. An answer for a known
    `<sid>` with nothing outstanding closes the connection with a reason.
  - **One timer per connection**, re-armed for the earlier of the dead peer at the last
    receive plus `DeadPeerTimeout` and the `PING` when it is due. Any bytes received are
    traffic; no `PING` goes before `OK 0`.
  - **Reconnecting.** One loop on the thread pool runs the attempts. The backoff doubles
    up to `MaxReconnectDelay`, is reset by an accepted attempt and is waited through the
    `TimeProvider`. One attempt, connect plus the answer to `HELLO`, is bounded by one
    `CancellationTokenSource(DeadPeerTimeout, TimeProvider)`.
  - **`Connected` and `Joined`** (Q1). On `OK 0`, one section under the lock writes every
    reopening `SUB`, sets `Connected` and queues the notification; the reader handles an
    `OK <sid>` only after that section.
  - **`UNWAIT`** is appended under the lock. A crossing `SIG` sets a flag, and the wait
    completes at `OK <sid>`, with `Signaled` if the flag is set.
  - **Notifications** are queued under the lock in the order of the changes and drained by
    one thread-pool work item. Handler exceptions are unhandled; a dispose does not wait
    for a handler.
  - **Placement.** Nested types of `SignalClient`, in `Outer.Nested.cs` files. The client
    reads without waiting for its flush, since its output is bounded by its outstanding
    requests.
  - **Limitations.** The notification queue grows only while a handler blocks. On Unix a
    name lookup can outlive an attempt; the client stops waiting at the deadline.
- **To read.** [Concept](../concept.md), §3, §4, §6, §11 (18 November 2026);
  `docs/protocol.md` as S3 committed it; `AGENTS.md`, principles 2–9; this plan, "The
  public contract".
- **Acceptance.**
  - The remote tests of S1 pass unskipped.
  - Tests with a test host that speaks raw lines, added in the tests step:
    - a host that goes silent gives `Disconnected` after `DeadPeerTimeout` (fake clock);
      `PING` is sent after `HeartbeatInterval` with nothing sent, and after
      `HeartbeatInterval` with nothing received, and not again within `HeartbeatInterval`
      while the host stays silent; a client that keeps sending `WAIT`s that
      the host does not answer stays connected to a host that answers its `PING`s; the
      backoff delays grow from `ReconnectDelay` to `MaxReconnectDelay` and fall back
      after a connection is accepted; an attempt that gets no `HELLO` answer ends within
      `DeadPeerTimeout`, or sees the connection end before that answer, and raises no
      `StateChanged`;
    - with the host holding back its answer to `SUB`, `WaitAsync` does not return
      `Joined` until the answer arrives, both for a `Subscribe` made while connected and
      for a subscription reopened after a reconnect;
    - a set answered `Unavailable` while the client was not connected never reaches the
      host: after the client connects, the first `SET` the host receives is that of a
      later call;
    - a `SET` whose answer is lost with the connection gives `Unavailable`;
    - a wait ended by its deadline or token while a `SIG` crosses the `UNWAIT` gives
      `Signaled`, and without the `SIG` gives `DeadlineReached` or
      `OperationCanceledException`;
    - no `<sid>` is used twice on a connection; a `SIG` for a `<sid>` the client has
      closed is ignored;
    - subscriptions are reopened before requests made after `Connected`;
    - `StateChanged` is raised in order, on another thread than the caller's, for
      `Connected` and `Disconnected`, and for each refused attempt with the host's code
      at the start of `Reason`; while a handler blocks, sets, waits and
      heartbeats go on; a handler that disposes the client completes.
  - Against a real `SignalHost` with `MaxConnections` 1, a second client never reaches
    `Connected` while the first is connected.
  - A test that while the code after an awaited `WaitAsync` or `SetAsync` of a client is
    blocked, another `SetAsync` of the same client completes and the client keeps its
    connection (contract, "Completions").
  - *Reported*, by scenarios the test author adds to the measurement program: loopback
    set to wake p50, p99 and maximum, judged as the owner answered PQ2; sets per second
    from one client with concurrent callers; bytes allocated per remote `SetAsync` and per
    remote wake.
  - **Explanation E2**, "Why nothing is lost when a connection drops or the host
    restarts", in `docs/explanations` as Markdown and as animated HTML, written as
    [Rules for every sprint](#rules-for-every-sprint) says: heartbeats and a dead peer,
    `Unavailable`, a busy member that leaves and the signal handed on, a host restart,
    the reconnect and `Joined` after the host answered. It links to concept §3, §4 and
    `docs/protocol.md` as the rules.
  - The documents step edits concept §3, rule 6, and D8, and the bullet of `README.md`
    on sets, as the owner answered PQ3, rows a, f and g. The remote samples
    run to the end; `README.md` says the client works.
- **Small changes made on this branch** (`process.md`, "Small changes during a sprint"):
  a CI job that runs the build and the tests on Linux ARM64, not a required check of
  `main` (O13, the owner).
- **Out of scope.** The hosting package; TLS; anything of concept §9.

### S6 · The hosting package, the samples and the first release

**Track:** full · **Branch:** `epic-1/sprint-06-hosting-and-release` · **Needs:** S5.

- **Goal.** An application registers the host or a client in a generic host with one
  call and gets log lines; every sample runs to the end; the documents say what was
  delivered; everything the owner needs before tagging the first release holds.
- **Contract.** None new: S1 put the hosting contract into code, and this sprint makes it
  work. There is no contract step to show.
- **Steps.** In the tests step the test author checks that the hosting tests of S1 cover
  this Acceptance and adds what is missing, before the implementation. The documents of
  this sprint — `README.md`, E3, the final check of the explanations and the comparison
  over the whole product documentation — are made before its independent review, not
  after it, so that the review covers them; a document changed after the review gets
  another review round.
- **To read.** [Concept](../concept.md), §5, §11 (18 November 2026); `AGENTS.md`,
  "Documentation"; [branches and releases](branches.md), "Releases"; `docs/protocol.md`;
  the explanations E1 and E2; this plan, "The public contract", "Log lines" and "The
  first release".
- **Acceptance.**
  - The hosting tests of S1 pass unskipped, and the log lines of [Log lines](#log-lines)
    appear with their categories, levels and values.
  - Every sample runs to the end with `dotnet run`, and the code in `README.md` is the
    code of the samples.
  - The measurement program runs every scenario on the final build, and the coordinator
    records the figures.
  - **`README.md`** says what works, with the measured figures and the processor and
    operating system they ran on. It is packed into both packages, so every link is
    absolute, into the repository on GitHub that `PackageProjectUrl` names, and none
    points to this epic document, which is removed after the release. Its "Package" section
    still says that nothing is published: the release change of
    [The first release](#the-first-release) turns it to the published state.
  - **Explanation E3**, "How the library is built", in `docs/explanations` as Markdown and
    as HTML with a still figure, written as [Rules for every sprint](#rules-for-every-sprint)
    says: the host process with its in-process `ISignals`, the client processes with one
    connection each, the line protocol, the two packages and the folders of
    `Moltaro.Signals` with the direction of their dependencies.
  - **The final check of the explanations**, made by the coordinator and checked by the
    independent reviewer: for E1, E2 and E3, the Markdown and the HTML say the same thing;
    both agree with what was delivered, with the concept and with `docs/protocol.md`;
    every status line says "built"; every link resolves; every HTML file opens from disk
    with nothing loaded from elsewhere, and an animated one plays on its own and can be
    stepped through.
  - The comparison of `README.md`, the concept, `docs/protocol.md` and the explanations
    with what exists, made once more over the whole product documentation, as the
    process says for the last sprint of an epic. Everything of this plan that has to
    outlive it is in its product document: the answer to PQ2 in concept §11, the edits of
    PQ3, the metrics and log lines in `README.md`. A disagreement with an accepted decision
    goes to the owner.
  - Items 2 to 6 of [The first release](#the-first-release) hold, item 3 without its
    last sentence, which only the release change can make true.
- **A sample of S1 to complete** (from the review of S1). The standalone host becomes one
  piece a reader can copy, still about 30 lines, its Ctrl+C wiring included; today that
  wiring sits in `Program.cs`, apart from `StandaloneHost.cs`.
- **Out of scope.** Configuration binding helpers, health checks, anything of concept §9.

## The first release

The coordinator never creates a release tag; the release is the owner's (O1,
[branches and releases](branches.md#releases)). Before the owner tags it, all of the
following hold:

1. S6 is closed and merged; `main` is green on both CI jobs; the release change below
   has passed its review.
2. No entry point throws `NotSupportedException` and no test is skipped in the Release
   run of CI; the tests of S1 pass with their assertions unchanged, except corrections of
   a wrong test made by the rule of the process, each with its reason.
3. `dotnet pack -c Release` from `src` produces exactly `Moltaro.Signals` and
   `Moltaro.Signals.Hosting`, each with the repository README, its XML documentation
   file, the licence expression, the repository URL and its description.
   `Moltaro.Signals` has no dependency;
   `Moltaro.Signals.Hosting` depends only on `Moltaro.Signals` and
   `Microsoft.Extensions.Hosting.Abstractions`. Packed from the release change, the
   README inside each package reads correctly on its own: the released state, the install
   command, links that resolve outside the repository.
4. The figures of [Measured results](#measured-results) are recorded on the final build,
   and every target is met or its miss was decided by the owner.
5. `README.md`, the concept, `docs/protocol.md` and the three explanations say what
   exists; `THIRD-PARTY-NOTICES.md` lists everything a sprint adapted from another
   project.
6. Every entry of the [open questions](open-questions.md) is answered, or carried into
   another epic by the owner's decision.
7. The owner's steps: the rulesets are applied; the nuget.org Trusted Publishing policy
   is re-activated, as [branches and releases](branches.md#releases) says; the owner
   merges the release change and at once tags its merge commit on `main` and pushes the
   tag. The version proposed is `v0.1.0` (D13). The Publish workflow builds, tests, packs
   and pushes both packages.

**The release change.** `README.md` on `main` says what works today (`AGENTS.md`), and
the README packed into a package cannot change after it is published. So the "Package"
section is turned to the published state — the packages on nuget.org from 0.1.0, the
command that installs them — by one small light-track change after S6 is merged, which
changes nothing else and has its independent review. O1 does not cover it: the owner
merges it and tags its merge commit right away, so the claim on `main` comes minutes
before it is true, and the tagged commit carries it into the packages. If the Publish
workflow fails, the owner acts at once: a run that failed outside the tagged commit is
run again, and any other failure takes the next version, with the section changed for it,
as [branches and releases](branches.md#releases) describes the release.

After the packages are on nuget.org, a light-track change removes this document from
`docs/work`, as the [work README](README.md) says of a finished epic.
