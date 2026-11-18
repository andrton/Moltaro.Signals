# Moltaro.Signals

Remote wake-up signals for .NET. An embeddable in-memory host and a TCP client: `Set` is
idempotent per key, one waiter per group on a key wakes, and liveness is visible at once.
Made for the case where a message broker is used only as a wake-up doorbell.

## Status

In-process stage. The public contract of both packages exists in code, with its
documentation. What works today is the host inside its own process: `SignalHost` as
`ISignals`, with every rule of signal delivery, deadlines, cancellation and the metrics
below. The host does not listen yet, and `SignalClient` and the hosting package are not
implemented; their members refuse with `NotSupportedException`. The first epic, the
library up to its first release, is under way in [current work](docs/work/README.md).

## Metrics

Each `SignalHost` creates its own `Meter` named `Moltaro.Signals` (`SignalHost.MeterName`),
with the host as its scope. The instruments carry no tags.

| Instrument | Kind | Unit | Counts |
| --- | --- | --- | --- |
| `moltaro.signals.subscriptions` | `UpDownCounter<long>` | `{subscription}` | Subscriptions open on the host |
| `moltaro.signals.sets` | `Counter<long>` | `{set}` | Sets the host applied |
| `moltaro.signals.coalesced` | `Counter<long>` | `{signal}` | Once per (key, group), ancestors included: a set that found a signal already pending. A signal handed on is not counted here |
| `moltaro.signals.deliveries` | `Counter<long>` | `{signal}` | Signals handed to a member: by a set or a signal handed on to a waiting member, or a pending signal taken by a wait. `Joined` is not counted |
| `moltaro.signals.handed_on` | `Counter<long>` | `{signal}` | Signals handed on because a busy member left while its group on that key kept at least one member: to a waiting member, or as a pending signal. A busy member that was the last of its group on that key hands nothing on and is not counted |

## What it is, and what it is not

Background workers usually keep their work in a database and only need a nudge: "something
new is there, look now". This library gives that nudge as a signal that is either pending
or taken, not as a message:

- twenty sets while a signal is pending are one set;
- one set wakes exactly one waiting member of a group on a key, and every group gets its
  own;
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
