# Retire the Form Layout window's design record without migrating it to a capability

> Written at conversion time (2026-08-16), when HexIDE's specs were migrated to the OpenSpec format. This
> change modifies no specs deliberately (`skip_specs: true`) — its purpose is to preserve a design record
> for a feature that is **not** getting a capability spec, and to say why.

## Why

The Form Layout window is a miniature of the desktop showing where a form will appear when it starts, with
a draggable thumbnail for setting that position. It is a VB6 signature — and the one every IDE built since
has dropped.

The reason it was dropped is that the thing it configures stopped being meaningful. It presents a single
screen at a fixed aspect ratio and asks the developer to place a window on it, which described how
applications ran in 1998. It does not describe multiple monitors, a window manager that positions windows
itself, a scaled display, or a machine whose screen layout differs from the one the developer authored on.
The property it edits still exists and still round-trips; setting it by dragging a thumbnail across a
picture of one monitor is what no longer earns a permanent place in the default workspace.

It is therefore on the removal list for the modern default experience, decided during the pre-launch surface
audit — and that is exactly why writing a capability spec for it would be wrong. A spec in `specs/` is a
contract that the current behaviour is intended and should be preserved; writing one for a surface already
slated for withdrawal would commit the project to keeping something it has decided to remove, and the next
person to read it would have no way of knowing that.

## What Changes

- The `form-layout-window` design record is retired from `openspec/specs-pending/` without becoming a
  capability under `openspec/specs/`. This change is its replacement as the historical record.
- No specs are added, modified or removed. The window's code is untouched.

## Impact

- `openspec/specs/` gains no `form-layout-window` capability, by design.
- **The window still ships.** As of this change the tool window exists, is reachable from the View menu and
  the Standard toolbar, and both parts of its original plan are implemented: the monitor illustration takes
  the primary display's real aspect ratio, and the thumbnail reflects the form's startup position and size
  rather than being drawn at a fixed place. So `specs/` deliberately has no contract for a surface that is
  currently present — the gap is the point, not an oversight.
- Actually removing it is separate work and belongs in its own change, which is where the removal's
  consequences (the View menu entry, the toolbar button, the command, the dock layout slot, and the
  localization keys behind them) get enumerated.
- If the capability comes back, it should come back as an add-in rather than as built-in chrome — which is
  also the cleanest demonstration that the add-in surface can carry a real tool window.
