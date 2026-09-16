# Start a language server for a project that has not been saved yet

## Why

A project that has never been saved gets no language server at all, and is told nothing about why.

`ProjectService.ProjectFilesDirectory` answers for an unsaved project with a scratch path under TEMP. It
*mints and memoises* that path and creates nothing — the four call sites that write a file into it call
`Directory.CreateDirectory` at that moment, which is the correct owner. So between File → New Project and
the first save, the directory is a real string naming nothing.

Both launching transports pass that string straight to `ProcessStartInfo.WorkingDirectory`, which throws:
`The directory name is invalid`. The transport catches it and returns no handler, `IsRunning` is false, and
the registry records the connection as `Failed` — which is **terminal for the session** by an argued
decision. Open a brand-new Standard EXE, view the code for `Form1`, and that is an IDE with no diagnostics,
no definition, no rename, for as long as it stays open.

### The issue understates it, and the correction is the interesting part

hexide-io/HexIDE#278 reasoned that `%TEMP%/hexide_Project1` "already exists on any machine where a new
project has ever been saved once", making this a first-run hazard. That was true when it was written and is
not true now. hexide-io/HexIDE#260 fixed a *different* fault — every unsaved project called `Project1`
shared one directory, so adding a `Module1` destroyed the previous session's — by appending a GUID. The
path is consequently unique per project and **never exists before the first write, on any machine, ever**.

So one fix turned a first-run hazard into an every-new-project one. Neither issue is wrong; the interaction
is simply not visible from either. It is worth recording because the hazard got quietly worse while both
issues sat open, and because it explains why nobody noticed: the documented MCP dev loop passes
`--newproject`, which saves immediately, so the state never arises there.

## What Changes

- A new `LspLaunchDirectory` in `HexIDE.Lsp` decides where a launched server *runs*, and declines a
  workspace directory that does not exist, falling back to the empty string — which hands the child the
  IDE's own working directory, exactly as .NET does when none is given.
- Both transports call it. They had two textually near-identical copies of the rule, and the pipe one's own
  remark described it as "the same rule the stdio transport applies" — a rule asserted twice is a rule by
  coincidence. `LspWorkspaceUri` is the precedent and its docstring is the argument.
- The workspace the server is told to **analyse** is unchanged. See below; this is the point of the change,
  not an omission from it.
- Both transports now name the working directory when a launch fails, and the stdio one logs it on the way
  in as the pipe one already did.

### Fall back, do not create the directory

The alternative fix — create the scratch directory before launching — was considered and rejected. The
strongest argument against it is that this codebase already wrote it down, in the stdio transport's own
docstring for the no-workspace case:

> Empty when there is neither — which hands the child the IDE's own working directory. That is not a good
> root, but it is the one .NET uses when none is given, and inventing a temp path instead would silently
> point a server's configuration lookup somewhere the user has never heard of.

Creating `%TEMP%/hexide_Project1_a3f…` *is* inventing that path. Taking the other branch would require that
comment to be rewritten to say the opposite, which is the tell.

Three further costs. **Nothing in the tree ever deletes a scratch directory**: today a directory there means
real content, because only a write creates one; creating one per server start would leave an empty
GUID-named directory behind for every unsaved project ever opened, with no owner to reap it. It would put
**filesystem I/O behind a hot static property** — the workspace reads it on every `didOpen`, and the unit
suite calls it a dozen times purely to assert a path's shape, which would then litter TEMP on every run.
And it would **buy no guarantee anyway**: the directory can vanish between the create and the start, so the
honest failure message is needed either way.

### The workspace root deliberately still names the directory

`rootUri`, `workspaceFolders` and the `{workspaceUri}` placeholder continue to answer with the scratch path
even while it is empty. This is not a compromise — it is the separation the pipe transport already argues
for at length, having got it wrong in its first draft:

> Deliberately not `WorkingDirectory`, though the first draft of this used it. The two answer different
> questions and only coincide by accident.

"Where the process runs" and "which tree it analyses" are different questions. The scratch path is the right
answer to the second before it is the right answer to the first: it is where the project's files will be
written the moment the user adds a module, and it is the parent of every document URI the server will
receive. Withholding it would instead drop the project from `workspaceFolders` and leave every document
outside any root — a far larger behaviour change than the one asked for, and one that would re-root every
server the instant the user added their first module.

### An explicit working directory is still not second-guessed

The existence check applies to the workspace branch only. A directory somebody named in configuration is
their intent, and a name that does not resolve is an error they can fix and must be told about. Silently
inheriting there would start the server against the wrong tree — *"it would start, report cleanly, and
answer every question about the wrong tree. A wrong answer, not a failure"* — which is the outcome the whole
split exists to prevent.

### The error message was wrong at both sites

Windows renders a bad working directory as `The directory name is invalid` **without naming the directory**.
So the stdio transport reported `could not start 'HexIDE.VbLspServer': The directory name is invalid`,
pointing at the executable for a fault that had nothing to do with it. The pipe transport was worse: its
`Process.Start` sits inside the connect `try`, so the same fault surfaced as `could not use pipe '<name>':
The directory name is invalid`, blaming the pipe. Both now carry the working directory. Still needed after
the guard, for the directory that vanishes between the check and the call, and for one this process cannot
read.

## Impact

- `openspec/specs/lsp-client/spec.md` — one requirement modified. The behaviour it states is now conditional
  where it was absolute, and it gains the launch/analyse distinction it was silent about.
- `IDE/HexIDE.Lsp/LspLaunchDirectory.cs` — new.
- `IDE/HexIDE.Lsp/Transports/StdioProcessLspTransport.cs`, `NamedPipeLspTransport.cs` — call it; message and
  logging corrections.
- `IDE/HexIDE/Projects/ProjectLspWorkspace.cs` — its documentation described the pre-#260 shared-path
  behaviour and said the directory "may hold another project's leavings", which is now the reverse of the
  truth and was the premise this change turns on.
- Three tests, each mutation-proven against a build with the check removed.

### Deliberately out of scope

- **Recovery from `Failed` when the workspace moves.** `LspClientRegistry` treats `Failed` as terminal for
  the session, with the argument that nothing has changed to make the next attempt more likely to work. That
  argument is genuinely weaker on the workspace-moved path, because a workspace move *is* such a change.
  Real, independent of this, and it touches the session-lifetime state machine. This change removes the
  reason to reach `Failed` here rather than papering over the gate.
- **A cleanup owner for `%TEMP%/hexide_*`.** Nothing reaps them; this change adds none. Larger than it looks,
  because a second IDE instance must not reap a live one's directory.
- **Surfacing "the working directory was not usable" in the connections list.** The `TransportNotice` seam is
  the right long-term home and this is arguably where it belongs, but it is a UX change carrying a visual
  verification obligation. A log line is the honest minimum here.
- **Whether a server minds a `rootUri` naming a directory that does not exist.** Unverified against the five
  foreign servers, none of which is rooted at an absent directory by any test. If one is observed objecting,
  that is its own issue with its own foreign-server test; guessing at it here would be inventing a
  requirement.
