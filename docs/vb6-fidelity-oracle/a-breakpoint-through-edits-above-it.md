## A breakpoint through edits above it, an Undo, and its own line's deletion (2026-11-21, #1053, IDE, SP6)

**The question.** HexIDE stores a breakpoint as a bare line number, and nothing moves it when lines are added or removed
above it, so it comes to mark another statement (#1053). Does a VB6 breakpoint follow its statement through such an
edit, an Undo and a paste?

**The probe:**
- **The IDE:** VB6.EXE 6.00.9782 (SP6), in the console session of a clone of the oracle guest. It was driven through
  the console agent, as "Reaching the VB6 *IDE*, not just the compiler" in the README describes.
- **Each row was a fresh IDE:** open the project, open the module's code window, put the caret on `L "two"` and press
  F9. Then make the row's edit in design mode, and press F5.
- **The keys** went through `SendKeys`. No code was typed, so no syntax check or completion list could take a key.
- **What was recorded:**
  - the caret's line after the F9, which was `L "two"` in every row;
  - a screenshot before and after the edit;
  - the IDE's title, the caret's line and the log after F5.
- **Builds:** this is IDE behaviour, so there is no native or p-code build to compare.
- **The program:** a Standard EXE whose startup is `Sub Main`. `L` appends a line to a log.

```vb
Option Explicit
Dim n As Integer

Sub Main()
    L "one"
    L "two"
    L "three"
    L "four"
End Sub
```

**VB6's Home went to the line's first non-blank character,** not to column 1. The edits show it: a first try at the
paste row, with Home in place of End, copied `L "one"` with the next line's indent and pasted it after `Sub `. That
matters for the rows marked †, whose edits started there.

| Edit, after F9 on `L "two"` | After F5 | Log at that point |
|---|---|---|
| **Control:** none | [break] on `L "two"` | `one` |
| A blank line inserted above it: End on `L "one"`, then Enter | [break] on `L "two"` | `one` |
| The line above deleted: from the end of `Sub Main()`, Shift+Down and Shift+End, then Delete | [break] on `L "two"` | nothing |
| A copy of `L "one"` pasted below `L "one"`, so above the breakpoint | [break] on `L "two"` | `one`, `one` |
| A blank line inserted above it as before, then Ctrl+Z | [break] on `L "two"` | `one` |
| Enter at its own line's first character †, which moves the statement down a line | [break] on `L "two"` | `one` |
| Its own line deleted: Home †, Shift+Down, Delete | **no break.** The run ended | `one`, `three`, `four` |
| Home † on `L "one"`, Shift+Down, Delete. This takes `L "one"`, its line end and `L "two"`'s indent, so it joins `L "two"` onto `L "one"`'s line | **no break.** The run ended | `two`, `three`, `four` |

In the screenshots, the breakpoint's dark red highlight sat on `L "two"` after the paste, one line lower than before.
After the last row's edit, the margin showed no breakpoint at all.

**Readings:**
- **A VB6 breakpoint belongs to its statement, not to a line number.** It moved with the statement when a line above was
  inserted, pasted or deleted, and when Enter pushed the statement itself down a line. It stayed with it through Undo.
- **Deleting the statement's line removes the breakpoint.** No other line inherited it.
- **An edit that joins the breakpoint's line onto the line above also removed it,** although the statement survived. No
  rule I would defend: the edit also deleted the line end before the statement, which may be what VB6 treats as
  deleting the breakpoint's line.

### In HexIDE

Per #1053, breakpoints and bookmarks are stored as bare line numbers (`BreakpointService`, `BookmarkService`), and
nothing moves them when text changes above them. That wasn't checked again for this page.

### Not measured

- bookmarks, which #1053 also covers;
- an edit made in break mode, or with the code window closed, as an add-in's edit is;
- an edit in another procedure than the breakpoint's, and a breakpoint in a form's module;
- whether a breakpoint survives a save, or closing and reopening the project.
