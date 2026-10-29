# `set_ide_language`'s Keep didn't save the language — **CLOSED** (#2268, 2026-10-27)

**Symptom (2026-10-27, rdcore's live check).** `set_ide_language` opened the confirmation gate, and Keep was pressed on
it. The IDE stayed in the new language for the session, and the next start came back in the one saved before, *System
default*. The reply said only that the gate "reverts on its own unless Keep is pressed", which a caller reads as Keep
being the whole of confirming.

**Cause.** The gate's Keep only leaves the language applied. The Options page saves it when the dialog closes with OK
(`LanguagePageViewModel.SaveToSettings`), and the tool has no OK to press. The localization spec's *Confirming the
change* says a confirmed language persists across restarts.

**Fixed (#2268).** The tool saves the language to `ActiveLanguage` when its gate closes on Keep. Revert or the countdown
saves nothing. It saves in the tool and not in the gate service, because there Cancel in Options still takes back a
kept language. The reply says what Keep and Revert will do. Asking for the language already in use opens no gate, and
the reply now says so, where before it claimed a gate was open. Guarded by `SetIdeLanguageSavesTests`.

**Live (2026-10-27, Windows, a Debug build of the fix, headless `claude -p` through the hexide tools, in the session's
own profile).**

- `set_ide_language {"id":"fr"}` replied `Switching to French ('fr'). A confirmation gate is open: Keep saves French
  for later starts, as OK in Options does, and Revert or its countdown goes back to 'system' and saves nothing. …`.
- `interact` invoked `Conserver  /  Keep`. The profile's `settings.json` then held `"activeLanguage": "fr"`.
- After `shutdown_ide` and a relaunch, the menu bar and status bar were French from the start.
- `set_ide_language {"id":"fr"}` then replied `French ('fr') is already the IDE's language, so no confirmation gate
  was opened. It is saved for later starts.`, and no dialog was open.
- The check also found the Properties description staying in English after the live switch, which is #2270.
