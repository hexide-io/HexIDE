# Take out what an add-in registers, whenever it registers it (#5661)

## Why

When an add-in was revoked mid-session (#5577), a tool window or Options page it registered after `Initialize` stayed until
HexIDE restarted. So did its later commands, menu items and project templates, though the owned host kept the commands
and menu items from running. Disabling an add-in, or revoking its consent, took out only its language servers and the
other tracked kinds at once. Everything else stayed until restart
([#5661](https://github.com/hexide-io/HexIDE/issues/5661)).

## What changes

- **The owned host tracks every command, menu item, tool window, Options page and project template,** whenever the add-in
  registers it, as language servers already are. Revoking, disabling or revoking consent takes them out at once. One
  registered after that is taken out as it is made.
- **Options says so.** After disabling or revoking consent, a note says the add-in's menus, commands, windows and
  language servers are gone, and that its code stays loaded until HexIDE closes. Enabling still says it takes effect at
  the next restart. This was decided by the maintainer on 2026-11-28.
