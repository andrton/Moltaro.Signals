# Repository Guidelines

`AGENTS.md` is the one instruction file for coding agents and a short statement of how
this project is built. Shared rules live here; `CLAUDE.md` only points to this file.

## Project

Moltaro.Signals is a small .NET library with one primitive: **remote signals with consumer
groups**. A host embedded in one process keeps the signals in memory; clients
in other processes connect over one persistent TCP connection with a line-based text
protocol; code inside the host process uses the same interface without a socket. A signal
carries no data: it only says "look now", coalesces per key and group, and wakes one
waiting member of each group on each key it sets.

What exists today is the **concept** and the **public contract** in code, in which only
its checks work; the epic in [current work](docs/work/README.md) builds the
rest. The [concept](docs/concept.md) is the authority for what the library guarantees: its
semantics, its decisions D1–D15 and its non-goals. `README.md` says what works today; a
feature is delivered when the README says so.

This repository is public. Nothing specific to a private product goes into it.

## Layout

| Path | Contents |
| --- | --- |
| `src` | The solution, every project and the build settings. Run `dotnet` from here |
| `docs/concept.md` | Where the library comes from, its semantics, protocol principles, decisions and quality bar |
| `docs/explanations` | Explanations of the architecture and of how the library works, each as Markdown and as HTML. Created with the first explanation |
| `docs/work` | Working documents: the epics in progress, the development process, branches and releases, exceptions to the rules, open questions |

The projects:

| Project | Contents |
| --- | --- |
| `src/Moltaro.Signals` | The library: the public contract, the host, the remote and in-process clients, the protocol. No package dependencies |
| `src/Moltaro.Signals.Hosting` | Registration in a .NET generic host and a container; a thin separate package |
| `src/Moltaro.Signals.Samples` | Runnable samples of intended use, including a standalone host of a few lines |
| `src/*.Tests` | Tests, one project per library |
| `src/Moltaro.Signals.TestSupport` | Code that two or more test projects need, as "Tests And Samples" says. Never shipped; no tests |
| `src/Moltaro.Signals.Measurements` | Planned with signal delivery: the benchmark program of the concept's performance targets. Started by hand; never shipped and not a test project |

The repository root holds only repository-level files. Code never lives there.

## Keep It Simple

KISS is the base rule for design, code, tests and documentation.

- Start from the simplest thing that fully solves today's case. Prefer one plain rule or
  an existing mechanism over a new one.
- Build what is needed now. What is not needed yet is not built; at most it is noted as
  planned in the document that owns it.
- No speculative abstractions, options, switches, retries, fallbacks or defensive checks
  "just in case".
- When something is awkward to use, fix its cause. Do not wrap it: no adapter or facade
  class around a core type to work around what the core lacks.
- Simple to use comes first: a small public surface, few options, one obvious way to do
  each thing. Then a simple implementation.
- A case that is rare in practice and costs no guarantee (see "Quality Priorities") is not
  handled. Its limitation is stated in one sentence, and nothing is built for it.
- A review finding is fixed by the smallest change that removes it. A new mechanism,
  option, state or public type is added only when a guarantee needs it; otherwise the fix
  states the limitation or removes the cause. An area whose fixes keep producing new
  findings is over-built: simplify it instead of fixing it again.

Simple is not the goal by itself. Do not make something simpler by dropping a guarantee,
blurring a boundary or pushing the difficulty onto the caller. The target is the simplest
design that keeps every accepted guarantee. When simple and correct disagree, correct
wins. When simple and fast disagree, measure before choosing.

The value of this library is that it is small enough to trust (concept D14). A request
outside the primitive is declined, or noted as planned in the concept.

## Architectural Principles

1. **A signal is state, not a message.** It has no payload, no history and no
   persistence. Receivers read the facts from their own storage. Nothing in the library
   queues, orders or replays signals.
2. **Set is idempotent and never waits for a connection.** A set while a signal is
   pending does nothing. A client that is not connected answers `Unavailable` at once; it never blocks
   and never queues a set for later. A connected client waits only for the host's answer,
   which carries the group count; that wait ends at the latest when a silent connection
   is declared dead.
3. **The last set is never lost.** Every rule of delivery serves this guarantee: one
   waiting member of a group on a key receives a signal; a member is busy until its next
   wait, and a set meanwhile leaves a signal pending; a member that leaves while busy
   hands its signal on; the first wait after subscribing or reconnecting returns `Joined`. A change that
   weakens any of these is a defect, whatever it gains.
4. **The host may restart at any time.** All signal state lives in the memory of the host;
   correctness never depends on it surviving. `Joined` after a reconnect is what makes
   this safe.
5. **Liveness is explicit.** Heartbeats detect a dead peer within a bounded time; the
   client exposes its connection state and a change notification. Nothing waits silently
   for a peer that is gone.
6. **One interface, two transports.** Code inside the host process and code in another
   process use the same `ISignals` contract with the same semantics. Application code
   does not branch on where the host is.
7. **One host instance is independent of another.** A process may run several. No static
   mutable state, except where a comment says why.
8. **Waiting is finite and outside locks.** No lock is held across I/O or an `await`. Every
   wait honors cancellation; a wait that can be long has a deadline. A timeout or a limit
   an application can observe — heartbeat interval, dead-peer time, reconnect backoff,
   connections, subscriptions per connection — is an option with a default, validated
   when the host or client is created; it is never a constant in the code. The length of
   a key, a group or the token is not an option: the protocol fixes it, so that both
   sides check the same limit. A type that takes more than one lock documents the order.
9. **Never an approximate success.** A request is honored or refused with a code. A
   protocol violation closes the connection with a reason; it is not guessed around.
10. **Dependencies point one way.** `Moltaro.Signals` depends on nothing outside .NET.
    `Moltaro.Signals.Hosting` depends on it and on the hosting abstractions of .NET. Inside
    an assembly a folder is a namespace and one responsibility, and folders depend on each
    other in one direction: signal delivery knows nothing of sockets, the protocol codec
    nothing of signal delivery, the transport ties them together. Public types stay in the
    namespace the contract gives them.
11. **Performance is a design goal, proven by measurement.** Avoid needless copies and
    allocations on the set and wake paths, but justify added complexity with a number from
    the benchmark program.

## Quality Priorities

When two good things pull apart, the zone of the code decides which one wins. Read each
row left to right: an earlier quality is not traded for a later one.

| Zone | Priorities, in order |
| --- | --- |
| Signal delivery | Correctness → latency and allocations → readability → flexibility |
| Protocol codec and transport | Correctness → robustness under disconnects and bad input → latency → readability |
| Public contract | Correctness → clarity of use → stability → speed |
| Hosting integration, samples, tests | Readability → simplicity; speed does not matter |

- **Correctness is first everywhere.** For this library it means: the last set is never
  lost, a set never wakes more than one member of a group on a key, a duplicate never
  turns into a second pass, and a dead peer is never reported as connected.
- **A performance claim needs a measurement.** "Faster" or "fewer allocations" is accepted
  with a number from a benchmark or an allocation test, not by argument. Without one, the
  simpler code stays.

## Design Patterns

Patterns are vocabulary for explaining a decision, not a requirement to meet. No pattern
is applied because it exists.

- Put logic where the data it needs lives.
- Introduce an interface only at a real boundary: the public contract, the transport. One
  implementation behind a private interface is not a boundary.
- On the set and wake paths, design from the data: plain structures, no allocation per
  signal where a measurement shows it matters.
- Elsewhere a known pattern is welcome when it removes real duplication or names a real
  variation. Say which problem it solves; if that cannot be said in a sentence, leave it out.

## The Contract Comes First

A feature starts as contract: public types with documentation, a sample of its use, and
tests that state its behavior. Implementation follows.

- Respect the stage that was agreed. "In the contract, not implemented yet" means exactly
  that: the entry points throw `NotSupportedException`, the tests that need the feature
  are skipped with a shared reason, the sample is written and reports where it stops.
- Checks that need no implementation belong to the contract and work from the start:
  validating a key, a group or an option.
- When the implementation arrives it has to pass the tests that were written for the
  contract. Do not weaken or delete a test to get a green run.
- Nothing has been released. There are no compatibility shims: when something is replaced,
  remove the old code, tests and documentation in the same change.
- The protocol is part of the contract. Its grammar lives in `docs/protocol.md`, written
  with the sprint that introduces it, and its version is part of `HELLO`.

## Code Style

Language and build:

- C# on .NET 10, nullable enabled, warnings are errors. `dotnet format --verify-no-changes`
  stays clean.
- Package versions are managed centrally in `src/Directory.Packages.props`; a
  `PackageReference` has no `Version`.

Organization:

- One type per file, named after the type. The generic form of a type that also has a
  non-generic one goes to `Name.Generic.cs`.
- A folder is a namespace and one responsibility. No `Internal`, `Helpers`, `Utils` or
  other folders that collect unrelated types.
- `internal` by default. A type is public only when it is part of the contract.

Naming:

- Public types are named in the contract stage and reviewed as public API. A name that
  would be generic on its own (`SetResult`, `WaitResult`) takes the `Signal` prefix.
- Every enum type ends with `Enum`.
- Asynchronous methods end with `Async` and take a `CancellationToken` last.
- Private fields are `_camelCase`.
- Name things for what they do for the user, not for the framework mechanism inside.
- Words, as the concept defines them: *key*, *group*, *subscription* (one member of a
  group on one connection), *signal* ("look now", no data), *pending signal* (one that no
  member of the group on that key has taken yet), *host*, *client*.

Shape of the public API:

- Something that answers at once from local memory is synchronous. Something that can
  wait is asynchronous.
- Expected outcomes are results: `Unavailable`, zero live groups, `Joined`,
  `DeadlineReached`. Exceptions are for failures. `ArgumentException` and its relatives
  report a caller's mistake; `ObjectDisposedException` follows a dispose;
  `NotSupportedException` marks what is not implemented.
- Every public member has XML documentation: what it does, what it guarantees, what it
  does not, and which errors it reports. Plain sentences, no restating of the signature.
  Internal members are commented with `//`, not `///`, so that the XML documentation of
  a package holds only its public contract.

Writing code:

- Write for a reader. Descriptive names, one statement per line, blank lines between
  steps; no nested ternaries or dense chains.
- Keep a method focused, without splitting a simple operation across needless helpers.
- Comments are in English and explain why: an invariant, an ordering requirement, who
  owns mutable data, what a lock protects. They do not repeat the code.
- Distinguish "not connected", "nobody listens" and "failed". Never return an invented
  success in place of a failure.
- No reflection on a call path. No duplicated checks. No redundant models.

## Tests And Samples

- A public behavior has a scenario test that reads like its use. A test name is a
  sentence stating the behavior.
- Tests use the public API, with one exception that is a rule of this project: signal
  delivery and the protocol codec have no public surface and are tested directly,
  through `InternalsVisibleTo` for their test project. Each such test states a rule of the
  concept (section 3) or of `docs/protocol.md`, never how the code happens to be built.
- Every rule of the concept's section 3 has a deterministic test of signal delivery.
- Time is injected through `TimeProvider`. Heartbeats, dead-peer detection, deadlines and
  backoff are tested with a fake clock.
- A test waits for an observed event; it never sleeps and never polls with a delay.
- Transport tests run over loopback TCP: connect, heartbeat loss, host restart (`Joined`),
  leaving while busy (hand on), bad input and limits.
- Code that two or more test projects need lives once, in a `Moltaro.Signals.TestSupport`
  project, created when the second test project needs it. It is never shipped and contains
  no tests.
- A test project is organized like a library: a folder is a namespace and one area of
  behavior.
- A feature that is not implemented has its tests written and marked
  `[Fact(Skip = ...)]` with one shared reason. Skipping is never used for a flaky test.
- Tests come first and from someone else. On the full track of the
  [development process](docs/work/process.md), the tests of a sprint are written and
  committed before its implementation, by a different agent than the one who implements
  it. The implementer removes the skip and makes them pass; it does not change what they
  assert. A test that looks wrong is reported, not edited.
- The samples project shows intended use and runs to the end with `dotnet run`.
- Code shown in `README.md` must compile against the current contract.

From `src`:

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
```

## Documentation

- Everything in the repository is written in English: documentation, code comments, XML
  documentation, test names and commit messages. This holds whatever language the task
  was given in.
- A document lives next to the code it changes. Product documentation is in `docs`;
  working documents, the epics, are in `docs/work`. The plan for using this library inside
  another product belongs to that product's repository.
- A decision is not an implementation. Say plainly what exists and what is planned.
- An explanation of the architecture or of how the library works is written twice, in
  `docs/explanations`: as Markdown and as HTML, saying the same thing. The two change
  together, in the same change. The HTML is one self-contained file that opens from disk,
  without a build, a server or anything loaded from elsewhere. In the main cases, named
  when the explanation is planned, the HTML is animated: the animation plays on its own,
  and the reader can step through it, with a caption for each step.
- When behavior changes, update the document that owns it in the same change: the
  concept for semantics and decisions, `docs/protocol.md` for the wire format,
  `README.md` for status and use; and the explanations that describe it, both forms.
- When a larger block of work is finished — a sprint, an epic, a change of the public
  contract — compare `README.md`, the concept, `docs/protocol.md` and the explanations
  with what now exists. Correct every statement of status, every name, limit and
  described behavior that no longer holds, in the same change.
- That check never changes a guarantee. When the code and an accepted decision disagree,
  it is a finding to report, not a sentence to edit.
- Requirements that come from a particular consumer are written in general terms.

## Working Rules

- The user's instructions define the task. Among repository documents this file owns the
  global rules. Code and tests show what the code does, not that it is intended.
- A departure from a rule of this file is recorded in
  [exceptions to the rules](docs/work/exceptions.md) in the same change: which rule,
  where, why and until when.
- Before designing a change to the contract, read the concept section that covers it.
- Make routine, reversible choices within the task. Ask only when a missing decision
  changes behavior, the contract or the scope. Say what was left out.
- Review the whole affected flow before finishing: contract, implementation, tests,
  samples, `README.md`, `docs`.
- Keep plans, progress and findings in the conversation. Do not create report, audit or
  plan files unless the user asks for that document.
- Never revert the user's changes or unrelated work.
- Do not commit or push unless asked. Stage only the files of the task. No secrets,
  tokens, machine paths, build output or run output in Git.
- Never commit to `main`. Work on a branch named as
  [branches and releases](docs/work/branches.md) says; `main` changes only through a
  pull request, merged by the owner or, for a closed sprint and with the owner's standing
  permission, by the coordinator of an epic. Do not create or move a release tag.
- Commit messages carry no agent attribution: no `Co-Authored-By` line and no "generated
  with" line. This overrides the default of any tool.
- Planning an epic, coordinating its sprints, reviewing a sprint or refactoring follows
  the [development process](docs/work/process.md). Read it before taking one of those
  roles.
- Technical debt is removed by the refactorer in the refactoring sprint of every epic, as
  the development process describes. Its measure is this file.
- Finish by reporting what changed, which checks ran and what is not done. When the task
  leaves changes in Git, suggest a commit message.
