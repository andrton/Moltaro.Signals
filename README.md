# Moltaro.Signals

Remote auto-reset events for .NET. An embeddable in-memory host and a TCP client: `Set` is
idempotent per key, one waiter per group wakes, and liveness is visible at once. Made for
the case where a message broker is used only as a wake-up doorbell.

## Status

Contract stage. The public contract of both packages exists in code, with its
documentation; only the checks of options, names and arguments work, and the rest is not
implemented yet. Nothing below works today. The first epic, the
library up to its first release, is under way in [current work](docs/work/README.md).

## What it is, and what it is not

Background workers usually keep their work in a database and only need a nudge: "something
new is there, look now". This library gives that nudge as the state of an auto-reset
event, not as a message:

- twenty sets of a raised event are one set;
- one set wakes exactly one waiting member of a group, and every group gets its own;
- a set that arrives while a member is working is waiting for it when it comes back;
- a member that dies mid-work hands the signal on;
- after a reconnect every member does one full pass, so the host can restart freely;
- a client knows within seconds whether the host is alive, and a set never blocks.

It is not a message broker, a queue, a lock or a pub/sub bus with payloads. Signals carry
no data, have no history and are not persisted. See the
[concept](docs/concept.md#9-non-goals-and-possible-later-work) for the full list of
non-goals.

## Documentation

- [Concept](docs/concept.md): where it comes from, semantics, protocol principles,
  decisions and quality bar.
- [Documentation index](docs/README.md) and [current work](docs/work/README.md).
- [How the project is built](AGENTS.md): architectural principles, code style and working
  rules, for people and coding agents.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). Run the commands
from the `src` directory.

```bash
dotnet build
```

```bash
dotnet test
```

## Package

Planned: the packages `Moltaro.Signals` and `Moltaro.Signals.Hosting` on
[nuget.org](https://www.nuget.org), published by the
[Publish](.github/workflows/publish.yml) workflow when a version tag is pushed. Nothing is
published yet. [Branches and releases](docs/work/branches.md) describes how work reaches
`main` and how a version is released.

## License

[Apache License 2.0](LICENSE)
