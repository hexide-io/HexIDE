# `type_text`'s default mode leaves a live TextBox drawn empty, though the app takes the text (#2737)

**Symptom (2026-10-28, #2717's live check, Windows, a main-based Debug build).**
- **The call:** in the Find dialog, `type_text {"target":"Window/Custom/Edit[#0]","text":"Form","window":"Find"}` answered
  `{"success":true,"mechanism":"document","detail":"typed 4 char(s) into TextBox"}`.
- **The app took it:** `inspect_element` reads `"value":"Form"`, and Find Next turned enabled and found `Form`.
- **The box is drawn empty:** in `take_snapshot`, and in an OS `PrintWindow` capture of the window, so the snapshot
  isn't at fault.
- **Effect:** a caller checking its own work by snapshot concluded it had typed into another box.
- **Keyboard mode differs:** `mode: "keyboard"` draws the text.
- **Headless doesn't reproduce it:** a headless `FindReplaceView` draws the same write.

**Workaround.** Pass `mode: "keyboard"` to `type_text` for a plain `TextBox` whose drawing matters.

**Suggested fix.** Find why the one-assignment write in `UiAutomationDriver.TypeText`'s `TextBox` case isn't drawn on a
live, focused box, and draw it. Failing that, say in the reply that the text was set but may not show until the box next
changes, or default a plain `TextBox` to the keyboard path.
