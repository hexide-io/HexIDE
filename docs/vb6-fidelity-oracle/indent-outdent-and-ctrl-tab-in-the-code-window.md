## Indent, Outdent and Ctrl+Tab in the code window (2026-11-21, #709, design-time, IDE, SP6)

**The question.** Which keys indent and outdent in VB6's code window, and what does each do with and without a
selection? HexIDE bound Indent to Ctrl+Tab, and its Documents switcher took that key first (#709). The issue's mapping,
Indent on Tab and Outdent on Shift+Tab, was from memory.

**The obvious guess, Ctrl+Tab for Indent, was HexIDE's. In VB6, Ctrl+Tab moves to the next window.** Tab is Indent,
and Shift+Tab is Outdent.

### The probe

VB6's IDE, SP6 (VB6.EXE 6.00.9782), in the console session of a clone of the oracle guest, driven through the agent
with SendKeys. The editor options were the guest's own, and were not read. Each case started a fresh vb6.exe on a
Standard EXE project whose module was this:

```vb
Attribute VB_Name = "Module1"
Option Explicit

Sub Main()
    Dim a As Long
    a = 1
    a = 2
End Sub
```

Down 3 and Home put the caret on the `Dim` line, at its first character, column 5: Home goes to the first character
that isn't a space. After the keys under test, Ctrl+A and Ctrl+C copied the module.

| Keys | What changed |
|---|---|
| Tab, nothing selected | the `Dim` line now starts at column 9: four spaces went in at the caret |
| Shift+Down twice, then Tab | `Dim a As Long`, `a = 1` and `a = 2` each moved right by four spaces |
| Shift+Down twice, then Shift+Tab | the same three lines each moved left by four spaces, to column 1 |
| End, then Shift+Tab, nothing selected | nothing |
| Shift+Down twice, then Ctrl+Tab, with Form1's designer also open | the active window went from `Module1 (Code)` to `Form1 (Form)`. A second Ctrl+Tab went back. The text didn't change |

The selection in the second and third rows ran from column 5 of the `Dim` line to column 5 of the `a = 2` line. So a
line the selection only reaches into is indented with the rest.

**The Edit menu** lists `Indent` with `Tab` and `Outdent` with `Shift+Tab`, as a screenshot shows.

### Readings

- **Tab and Shift+Tab are Indent and Outdent.** With a selection, each moves every line the selection touches by one
  indent.
- **Ctrl+Tab switches between the IDE's windows, as in other MDI applications.** It doesn't indent.
- **The first row can't tell typing at the caret from indenting the line.** The caret was at the line's first
  character, where both give the same text.

**Not measured:**
- Tab in the middle of a line's text;
- a selection within one line;
- Indent and Outdent from the Edit menu;
- the Tab Width option's value, which the four spaces suggest is 4.

**In HexIDE.** #709 tracks the keys.
