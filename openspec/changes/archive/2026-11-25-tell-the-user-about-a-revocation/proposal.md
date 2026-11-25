# Tell the user about a mid-session revocation, and show the check's state (#5577)

## Why

The hourly check (part 1) withdraws a revoked add-in without saying so. The add-in's code stays loaded until HexIDE
closes, so the user needs to know to close it. And a check that has stopped working was invisible
([#5577](https://github.com/hexide-io/HexIDE/issues/5577)).

## What changes

- **A notice** names each revoked add-in whose code is loaded, and offers to close HexIDE through the normal close, with
  its prompts to save. The maintainer chose this on 2026-11-25 over a relaunch, which HexIDE has no facility for. One
  notice shows at a time, and add-ins revoked while it is open are named in the next.
- **Options → Add-Ins** says one of three things:
  - when the list was last checked, and which list is in use;
  - that the last check failed, and when one last worked;
  - that checking is off.
- **A revoked add-in's page** offers no Activate or Deactivate button, and an open page shows the revocation at once.
