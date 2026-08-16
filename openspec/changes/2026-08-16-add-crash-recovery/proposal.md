# Add crash recovery for unsaved work

> Converted from the `crash-recovery` design record (2026-08-16). **Nothing here is built** — the only
> trace in the tree is a comment on the file-watcher's baseline store anticipating it — so this is a change
> rather than a spec.

## Why

A clean exit already prompts to save. The gap is the unclean one: a crash, a kill, a power loss. There the
IDE simply disappears with whatever was unsaved, offering nothing on the way back.

The project has no autosave by design — VB6 did not have one either, and the explicit-save model is
deliberate. But "we do not autosave" and "we lose your work when we crash" are different claims, and only
the first is defensible. Backing up unsaved work without touching the real files preserves the model and
removes the loss.

## What Changes

- Back up unsaved editor content to a private per-session store, on a timer and after edits settle.
- Detect an unclean exit, and offer to restore when the affected project is next opened.
- Identify the project by a stable id rather than its path, so a recovered session still matches after the
  folder has been moved or renamed.
- Warn on restore where the file on disk has changed since the crash, reusing the file-watcher's baseline.
- Extend the same treatment to unsaved designer layout, and surface the project id so it is discoverable.

## Impact

- New capability: `crash-recovery`.
- Reuses the per-file baseline the file-watcher already maintains — it was shaped for this, and this is its
  first other consumer.
- Adds a stable project identity, which is currently recovery-scoped. Re-keying the user sidecar or the
  recent-projects list on it is a plausible follow-on and deliberately not in this change.
- **Backup only.** Real project files stay written solely by explicit save; nothing here changes what a save
  does or when it happens.
- A project never saved cannot be recovered — with no project file there is nothing to match a recovered
  session against when the IDE reopens. A documented limitation rather than a gap to close.
