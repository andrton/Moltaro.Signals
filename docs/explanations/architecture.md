# How the library is built

An explanation for a developer who uses the library, not a specification. The rules are
[concept §2, "What it is"](../concept.md#2-what-it-is),
[concept §5, "Hosting"](../concept.md#5-hosting) and the wire format in
[docs/protocol.md](../protocol.md). A version of this page with its figure is
[architecture.html](architecture.html). How the library works at run time is the subject
of the other two explanations,
[How a set wakes one member of each group on a key](signal-delivery.md) and
[Why nothing is lost when a connection drops or the host restarts](connection-loss.md).

**Status, 19 November 2026.** Built. Sprints S2 and S3 of the first epic built the host,
`SignalHost`, inside its own process and over TCP; sprint S5 built the client,
`SignalClient`; sprint S6 built the hosting package, `Moltaro.Signals.Hosting`.

## The idea

> One process embeds the host, which keeps every signal in its memory. Every other process
> reaches it through a client, over one TCP connection that carries lines of text. Code in
> either place uses the same interface, `ISignals`, by the same rules.

There is nothing else to run: no broker and no server of its own. The library is two
packages: one that holds all of it and depends on nothing outside .NET, and a thin one that
registers it in a .NET generic host.

## At a glance

The figure, drawn in [architecture.html](architecture.html), has two halves. The upper half
shows one deployment at run time: the host process, and two client processes with one
connection each, with lines of the protocol on each connection. The lower half shows the
two packages and the folders of `Moltaro.Signals`; an arrow points from a part to the part
it uses.

## The processes

- **The host process.** The application process that owns the background work embeds the
  host, `SignalHost`. The host keeps the signals in its memory and nothing else, and listens
  on a TCP port. It is also the `ISignals` of the code in its own process: a set or a wait
  there goes straight to the host's signal delivery, with no socket. There is one host per
  deployment.
- **A client process.** Any other process of the deployment, for example an API or a
  worker. It uses a `SignalClient` as its `ISignals`. The client keeps one persistent TCP
  connection to the host, and all its sets and subscriptions share it: a worker with two
  lanes has two subscriptions on one connection. The client reports whether it is
  connected, with `State` and `StateChanged`, and reconnects by itself.
- **`ISignals`.** The one interface of both. `SetAsync` sets a key; `Subscribe` makes a
  member of a group on a key, whose `WaitAsync` returns when the member should look. Code
  written against it runs unchanged in the host process and in a client process.
- **The line protocol.** What a connection carries: one command per line of plain ASCII
  text, ended by `\r\n`. The client sends `HELLO` first, with the protocol version and a
  shared token, then `SUB`, `WAIT`, `UNWAIT`, `UNSUB`, `SET` and `PING`. The host answers
  with `OK`, `ERR` and `PONG`, and hands a signal to a member with `SIG`.
  [docs/protocol.md](../protocol.md) fixes the grammar.

## The two packages

The split is decision D10 of the [concept](../concept.md#8-decisions).

- **`Moltaro.Signals`** holds the whole library: the public contract, the host, the client
  and the protocol. It depends on nothing outside .NET. A process that creates its host or
  its client itself needs only this package.
- **`Moltaro.Signals.Hosting`** registers the host or a client in a .NET generic host and
  its service container, with one call: `AddSignalHost` or `AddSignalClient`. The call
  registers the host or the client as a singleton, whose options its `configure` fills when
  it is first resolved, and `ISignals` as that same object. A hosted service calls `Start`
  and writes the log lines: when the host listens, when a client connects, and when its
  connection ends or the host refuses it. Registered before the other hosted services, the
  host binds its port before the workers of its process start. When the application stops,
  the hosted service does nothing; the container disposes the host or the client after
  every hosted service has stopped. The package depends on `Moltaro.Signals` and on the
  hosting abstractions of .NET, `Microsoft.Extensions.Hosting.Abstractions`.

## Inside `Moltaro.Signals`

A folder of the project is a namespace and one responsibility, and the dependencies between
them point one way, as principle 10 of [AGENTS.md](../../AGENTS.md) requires: signal
delivery knows nothing of sockets, the protocol codec knows nothing of signal delivery, and
the transport ties the two together.

- **`Delivery`, signal delivery.** The rules of [concept §3](../concept.md#3-semantics) over
  plain data: for each key and group its members, whether a signal is pending, and which
  waiting member a set hands its signal to. It uses neither of the other parts, so each rule
  is tested on it directly, without a connection.
- **`Protocol`, the protocol codec.** It reads and writes the lines of
  [docs/protocol.md](../protocol.md), and holds the rules that both sides check, so that the
  host and its clients check the same limits: the name rule of a key and a group, the token,
  the length of a line. It knows nothing of signal delivery or sockets.
- **The root, namespace `Moltaro.Signals`.** The public contract: `ISignals`,
  `ISignalSubscription`, `SignalHost`, `SignalClient`, their options and their results. And
  the transport, as nested types of `SignalHost` and `SignalClient`: the sockets, the
  heartbeats and, in the client, the reconnect. On the host, the transport decodes each line
  of a connection with the codec, and each `SUB`, `WAIT`, `UNWAIT`, `UNSUB` and `SET`
  becomes a call of signal delivery; a signal that delivery hands to a remote member goes
  out as its `SIG` line. In
  the host process, `SignalHost` as `ISignals` calls signal delivery directly, without the
  codec. The client uses only the codec: signal delivery runs in the host alone.

The arrows point from the root to the two folders and never back, and there is none
between the two folders.

## What an application can rely on

- **One interface, wherever the host is.** Code written against `ISignals` follows the same
  rules in the host process and in a client process; only the line that creates or
  registers the host or the client differs.
- **Nothing else to run.** The host lives in an application process: there is no broker, no
  cluster and no store. If that process is down, its clients report `Disconnected` within
  seconds; when it is back, every member does one full pass.
- **A small core.** `Moltaro.Signals` depends on nothing outside .NET. The hosting package
  adds only the registration in a generic host, which an application without one does not
  need.
- **A wire a person can read.** Each client has one TCP connection, carrying lines of text;
  `nc` is enough to type a session against a host, as the
  [example of docs/protocol.md](../protocol.md#example) shows.
