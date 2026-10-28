# `invoke_format_command`'s no-op reply states a selection rule the spec contradicts

**Symptom.** With one control selected (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf,
2026-10-13):

```
interact {"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/Pane[#0]/Custom[Root]/Pane/None[FormContainer]/List[ControlsContainer]/ListItem[Command1]","action":"select"}
invoke_format_command {"command":"AlignLefts"}
→ "Nothing changed, and no undo step was added: the selection is Command1. The spacing commands need two or
   more selected controls, the others at least one, and a selection that already satisfies the command is
   left as it is."
```

The spec makes the nine alignment and sizing commands unavailable below two selected controls, and with one they
can never change anything, so "at least one" sends a caller back with the same single selection. The text is
hard-coded and the command table has no per-command minimum. Cross-platform by code reading.

**Workaround.** Before any Align* or MakeSame* command, select two or more controls with `interact` `select` and
then `add_to_selection`. Read "at least one" as true only of SizeToGrid, CenterHorizontally and CenterVertically.

**Suggested fix.** A per-command minimum in the `FormatCommands` table, a `success:false` refusal below it that
names the rule and points to `add_to_selection`, and the no-op note generated from the same table, so the prose
cannot drift from the code again. [#930](https://github.com/hexide-io/HexIDE/issues/930).

**Fixed (#930).** The command table now supplies each minimum to the refusal and no-op note. Below the
minimum, the tool returns `success:false`, names the command and current selection, and directs the caller to
`interact` `select` and `add_to_selection`. Alignment and sizing predicates and execution guards require two
controls; changing selection invalidates command enablement. Headless regression coverage checks all twenty
command rules and the nine predicates. Live menu/toolbar verification remains for the supervising session.
