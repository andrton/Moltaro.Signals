# Moltaro.Signals — protocol, version 1

The wire format between a host and its clients. The semantics it carries are those of the
[concept](concept.md), §3; this document fixes how they travel. It is part of the contract:
a change of it is a change of the protocol version, which `HELLO` carries.

## Lines

- A line is ASCII text, the characters U+0020 to U+007E, ended by `\r\n`. A line holds at
  most 282 characters before its `\r\n`, the length of the longest `HELLO`.
- Fields are separated by exactly one space; there is no space at either end of a line.
- Verbs and codes are case-sensitive: they are written exactly as below.
- Anything else is a protocol violation: the host answers `ERR 0 protocol <text>` and
  closes the connection.

## Fields

| Field | Form |
| --- | --- |
| `<sid>`, `<rid>` | 1 to 19 digits, no leading zero, at most 2^63−1. The client takes the numbers of subscriptions and of requests from one counter per connection and never uses a number twice on it. It never sends `0` |
| `0` | The connection itself: the request that `OK 0` and `ERR 0` answer is the connection's `HELLO`, or the connection as a whole |
| `<version>` | The same form as an id. This document is version `1` |
| `<token>` | 1 to 256 characters from U+0021 to U+007E |
| `<key>`, `<group>` | A name: 1 to 128 characters from `A–Z a–z 0–9 . _ : -`, where `.` separates segments and no segment is empty |
| `<groups>` | `0` or 1 to 10 digits with no leading zero, at most 2^31−1 |
| `<code>` | `version`, `token`, `connections`, `protocol` or `subscriptions` |
| `<text>` | 1 to 128 characters, spaces allowed, to the end of the line: a fixed sentence for a person, which never repeats what the client sent |

A correct client has at most one unanswered `SUB` or `UNWAIT` per `<sid>`, and the host
sends `SIG <sid>` only to an armed member, so each answer names one request.

## Client to host

| Line | Meaning | Answer |
| --- | --- | --- |
| `HELLO <version> <token>` | The first line of a connection | `OK 0`; or `ERR 0 version <text>`; or `ERR 0 token <text>` for another token. A first line made of `HELLO`, a space and a version of the id form other than `1` is refused with `version`, whatever else it holds within the rules of [Lines](#lines); a second `HELLO` is `protocol` |
| `SUB <sid> <key> <group>` | Open the subscription `<sid>`: one member of `<group>` on `<key>`, busy at once, since joining counts as a signal | `OK <sid>`; or `ERR <sid> subscriptions <text>` beyond the host's limit of subscriptions per connection. A `SUB` for a `<sid>` that is open is a violation |
| `WAIT <sid>` | Arm the member; this ends its busy state | `SIG <sid>` when a signal is handed to it, at once if one is pending. A `WAIT` for an armed member is a violation |
| `UNWAIT <sid>` | End the wait of the member: an armed member becomes idle; a busy or idle one stays as it is | `OK <sid>`, after any `SIG <sid>` the host wrote before it |
| `UNSUB <sid>` | Close the subscription; a busy member hands its signal on | None |
| `SET <rid> <key>` | Set the key and each of its ancestors | `OK <rid> <groups>`: the (key, group) pairs over the key and its ancestors that have a member, each counted once |
| `PING` | Heartbeat | `PONG` |

The host ignores `WAIT`, `UNWAIT` and `UNSUB` for a `<sid>` that is not open, so a client
may send `UNSUB` right after a `SUB` it has not seen answered. A correct client sends
nothing after `HELLO` until it is answered, `PING` included.

## Host to client

| Line | Meaning |
| --- | --- |
| `OK 0` | The `HELLO` was accepted |
| `OK <sid>` | The answer to `SUB` or to `UNWAIT` |
| `OK <rid> <groups>` | The answer to `SET` |
| `ERR 0 <code> <text>` | A refusal of the connection, for `version`, `token`, `connections` or `protocol`. The host closes the connection after it |
| `ERR <sid> subscriptions <text>` | The `SUB` was refused; the connection stays open |
| `SIG <sid>` | A signal is handed to the subscription |
| `PONG` | The answer to `PING` |

## Order

- The host handles the lines of one connection in the order they arrive.
- It writes what it sends on one connection in the order of the decisions that cause it.
  The `SIG` lines a line causes come before its answer, and a `SIG <sid>` written before
  the `OK <sid>` of an `UNWAIT` is read before it, so the client knows the signal came
  first.
- A client that ignores a `SIG` for a `<sid>` it has closed loses nothing: the host handed
  that signal on when it removed the member.

## Connections and refusals

- A connection counts toward the host's limit from its accept until its socket is closed.
  One accepted while the host holds its limit is answered `ERR 0 connections <text>` at
  once, before its `HELLO` is read, and closed. If the host has received that `HELLO` but
  not read it when it closes, the close can turn into a reset that discards the line;
  that client then sees only the end of the connection.
- A refusal that closes is written as one `ERR 0` line. After the `ERR 0` line the
  connection takes no more output; the host removes its members, a busy one handing its
  signal on, before it closes the connection normally.
- After `version` or `token` the host has read the `HELLO`, and a correct client has sent
  nothing more, so the client reads the `ERR`.
- When the host is disposed, it closes every connection and writes nothing more.

## Liveness

- The client sends `PING` when it has sent nothing, or received nothing, for its heartbeat
  interval, and at most one `PING` in that time; the host answers `PONG`.
- After `HELLO`, each side closes a connection on which it received nothing for its
  dead-peer time. The host writes nothing before that close.
- A connection that has not sent `HELLO` within the host's dead-peer time, counted from
  its accept, is refused with `ERR 0 protocol <text>`.

## Example

A session typed into `nc`, which sends `\r\n` with `-C`, against a host whose dead-peer
time is long enough for typing, such as one minute. The set of `jobs.mail` reaches the
member on its ancestor `jobs`.

```text
$ nc -C 127.0.0.1 5000
HELLO 1 s3cret
OK 0
SUB 1 jobs workers
OK 1
WAIT 1
SET 2 jobs.mail
SIG 1
OK 2 1
WAIT 1
UNWAIT 1
OK 1
PING
PONG
UNSUB 1
SET 3 bad..key
ERR 0 protocol A name breaks the name rule.
```

The last line is a violation, so the host closes the connection after it.
