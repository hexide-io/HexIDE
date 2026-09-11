# The module gate moves above its picker, and a refused write stops reporting success

## Why

Two halves of #147, both left over after #151 added the refusal to `SaveModule` itself.

**The gate sat below the picker.** `SaveFormCore` refuses *above* its file picker, deliberately — #145
established that the developer must not be asked for a destination that will never be written, and the
merged requirement says so in terms ("no destination is asked for"). `SaveModule` refused *after* its
picker, so Save As on a UserControl HexIDE cannot reproduce asked where to put the file and then declined
to write it. Nothing in the predicate depends on the picker's result, so this was a statement in the wrong
place rather than a design.

**A refused write was reported to an agent as a success.** Both MCP write tools ran
`await ctx.ProjectService.SaveForm(...)` and then returned `MutateResult(true, null)` unconditionally,
because neither `SaveForm` nor `SaveModule` returned anything. A developer who hits a refusal gets a
dialog; an agent gets the tool's answer and nothing else, so the wrong answer is the only signal there was.

**And none of it was tested.** The repo carries no `.ctl` or `.ctx` fixture anywhere, so the refusal gate
had never been exercised on a designer file that is not a `.frm` — for either kind, at any point.

## What Changes

- `SaveModule` splits into `SaveModule` / `SaveModuleCore`, mirroring `SaveForm` / `SaveFormCore`: the
  public one reports a lone refusal at the moment it happens, the core one banks it so a batch still
  produces one dialog rather than N. The batch call sites move to the core.
- The gate is hoisted **above** the picker. A brand-new module is unaffected — it has no designer half yet,
  or a fresh one with no unfaithful causes.
- `SaveForm` and `SaveModule` return whether the file was written, and both MCP write tools report a
  refusal instead of success.
- `.ctl` coverage for the gate: refused on a project save, refused above the picker on Save As, reported
  once when alone, reported to the caller, and — the over-reach guard — an ordinary UserControl still saves.

## Impact

- Adds a requirement to `hexide-mcp-server`: a write tool reports the outcome the IDE actually reached.
- **No change to `serialization-round-trip`.** Its refusal requirement is already file-generic ("a file...
  wherever that file would be written"), so the module case was a violation of an existing contract rather
  than a gap in it. This closes the violation.
- **No new localization keys.** The refusal reuses the existing message; the MCP failure text is a
  developer-facing tool result, not IDE chrome.
- `IProjectService.SaveForm` and `.SaveModule` change from `Task` to `Task<bool>`. Callers that do not care
  are unaffected.
