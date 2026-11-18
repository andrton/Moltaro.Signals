# Development process

How an epic goes from a plan to finished code when the work is done by agents. The rules
every agent follows in every task are in [AGENTS.md](../../AGENTS.md); this document is for
whoever plans an epic, coordinates its sprints or reviews a sprint.

This is the process of Moltaro.Lattice, carried over on 18 November 2026. The owner's rules
quoted below with their dates were made there and hold here as well. The accepted decisions
of this project are the decisions D1–D15 of the [concept](../concept.md); a decision that
needs more than a row of that table gets a record of its own in `docs/decisions`, created
when the first such decision is.

## Two tracks

| Track | For | How |
| --- | --- | --- |
| **Full** | Changes to the core library and to the public contract | Everything below |
| **Light** | Documentation, samples, build files, small fixes that change no behavior | One agent, the usual checks, and an independent review |

The coordinator picks the track for each sprint and the plan records it. When in doubt, full.

Every sprint, on either track, is reviewed by an independent reviewer, and the review
repeats after the fixes until it finds nothing that blocks; see [Review](#review).

## Roles

One agent holds one role in a sprint. The separation is the point: nobody checks their own work.

| Role | Does | Does not |
| --- | --- | --- |
| **Owner** (a person) | Approves the epic plan and every change of the contract; decides escalations; pushes and merges, or gives the coordinator a standing permission to | — |
| **Planner** | Writes the epic document: goal, scope, requirements, the sprints, and the contract of every sprint, stage by stage | Implement |
| **Coordinator** | Runs an approved epic sprint by sprint: assigns agents, runs the checks, reviews, decides disputes, keeps the epic document current; with the owner's standing permission, pushes and merges a closed sprint | Write production code or tests |
| **Contract author** | Puts the contract of a sprint into code: the public types and members the brief names, with their documentation, refusing until they are implemented | Design the contract: a name or a signature the brief does not give goes back to the planner. Write the tests of that sprint or implement it |
| **Test author** | Writes the tests of a sprint from its brief and contract, before the implementation | Implement that sprint |
| **Implementer** | Makes the tests of the sprint pass | Change what the tests assert; change the contract |
| **Independent reviewer** | Reviews the result against the brief, with no part in producing it | Fix what it finds |

## The plan

The planner delivers an epic document in the form described in [README.md](README.md),
with one brief per sprint. A brief has to be enough for an agent that knows nothing else:

- **Goal** — one behavior, end to end, small enough for one agent and one review.
- **Contract** — the public types and members the sprint adds or changes.
- **To read** — the decisions and documents the sprint depends on, with their dates.
- **Acceptance** — the behaviors the tests have to state. For a sprint on the event state
  machine, the protocol codec or the transport also what is measured: a benchmark or an
  allocation test.
- **Out of scope** — what the sprint deliberately leaves alone.
- **Track** — full or light.

**The contract is written stage by stage.** An epic of many sprints is divided into
stages, and every sprint has its brief from the start. For the sprints of the first
stages, which the plan names, the brief gives the types and members exactly, with their
signatures. For a later stage
it says what the contract has to be able to do, and the planner writes the types and
members into those briefs before the stage begins, with what the earlier stages have
taught. The owner approves the contract of each stage before its first sprint starts. A
sprint never starts from a brief whose contract is not written out and approved: an
interface designed twenty sprints ahead would be changed anyway, and every change of a
contract goes through the owner.

For a sprint on a mechanism that has no public surface — the event state machine, the
protocol codec — the contract is the internal types its tests address; they are named in
the brief and put into code in the same way.

Before any sprint starts:

1. An independent reviewer reviews the plan the way a sprint is reviewed, and reviews it
   again after the corrections, until no P1, P2 or P3 finding is left. A wrong plan is
   the most expensive mistake, and it is the cheapest to catch here.
2. The owner approves the plan, and with it the contracts of the first stages.

Before a later stage starts, the planner goes through the [open questions](open-questions.md)
and takes up those the stage touches. It then completes the contracts of its sprints, an
independent reviewer reviews them as the plan was reviewed, until no P1, P2 or P3 finding
is left, and the owner approves them. The coordinator does not start the first sprint of
a stage without that approval.

If a decision the plan depends on changes while the epic runs, the coordinator
stops and has the plan corrected before the next sprint.

## A sprint on the full track

1. **Contract.** The contract author adds the public types of the sprint with their
   documentation, exactly as the brief names them; what is not implemented throws
   `NotSupportedException`. The build is green. One commit.
2. **Tests.** The test author writes the tests against the contract and marks those that
   cannot pass yet as skipped, with one shared reason. The sample of intended use is
   written too. The build is green and nothing fails. One commit.
3. **Implementation.** The implementer removes the skips and makes the tests pass. It may
   add tests. It does not change the assertions of the tests it was given, and it does not
   change the contract. If either looks wrong, it stops and reports to the coordinator.
4. **Verification.** The coordinator runs the checks itself: build, format, all tests, the
   samples. An agent's report that the checks passed is not a check.
5. **Review.** The coordinator reviews, and an independent reviewer reviews.
6. **Fixes.** The implementer fixes every P1, P2 and P3 finding. The next review covers
   only what changed and what was still open. Review and fixes repeat until an
   independent review ends with no P1, P2 or P3 finding that is still open.
7. **Documents.** The coordinator compares `README.md`, the [concept](../concept.md) and
   `docs/protocol.md` with what the sprint delivered: what exists and what is planned, names, limits, described
   behavior. What no longer holds is corrected and committed with the sprint. A
   disagreement between the code and an accepted decision is not corrected in the text;
   it goes to the owner.
8. **Close.** With no P1, P2 or P3 open and the documents current, the coordinator marks
   the sprint done in the epic document. A finding is no longer open when an independent
   review found its fix sufficient, or when the owner accepted it or moved it to a named
   sprint, as [Review](#review) describes. The next sprint starts, with another implementer.

**Small changes during a sprint.** A change that comes up while a sprint is open and is
small — an answer of the owner that touches a rule, a document, a file of the
repository's settings or the brief of a later sprint; a sentence of documentation — is
made in the branch of that sprint and merged with it. It gets no branch and no pull
request of its own. The brief of the sprint or the [open questions](open-questions.md)
name it with the answer that caused it, whoever holds the role for that kind of work
makes it, and it is reviewed with the sprint. The light track is for a change made while
no sprint is open, or one that cannot wait for the open sprint.

**A sentence of documentation needs no step of its own.** When a decision that is already
made changes a sentence of public documentation or a comment and nothing else of the
contract — no name, no signature, no behavior that was not decided — the agent whose
commit carries the change it describes writes the sentence in that commit: the test author
with the tests, the implementer with the code. The brief or the register gives the
wording, and the reviews check it. A change of a name, a signature or a member remains
the contract author's step.

**A decision is written in one place.** A decision of the owner, or an answer of the
planner, is written out once: in the decision or the list of decisions that owns
it, and in the brief of each sprint whose work it changes. Other documents that touch
the matter point to that place and do not restate it; a sentence elsewhere is changed
only when the decision makes it untrue. Tests are sized the same way: a rule that one
check enforces is stated by the smallest set of cases that tells a right implementation
from a wrong one, not once for every path that reaches the check.

When the last sprint of an epic closes, the same comparison is made once more over the
whole product documentation, and what the epic delivered moves from its working document
into it.

**Questions that do not block.** A sprint can find a gap that no brief, decision
or rule answers and that does not stop it. The agent that finds it reports it and does
not decide it. The coordinator enters it in the [open questions](open-questions.md), with
what the code does until it is answered, names the new entries in its report when the
sprint closes, and goes through all of them with the owner when the epic closes. An entry
that names a sprint as its limit is read when that sprint is prepared: the sprint does
not start before the entry is answered or moved to a later one. A question that does
block is not entered there: the coordinator stops and asks.

A test that turns out to be wrong is changed by the test author, or by the coordinator,
with the reason in the commit message. Never by the implementer on its own.

**Edits that state nothing new.** A sprint can force edits on what already exists: a
member that was renamed is used under its new name, a descriptor is written in a new
form, a helper names another class. Such an edit is made by whoever makes the commit that
would otherwise break the build or a test — the contract author, the test author or the
implementer — in that same commit, in tests and samples as well, so that the build stays
green after every step. When the commit of the contract removes a member in favour of
another, the new one works at once for what already exists, so that the tests that read
it still pass; this is the one case in which a member of a contract commit does more
than refuse. Such an edit never changes what a test asserts. The brief names each such
edit, and the reviewers check that nothing else was changed under this rule.

## Review

**What the independent reviewer gets:** the sprint brief, the contract, `AGENTS.md` and the
diff. Not the implementer's explanation and not the coordinator's opinion: a reviewer that
is handed a conclusion confirms it.

**What is checked:**

- The behavior against the brief and the decisions it names.
- The architectural principles and the quality priorities of the zone, from `AGENTS.md`.
- That every behavior the brief states has a test, and that no test was weakened.
- That nothing outside the sprint's scope was changed. A small change that the owner
  decided while the sprint was open belongs to its scope when the brief or the
  [open questions](open-questions.md) name it; see "Small changes during a sprint".
- That the documentation the change touches says what the code now does.
- That every file the sprint added stands where `AGENTS.md` puts it — one type per file,
  a folder for one responsibility — in the tests and the samples as in the libraries. A
  folder that only collects files is a finding, whoever started it.

**A finding states** where it is, which rule, decision or requirement it breaks, and what
goes wrong because of it: a concrete failure or a concrete cost. Then its severity:

| Severity | Meaning | Blocks |
| --- | --- | --- |
| **P1** | Wrong result; a set lost, one set waking two members of a group, a duplicate turned into a second pass, a dead peer reported as connected; a guarantee of the concept or the contract broken; a security problem; the build or a test fails | Yes |
| **P2** | An accepted decision or an architectural principle is violated; a stated behavior has no test; a concurrency or lifetime hazard; the contract changed without approval | Yes |
| **P3** | It will cost later: duplication, unclear ownership or naming, missing or misleading public documentation, needless complexity, the quality priorities of the zone ignored | Yes |
| **P4** | A suggestion or a matter of taste | No; listed for the coordinator |

A finding that names no rule and no failure is P4. A reviewer does not widen the scope:
"while you are here" is not a finding.

"Does not change the contract", said of the implementer and of the test author, means
its names, signatures and behavior; a decided sentence of documentation is theirs to
write, as "A sentence of documentation needs no step of its own" says. "Every behavior
the brief states has a test" is ranked by what follows: a behavior the Acceptance
states and no test holds is P2; one the brief states elsewhere is P4.

A changed copy of the code that the tests do not notice is a finding of its own only when
the code is wrong, or when the change breaks a behavior that the Acceptance of the brief
states and no test holds it: then it is "a stated behavior has no test", P2. Where the
code is right and the behavior is not one the Acceptance states, it is P4. A review looks
for defects; it does not ask for a test of every line.

**Disputes.** The implementer may contest a finding, with its reason. The coordinator
decides. What the coordinator cannot decide goes to the owner.

**Rounds.** Review and fixes repeat for as long as an independent review finds a P1, P2
or P3. A sprint or a plan is never closed on its author's word that a finding was fixed:
the fix is reviewed. The rounds are not limited by a number: a round that finds something
new, which is then fixed and reviewed, is the review doing its work, and the sprint goes
on.

A finding that comes back is another matter. When a finding is found again after its fix
was reviewed, the coordinator settles it itself first: it decides the dispute between the
implementer and the reviewer, or gives the fix to another implementer. The coordinator
stops and asks the owner only when the finding comes back once more after that, or when
it can be closed only by changing the contract, the acceptance of the brief or an accepted
decision. Then it reports what keeps coming back and what it tried, and the owner
decides: the requirement changes, or the finding is accepted, or it is moved to a named
sprint. The decision is recorded in the epic document, and in the
[exceptions](exceptions.md) as well when it departs from a rule of `AGENTS.md`. A sprint is never closed with an
open P1, P2 or P3 on the coordinator's own word. The report of a closed sprint says how
many rounds it took.

## A study comes before a design

A sprint that builds a mechanism of the library — the event state machine, the protocol
and its codec, the transport, liveness and reconnection — begins with a study, before its internal contract is fixed and
before any code (the owner's rule of 17 November 2026). The agent reads the source, not
summaries, of two to four established systems that solve the same problem, at pinned
commits, the licence checked first by the rule on sources below. It reports, briefly: what
each guarantees, what it costs, what this library takes and why, what does not fit and why. The
planner and the coordinator read the report before the contract step ends. A design of our
own is made only where nothing fits, and the report says so. The study is bounded: it
answers the questions the brief names and is not a survey. It is a step of its sprint, not
a sprint of its own.

## Code modelled on another project

When a sprint repeats a proven solution, its code is modelled only on sources under a
permissive licence (MIT, Apache-2.0, BSD). Sources under GPL or LGPL may be read about
in their papers and documentation to understand an idea; their code is not opened while
ours is written, and nothing is ported from it. A type that follows the code of a source
closely enough to be an adaptation of it says so in a comment at the head of its file —
which project, which file, which commit — and `THIRD-PARTY-NOTICES.md` gains the
project's copyright line and licence text in the same change. The pull request lists
what was adapted and what was only taken as an idea.

## Commits

- A sprint is a short series of commits in this order: contract, tests, implementation,
  fixes. On the branch they are not squashed, so that "tests first" stays visible in the
  pull request; the merge squashes them into one commit of `main`.
- The subject names the epic and the sprint: `Epic 1, sprint 3: tests for re-raising when a busy member leaves`.
- No agent attribution lines, as `AGENTS.md` says.
- Commits stay local. Nothing is pushed without the owner's instruction or standing
  permission.
- One branch per sprint, cut from `main` and merged back through a pull request when the
  sprint closes. [Branches and releases](branches.md) has the names, the merge rules and
  what `main` refuses.

## When to stop and ask the owner

- The contract has to change.
- A decision of the concept contradicts the brief, or has changed.
- A finding keeps coming back after the coordinator has settled it once, as
  [Review](#review) describes. The number of review rounds alone is no reason to stop.
- A dispute the coordinator cannot settle.
- Anything destructive, or anything that leaves this repository: a push, a pull request,
  a merge, a release tag. A standing permission of the owner, as
  [branches and releases](branches.md#who-does-what) describes it, covers the push, the
  pull request and the merge of a closed sprint while no decision of the owner is open;
  nothing covers a release tag.

A measured fall of performance is not by itself a reason to stop (the owner, 16 November
2026). First the cause is found, with a control or a profile. Then it is compared with
what established systems do for the same problem: is this what the problem costs
everywhere, or is something done wrong here. Then alternatives are tried, each kept only
if a figure justifies it. The owner is asked only when nothing helped, the fall cannot be
put down to what the problem costs in the industry, and a requirement of the project is
missed; the question then comes with the cause, the evidence, the alternatives tried with
their figures and the requirement that is missed. A fall that has a known cause and stays
within the requirements is reported at the close.

## Where the state lives

In the epic document: the list of sprints with the status of each. The coordinator updates
it when a sprint closes. What a later agent needs to know is in the brief of its sprint.
What nobody has answered yet is in the [open questions](open-questions.md). There are no
other reports, logs or handoff files.
