# Moltaro.Signals

Remote wake-up signals for .NET. An embeddable in-memory host and a TCP client: `Set` is
idempotent per key, one waiter per group on a key wakes, and liveness is visible at once.
Made for the case where a message broker is used only as a wake-up doorbell.

## Status

Version 0.1.0, the first release. Both packages work as their documentation says:

- `SignalHost` is `ISignals` inside its own process, with every rule of signal delivery,
  deadlines, cancellation and the metrics below. After `Start` it listens over TCP and
  speaks version 1 of the [protocol](https://github.com/andrton/Moltaro.Signals/blob/main/docs/protocol.md).
- `SignalClient` is `ISignals` in other processes, with heartbeats, dead-peer detection,
  reconnection with backoff and `Joined` after every reconnect.
- `Moltaro.Signals.Hosting` registers either in a .NET generic host and writes the log
  lines below.

Versions stay 0.x until a first production adopter runs on the library.

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
- a client knows within seconds whether the host is alive, and a set never waits for a
  connection.

It is not a message broker, a queue, a lock or a pub/sub bus with payloads. Signals carry
no data, have no history and are not persisted. See the
[concept](https://github.com/andrton/Moltaro.Signals/blob/main/docs/concept.md#9-non-goals-and-possible-later-work)
for the full list of non-goals.

## Use

The process that owns the background work runs the host and registers it first, so that
its port is bound before its workers start:

```csharp
builder.Services.AddSignalHost(options =>
{
    options.ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0);
    options.Token = "sample-token";
});
builder.Services.AddSingleton<JobStore>();
builder.Services.AddHostedService<JobWorker>();
```

A process elsewhere registers a client of that host instead:

```csharp
builder.Services.AddSignalClient(options =>
{
    // The sample runs the host in this process; a real client names the host's address from its configuration.
    options.HostEndPoint = signalHost.LocalEndPoint;
    options.Token = "sample-token";
});
```

A worker's code is the same with either: it waits, then does one full pass over its own
storage.

```csharp
internal sealed class JobWorker(ISignals signals, JobStore store, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using ISignalSubscription lane = signals.Subscribe("jobs", "workers");

        while (!stoppingToken.IsCancellationRequested)
        {
            SignalWaitResultEnum result = await lane.WaitAsync(until: null, stoppingToken);

            // Every result is followed by one full pass over the storage.
            while (store.TryClaim(out string? job))
            {
                Console.WriteLine($"The worker ({result}) processes {job}.");
                // Only so that the sample ends; a real worker keeps its loop running.
                lifetime.StopApplication();
            }
        }
    }
}
```

A publisher sets the key after its writing transaction has committed; `Applied` with
`Groups` 0 means nobody listens, `Unavailable` that the client has no connection right now:

```csharp
SignalSetResult result = await app.Services.GetRequiredService<ISignals>().SetAsync("jobs", cancellationToken);
```

A host in a process of its own runs this method until Ctrl+C:

```csharp
public static async Task RunAsync(IPEndPoint listenEndPoint, CancellationToken cancellationToken)
{
    using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    // Ctrl+C ends the wait below instead of the process, so that the host is disposed and closes its connections.
    using var ctrlC = PosixSignalRegistration.Create(PosixSignal.SIGINT, signal =>
    {
        signal.Cancel = true;
        stop.Cancel();
    });

    await using var host = new SignalHost(new SignalHostOptions
    {
        // An address of the internal network serves other machines; the loopback address only this one.
        ListenEndPoint = listenEndPoint,

        // Shared with every client. A real host reads it from its configuration, never from its code.
        Token = "sample-token",
    });

    host.Start();
    Console.WriteLine($"The host listens on {host.LocalEndPoint}.");

    // The cancellation is the expected end, not a failure.
    await Task.Delay(Timeout.Infinite, stop.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

    Console.WriteLine("The host stops.");
}
```

All of this is taken from the
[samples](https://github.com/andrton/Moltaro.Signals/tree/main/src/Moltaro.Signals.Samples),
which run to the end with `dotnet run`.

## Metrics

Each `SignalHost` creates its own `Meter` named `Moltaro.Signals` (`SignalHost.MeterName`),
with the host as its scope. The instruments carry no tags.

| Instrument | Kind | Unit | Counts |
| --- | --- | --- | --- |
| `moltaro.signals.connections` | `UpDownCounter<long>` | `{connection}` | Client connections whose `HELLO` was accepted and that are open; a connection leaves it when its socket is closed |
| `moltaro.signals.subscriptions` | `UpDownCounter<long>` | `{subscription}` | Subscriptions open on the host, in-process and remote |
| `moltaro.signals.sets` | `Counter<long>` | `{set}` | Sets the host applied, in-process and remote |
| `moltaro.signals.coalesced` | `Counter<long>` | `{signal}` | Once per (key, group), ancestors included: a set that found a signal already pending. A signal handed on is not counted here |
| `moltaro.signals.deliveries` | `Counter<long>` | `{signal}` | Signals handed to a member: by a set or a signal handed on to a waiting member, or a pending signal taken by a wait. `Joined` is not counted |
| `moltaro.signals.handed_on` | `Counter<long>` | `{signal}` | Signals handed on because a busy member left while its group on that key kept at least one member: to a waiting member, or as a pending signal. A busy member that was the last of its group on that key hands nothing on and is not counted |

## Log lines

`Moltaro.Signals` itself writes no log. The hosting package writes these lines through the
container's `ILoggerFactory`; the named values are structured properties, and the message
text is not part of the contract.

| Category | Level | When | Values |
| --- | --- | --- | --- |
| `Moltaro.Signals.SignalHost` | Information | The host listens | `LocalEndPoint` |
| `Moltaro.Signals.SignalClient` | Information | Connected to the host | `HostEndPoint` |
| `Moltaro.Signals.SignalClient` | Warning | The connection to the host ended, or the host refused an attempt; then `Reason` begins with its code | `HostEndPoint`, `Reason` |

## Measured

Measured with the [measurement program](https://github.com/andrton/Moltaro.Signals/tree/main/src/Moltaro.Signals.Measurements)
on an Intel Core i9-13900H laptop (20 logical processors), Windows 10.0.26300, .NET 10.0.15,
Release build. Rates and latencies on this machine vary between runs by up to a factor of
five, and the maxima more; allocations do not, and retained memory by a few bytes.

| Scenario | Figure | Target |
| --- | --- | --- |
| In-process set to wake | p50 1.4 µs, p99 4.3 µs, maximum 180 µs | p50 under 50 µs |
| Sets per second on one thread, most of them coalesced | 10.4 million | At least 100,000 |
| Memory retained per (key, group) / per subscription | 190 / 142 bytes, flat over a million sets | Grows with keys × groups and subscriptions only |
| Loopback set to wake, from one client to a member on another | p50 125 µs, p99 422 µs, maximum 17.7 ms | p99 under 1 ms |
| Allocated per in-process wake / per set over loopback | 96 / 0.7 bytes | Reported |
| Sets per second from one client with 64 concurrent callers | 259,000 | Reported |

## Documentation

- [Concept](https://github.com/andrton/Moltaro.Signals/blob/main/docs/concept.md): where it
  comes from, semantics, protocol principles, decisions and quality bar.
- [Protocol](https://github.com/andrton/Moltaro.Signals/blob/main/docs/protocol.md): the wire,
  version 1, with a session you can type into `nc`.
- Explanations, each also as an animated or illustrated page to open from a checkout:
  [how a set wakes one member of each group on a key](https://github.com/andrton/Moltaro.Signals/blob/main/docs/explanations/signal-delivery.md),
  [why nothing is lost when a connection drops or the host restarts](https://github.com/andrton/Moltaro.Signals/blob/main/docs/explanations/connection-loss.md),
  and [how the library is built](https://github.com/andrton/Moltaro.Signals/blob/main/docs/explanations/architecture.md).
- [Documentation index](https://github.com/andrton/Moltaro.Signals/blob/main/docs/README.md).
- [How the project is built](https://github.com/andrton/Moltaro.Signals/blob/main/AGENTS.md):
  architectural principles, code style and working rules, for people and coding agents.

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

The packages are on nuget.org from version 0.1.0:
[Moltaro.Signals](https://www.nuget.org/packages/Moltaro.Signals), which depends on nothing
outside .NET, and [Moltaro.Signals.Hosting](https://www.nuget.org/packages/Moltaro.Signals.Hosting),
which registers the host or a client in a .NET generic host and brings `Moltaro.Signals`
with it.

```bash
dotnet add package Moltaro.Signals
```

```bash
dotnet add package Moltaro.Signals.Hosting
```

They are published by the
[Publish](https://github.com/andrton/Moltaro.Signals/blob/main/.github/workflows/publish.yml)
workflow when a version tag is pushed;
[branches and releases](https://github.com/andrton/Moltaro.Signals/blob/main/docs/work/branches.md)
describes how a version is released.

## License

[Apache License 2.0](https://github.com/andrton/Moltaro.Signals/blob/main/LICENSE)
