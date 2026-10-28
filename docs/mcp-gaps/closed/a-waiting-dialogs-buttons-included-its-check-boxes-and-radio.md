# A waiting dialog's buttons included its check boxes and radio buttons — **CLOSED** (#2079, 2026-10-26)

**Symptom (2026-10-26, #534's first-contact baseline, Windows).** Opening Tools > Options gave
`'Options' (Prompt for project/Reload files changed outside the IDE/Reset All/Restore Defaults/OK/Cancel), a modal
dialog over 'Project1 - HexIDE [design]', opened during this call …`. The first two are check boxes on the page that
was showing, not ways to answer the dialog. All three runs that opened Options showed it.

**Cause.** `OpenDialogs.DescriptionOf` listed every `Button` it found, and Avalonia's `CheckBox` and `RadioButton`
derive from `ToggleButton`, which derives from `Button`.

**Fixed (#2079).** The list leaves out every `ToggleButton`. The five texts that describe a waiting dialog all go
through `DescriptionOf` (#1975), so all five gain the fix. Guarded by `OpenDialogsTests`.
