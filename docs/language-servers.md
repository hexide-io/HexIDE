# Attaching a language server

HexIDE ships a language server for VB6 and will talk to others you attach yourself. Servers are
configuration, not code: you add one by writing a file, and the bundled VB6 server is an ordinary entry in
that file's defaults rather than a special case beside it — so you can replace it, or switch it off.

## Where the file lives

```text
%AppData%/HexIDE/lsp-servers.json          Windows
$XDG_CONFIG_HOME/HexIDE/lsp-servers.json   Linux and macOS, when XDG_CONFIG_HOME is set
~/.config/HexIDE/lsp-servers.json          Linux and macOS otherwise
```

> Earlier revisions of this page warned that the Unix location was unreliable, because HexIDE resolved it
> through a folder API that returns an empty string when `XDG_CONFIG_HOME` is unset — which it is on most
> distributions. The file then landed relative to wherever the IDE was started from.
> [#280](https://github.com/hexide-io/HexIDE/issues/280) fixed that: the paths above are now what HexIDE
> actually uses, and setting `XDG_CONFIG_HOME` is a preference rather than a workaround.

It does not exist until you create it. **Deleting it restores the defaults**, which is the intended way
to undo an experiment that went wrong: the bundled server is compiled in, so no edit to this file can
leave you without VB6 support permanently.

Changes take effect on restart. Data applies live in HexIDE — themes, keymaps, languages — and code does
not; a language server is a process, so it belongs to the second group.

## A worked example

```jsonc
{
  "version": 1,
  "servers": [
    {
      "id": "markdown",
      "displayName": "Markdown language server",
      "extensions": [".md", ".markdown"],
      "languageId": "markdown",
      "transport": "stdio",
      "command": "C:/tools/some-markdown-server.exe",
      "arguments": "server"
    }
  ]
}
```

Comments and trailing commas are allowed — this is a file you edit by hand, not a wire format.

## The fields

| Field | Required | Meaning |
|---|---|---|
| `id` | yes | Identifies the entry. Reusing a default's id **replaces** that default wholesale, keeping its position. |
| `extensions` | yes | The file extensions this server **serves**. This is what routing reads. |
| `languageId` | yes | What this server wants those files **called**, in the protocol sense. |
| `transport` | yes | `stdio`, `pipe` or `websocket`. |
| `command` | for `stdio` | The executable to launch. Optional on `pipe` — see *Starting the server yourself* below. |
| `arguments` | no | Command line. Part of the command's identity — see *A command you have not run before*. |
| `workingDirectory` | no | Empty means the server runs in whichever project is open, which is usually what you want. |
| `pipeName` / `pipeRole` | for `pipe` | `pipeRole` is `connect` (default) or `listen`. |
| `endpoint` | for `websocket` | e.g. `ws://localhost:1234/`. |
| `displayName` | no | Shown wherever servers are listed. Defaults to the id. |
| `trace` | no | How much this server should say about its own work: `off` (default), `messages` or `verbose`. It travels in the handshake, so it sets what the server *starts* at; changing it on a running server is a separate thing the IDE does for you. Misspell it and the server still starts, with tracing off and a warning. |
| `priority` | no | Breaks ties for features that cannot merge two answers, such as formatting and rename. Higher wins. The bundled server sits *below* the default, so your own server wins without you having to know this field exists. |
| `enabled` | no | `false` switches an entry off entirely — no process, no registration. |
| `capture` | no | How much of this server's conversation the protocol capture may hold. See *Giving one server a bigger record* below. |

### Extensions serve; the identifier names

These two are easy to confuse and they do different jobs.

**`extensions` decides which documents reach the server.** **`languageId` decides what the server is told
they are.** Two servers may claim the same extension and disagree about what to call it — one saying
`python`, another `python3` — and both are right; each has its own connection and hears its own answer.

This matters for HexIDE's own forms and modules, which have no file extension on the wire. A server is
recognised as serving them if it declares VB6 source extensions (`.bas`, `.frm`, `.ctl`, `.pag`) **or**
names the language `vb6` directly. Declaring only `.cls` is not enough, because a `.cls` is equally a
LaTeX class file and reading that as a claim on VB6 would hand a LaTeX server your project's source.

## Giving one server a bigger record

HexIDE keeps a record of every language-server conversation, and the amount it holds is capped so a long
session cannot grow without bound. The caps are deliberately uneven-friendly: server traffic is wildly
uneven, and on an identical editing script one server returned around a hundred times another's bytes,
almost entirely completion replies. So each connection gets its own budget, and you can raise the one that
needs it without paying for it everywhere.

Defaults sit at the top of the file; a `capture` block inside a server entry overrides them for that server
only.

```jsonc
{
  "version": 1,

  // Everything not overridden below.
  "capture": {
    "frameBytes": 131072          // keep more of each large message
  },

  "servers": [
    {
      "id": "clangd",
      "extensions": [".c", ".cpp", ".h"],
      "languageId": "cpp",
      "transport": "stdio",
      "command": "clangd",

      // This one is noisy, and only this one.
      "capture": { "payloadBytesPerConnection": 67108864 }
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `envelopeEntries` | How many messages a connection's timeline remembers. An envelope is method, direction, size and timing — no message content — so this is cheap. |
| `prologueEntries` | Entries at the start of a connection that are never discarded, so a startup failure can still be explained after a long session. |
| `payloadBytesPerConnection` | Message bodies retained for one connection, once you have armed it. |
| `globalPayloadBytes` | The ceiling across every connection together. **Top level only** — written inside a server entry it is ignored, and HexIDE tells you so, because one server's entry must not decide what the others may cost. |
| `frameBytes` | The most of any single message that is kept, as a head and a tail with the true length recorded. |

**A value out of range is corrected, not refused, and you are told.** Type a payload budget of four
gigabytes and you get the ceiling, with a line in the servers window naming the field and the value used.
Nothing here can stop the IDE starting.

## Letting HexIDE start a pipe server

A `pipe` entry connects to a server that is already running. Give it a `command` and HexIDE will start
that server itself, then connect to it — which makes the entry self-contained, rather than something that
only works if you remembered to launch the server first.

```jsonc
{
  "id": "example",
  "extensions": [".bas"],
  "languageId": "vba",
  "transport": "pipe",
  "pipeName": "hexide.example",
  "command": "C:/tools/example-server.exe",
  "arguments": "--pipe {pipe} --workspace {workspaceUri}",
  "workingDirectory": "C:/tools/example"
}
```

### Placeholders in `arguments`

| Placeholder | Becomes |
|---|---|
| `{pipe}` | The pipe name. |
| `{workspaceUri}` | The open project as a `file:` URI — the same one sent as `rootUri`. |
| `{workspaceDir}` | The same directory as a plain path. |

**`{pipe}` is the one you will always need**: a server that has to be told which pipe to create cannot be
given a fixed command line.

**The workspace pair is for a server that takes the workspace as an argument** rather than reading it from
the handshake. Without them such an entry could only name one absolute path, so it would serve one project
on one machine — and a `.vbg` group or a second checkout would silently point the server at the wrong tree.

`{workspaceUri}` is spelled by the same code that spells `rootUri`, so the two cannot disagree. That
matters more than it sounds: a server told one directory on its command line and a different one at
initialize does not fail — it loads one workspace and answers questions about the other.

**With no project open, an entry using either workspace placeholder is not launched**, and the reason says
so. Substituting nothing would hand the server a flag with no value and let it fail in its own vocabulary,
which the reader then has to translate back into "no project was open".

### `workingDirectory` is a different question

It says where the server **runs**; the placeholders say which workspace it should **analyse**. They are
easy to conflate and often differ: a server with a required install layout has to be launched from its own
directory, while the code it must read is wherever your project is. HexIDE keeps them separate, so setting
`workingDirectory` never changes what `{workspaceUri}` expands to.

Leave `workingDirectory` out and the server runs in the open project's directory, the same as `stdio`. Set
it when the server needs to be somewhere specific — some servers resolve their own configuration relative
to where they were started, and that is part of whether they work at all.

**A launched server is not reconnected to.** When HexIDE owns the process, a server that dies stays dead
for the session: retrying the same pipe would mean waiting for something nobody is going to start. A pipe
entry with no `command` behaves as it always has, and is re-dialled, because its lifetime is someone
else's business.

It also changes who is announcing what — see below.

## A command you have not run before

The first time HexIDE is asked to launch a particular command line, it says so rather than launching it
quietly. Typing a path into your own configuration is consent; a file appearing with a path in it is not,
and this file is an ordinary file any process running as you may write. An entry is launched on every
start thereafter, so without this, writing that file would be a durable way to have the IDE run something
indefinitely and silently.

The **arguments are part of the command's identity**, not just the executable: `node` is harmless, and
`node /tmp/something.js` is whatever that script says.

This applies to a `pipe` entry that carries a `command` exactly as it does to a `stdio` one. What
decides it is whether HexIDE starts a process, not which transport the entry names — a pipe it merely
connects to is someone else's decision to have run something, and announcing that would be reporting
a fact about a program the IDE did not start.

Nothing is refused — refusing to run what you typed into your own file would be theatre. The launch is
simply not silent.

## Servers start when they are needed

Nothing starts at launch. A server starts when the first document of a language it claims is opened, so a
project containing no Markdown never pays for a Markdown server, and a broken server for a language you
are not using cannot spoil startup for you.

## When a server seems to do nothing

In order of likelihood:

1. **Check the log** — `%LocalAppData%/HexIDE/logs/ide/`. A malformed entry is reported there with the
   reason, the field, and the line and column. A rejected entry does not stop the others from working.
2. **Windows paths need doubled backslashes in JSON**, or forward slashes. A path written with single
   backslashes is not valid JSON at all — the log names the offending field, such as
   `$.servers[0].command`, with the line and column.
3. **Have you opened a file it claims?** Servers start lazily, so a server for a language you have not
   opened is *expected* to be absent.
4. **Does it defer its analysis to save?** HexIDE tells a server about a save only if the server asked to
   be told, which it does through its own capabilities. Nothing to configure — but it explains a server
   that seems to answer nothing until you save.
5. **Check `extensions`, not `languageId`.** Routing reads the first. An entry with the right identifier
   and the wrong extensions serves nothing.

## What is deliberately not here

- **Per-project configuration.** Attaching a server is a decision about what code runs on your machine,
  and a per-project file is written by whoever wrote the project.
- **Discovery of servers already installed.** Nothing scans your machine for language servers.
- **A user interface.** Today this file is the whole of it.
