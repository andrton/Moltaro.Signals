# Open questions

Questions that came up while a sprint was carried out and that no brief, decision or rule
answers. Each is written down when it is found, so that it is not settled by whoever
happens to write the code, and not forgotten.

A question that blocks a sprint does not wait here: the coordinator stops and asks the
owner, as the [development process](process.md#when-to-stop-and-ask-the-owner) says. This
register is for what does not block: the sprint goes on, and the entry says what was done
in the meantime.

## How it is used

- **Who writes.** The coordinator, when an agent or a reviewer reports a gap, or when it
  finds one itself. An agent reports; it does not decide.
- **What an entry says.** Where the question was found, the question itself, what the
  code or the tests do until it is answered, and who has to answer it.
- **When it is looked at.** When a sprint closes, the coordinator names the entries the
  sprint added in its report to the owner. When the coordinator prepares a sprint, it
  reads the open entries that name that sprint and does not start it before each of them
  is answered or moved to a later sprint by whoever decides it. Before the contracts of a
  stage are written, the planner goes through the open entries and takes up those the
  stage touches. When an epic closes, every entry is gone through with the owner: none
  stays open without a decision to carry it into the next epic.
- **How it ends.** An answer comes from whoever the entry names as deciding. A rule of
  the public contract or a guarantee of the concept is the owner's; what follows from a
  rule that already stands the planner answers and reports to the owner. It is entered
  here with its date and with who gave it, and the rule it gives goes where it belongs:
  the concept, `docs/protocol.md`, the epic document, a brief, `AGENTS.md`. The entry then
  moves to **Answered** and says where the rule now lives.

An entry is numbered `Q1`, `Q2`, … and keeps its number for good:

| | |
| --- | --- |
| **Found** | When, and in which sprint or review |
| **Question** | The gap, stated so that it can be answered |
| **Until answered** | What the code and the tests do meanwhile |
| **Decides** | The owner or the planner |

## Open

None.

## Answered

### Q1. `Connected` and `Joined` of a subscription opened while connecting

| | |
| --- | --- |
| **Found** | 18 November 2026, Epic 1, S1, by the test author |
| **Question** | When a client connects, it sends the openings of its subscriptions and then reports `Connected`; a subscription returns `Joined` once the host answered its opening. Is `State` already `Connected` when such a `Joined` returns, or may the `Joined` come first? |
| **Answer** | The planner, 19 November 2026, at the start of Epic 1, S5: `State` is `Connected` before any `Joined` of that connection can be decided. `Connected` is set in the same section under the client's lock that writes the reopening `SUB`s, and the reader handles an `OK <sid>` only after that section. The notification of `Connected` is not ordered against the `Joined`: its handler runs on the thread pool, apart from any wait, and `State` may have moved on by then. The rule lives in the brief of S5 and in the documentation of `SignalClient.Start` |

### Q2. A time limit for a test run that hangs

| | |
| --- | --- |
| **Found** | 18 November 2026, Epic 1, S1, by the test author and the independent reviewer |
| **Question** | Many skipped tests await a wait with no bound, so a wrong implementation makes them hang instead of fail, and a CI job then runs until GitHub's default limit. Should the CI jobs or the test run have a time limit, and which? |
| **Answer** | The coordinator, 18 November 2026, at the start of Epic 1, S2: each CI job has `timeout-minutes: 15`; the tests get no time limit of their own. The rule lives in `.github/workflows/ci.yml` |
