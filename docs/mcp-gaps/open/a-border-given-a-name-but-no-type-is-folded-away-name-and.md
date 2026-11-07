# A border given a name but no type is folded away, name and all, so `match` cannot find it

**Symptom (2026-10-22, #157, headless).** The designer's box for a control HexIDE does not model was a
`ClassicBorderDecorator` with `AutomationProperties.Name` set to the control's name. `dump_visual_tree`'s `match`
path, `UiAutomationDriver.Find(UiAutomationDriver.Dump(window, "Window", 60, interactiveOnly: false), "imlIcons", 10)`,
the code `{"match":"imlIcons"}` runs, found nothing. A border's control type is `None`, it has no provider and takes no
focus, so the control view hides it as plumbing, and the name it was given goes with it. This is the rule
[#1436](https://github.com/hexide-io/HexIDE/issues/1436) is about, reached from the other side: a name set on purpose is
no more plumbing than an AutomationId.

**Workaround.** Give the element a type: `AutomationProperties.ControlTypeOverride="Pane"` keeps it, as `VBProps` does
for a running form's Frame, PictureBox, Label and Image.

**Suggested fix.** Count an explicitly set `AutomationProperties.Name` as the AutomationId already counts, so a view's
author is not left to discover the override. The path-stability part of #1436 is fixed; this explicit-name
discovery gap remains open.
