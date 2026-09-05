# Attaching a language server

HexIDE ships a language server for VB6 and will talk to others you attach yourself. Servers are
configuration, not code: you add one by writing a file, and the bundled VB6 server is an ordinary entry in
that file's defaults rather than a special case beside it — so you can replace it, or switch it off.

## Where the file lives

```text
%AppData%/HexIDE/lsp-servers.json          Windows
```

> **Linux and macOS**: this location is currently unreliable — HexIDE resolves it through a folder API
> that returns nothing on Unix unless `XDG_CONFIG_HOME` is set, so the file is looked for relative to
> wherever the IDE was started from. Setting `XDG_CONFIG_HOME` gives a stable answer until
> [#280](https://github.com/hexide-io/HexIDE/issues/280) is fixed. This is not a documentation gap; it is
> a defect, and it is being reported here rather than papered over with a path that only sometimes works.

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
| `command` | for `stdio` | The executable to launch. |
| `arguments` | no | Command line. Part of the command's identity — see *A command you have not run before*. |
| `workingDirectory` | no | Empty means the server runs in whichever project is open, which is usually what you want. |
| `pipeName` / `pipeRole` | for `pipe` | `pipeRole` is `connect` (default) or `listen`. |
| `endpoint` | for `websocket` | e.g. `ws://localhost:1234/`. |
| `displayName` | no | Shown wherever servers are listed. Defaults to the id. |
| `priority` | no | Breaks ties for features that cannot merge two answers, such as formatting and rename. Higher wins. The bundled server sits *below* the default, so your own server wins without you having to know this field exists. |
| `enabled` | no | `false` switches an entry off entirely — no process, no registration. |

### Extensions serve; the identifier names

These two are easy to confuse and they do different jobs.

**`extensions` decides which documents reach the server.** **`languageId` decides what the server is told
they are.** Two servers may claim the same extension and disagree about what to call it — one saying
`python`, another `python3` — and both are right; each has its own connection and hears its own answer.

This matters for HexIDE's own forms and modules, which have no file extension on the wire. A server is
recognised as serving them if it declares VB6 source extensions (`.bas`, `.frm`, `.ctl`, `.pag`) **or**
names the language `vb6` directly. Declaring only `.cls` is not enough, because a `.cls` is equally a
LaTeX class file and reading that as a claim on VB6 would hand a LaTeX server your project's source.

## A command you have not run before

The first time HexIDE is asked to launch a particular command line, it says so rather than launching it
quietly. Typing a path into your own configuration is consent; a file appearing with a path in it is not,
and this file is an ordinary file any process running as you may write. An entry is launched on every
start thereafter, so without this, writing that file would be a durable way to have the IDE run something
indefinitely and silently.

The **arguments are part of the command's identity**, not just the executable: `node` is harmless, and
`node /tmp/something.js` is whatever that script says.

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
