# Redirect the user data directory

## Why

Every per-user file HexIDE keeps lives in one directory — settings, window layout, recent projects,
`lsp-servers.json`, add-in consent and revocations, user translations — and there has been no way to point a
session anywhere else. Anything that needs a configuration of its own therefore has to edit the user's real
one: a demo that attaches a foreign language server, an automation run, an experiment with a server entry.
Each leaves the user's settings changed, and depends on somebody remembering to change them back.

That is not hypothetical. Preparing a screenshot of a foreign server's folding and diagnostics meant switching
off the bundled VB6 server in the user's own `lsp-servers.json`, because the IDE merges every server's
diagnostics and the picture would otherwise show two servers' opinions. The switch outlived the screenshot.

The cost is low because of an earlier fix. hexide-io/HexIDE#280 routed every per-user path through one place
so that Unix stopped writing them relative to the working directory, which leaves exactly one seam to redirect.

### A second defect, found on the way

HexIDE moves its working directory to its own folder before it reads the command line, so a relative project
path was resolved against the executable's folder rather than the shell it was typed in. Launched from `IDE/`
with `../demo/spring-tide/SpringTide.vbp`, the IDE opened no project and said nothing. A relative
`--user-data-dir` would have inherited the same fault, and the fix is the same line, so it is fixed here.

## What changes

- A `--user-data-dir <path>` option puts every per-user file for the session in that directory.
- A relative path on the command line — that option's, or the project's — is resolved against the directory
  HexIDE was started from.
- `--user-data-dir` given without a directory refuses to start, with a non-zero exit and the reason. It is the
  one flag where skipping a malformed argument, as every other flag does, would do the opposite of what was
  asked.

## Deliberately not in this change

**Safe mode.** Tempting to define as "an empty profile", and not equivalent to one: add-ins install beside the
executable and only their consent lives in the per-user directory, so a fresh directory prompts for every
third-party add-in rather than skipping them. Safe mode needs its own decision about what it switches off.

**Profiles.** Safe mode, a profile directory and `--personality` are one axis — which bundle of settings loads
— and `--personality`'s fixed list of names is the wrong shape for it. That is larger work, and it waits on
the things a profile would bundle becoming data rather than code (theme packs, #413; keymap packs, #414;
toolbars, #419). This option is the "load from a path" case of that axis and is named so as not to close it
off.

**Logs.** Diagnostic output, not per-user state; they stay where they are.

## Impact

- `IDE/HexIDE.Core/IDE/UserDataPath.cs`, and a new `UserDataRoot.cs` holding the redirect's rule where tests
  can construct it without moving the test process's own files.
- `IDE/HexIDE.Desktop/ServerOptions.cs` and `Program.cs`.
- `docs/command-line.md`, `docs/language-servers.md`.
