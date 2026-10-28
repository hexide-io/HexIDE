# `toggle` read the state back before work it set off had run, so a refused uncheck was reported as done — **CLOSED** (#2050, 2026-10-26)

**Symptom (2026-10-26, #1103, Windows).** The Components dialog refuses an uncheck of a library a form uses, and puts
the box back from posted work. `interact {"window":"Components","target":"…/CheckBox[Microsoft Windows Common
Controls 6.0 (SP6)]","action":"toggle"}` answered `toggled 'Microsoft Windows Common Controls 6.0 (SP6)': it is
now off`. After the refusal's message box was answered, `inspect_element` read `"toggleState": true`.

**Fixed (#2050).** On the IDE's own windows, `toggle` now reads the state back once the dispatcher has run what is
queued ahead of `Background`, so a box put back from a posted callback is reported as put back: `…, but its handler or
a binding put it back: it is still on`. A refusal that waits for its message box to be answered can't be known
before the reply, so when a dialog opened during the call the reply adds `a dialog opened during this call, which
modalDialog names, and the state may change again once it is answered`. A running form's toggle was
already read once its program had run (#1280). Guarded by `ToggleSettledTests`.
