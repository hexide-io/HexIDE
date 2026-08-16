# Tasks

## 1. Preserve the record
- [x] 1.1 Capture what the window shows and how the thumbnail is derived, in `design.md`.
- [x] 1.2 Record why it is not becoming a capability: it is slated for removal, and a spec in `specs/` is a
      commitment to keep the behaviour.
- [x] 1.3 Record what an eventual removal has to account for beyond the tool window itself — the menu entry,
      the toolbar button, the command, the dock slot, and the localization keys in every shipped pack.

## 2. Retire the design record
- [x] 2.1 Remove `openspec/specs-pending/form-layout-window/` in the same commit as this change, so the two
      never disagree.
- [x] 2.2 Sweep inbound references to it.
- [x] 2.3 Leave the window's code untouched — it still ships, and removing it is separate work.
