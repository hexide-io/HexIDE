# 14. `set_file_content` silently dropped a form's `Attribute` header — **CLOSED** (#344, 2026-09-09)

> **The mirror half is superseded, 2026-10-13 (#273 task 3.18).** A whole `.frm` is no longer refused.
> `get_file_content` now returns a form's whole file, designer block included, so a whole `.frm` is what a
> caller round-tripping a form sends back, and refusing the shape would refuse what the read returned.
> `set_file_content` compares the incoming header with the form's instead: a designer block exactly as the
> form has it applies nothing and is accepted, and one that differs is refused with the reason and nothing
> changed. That meets the argument below more exactly than refusing the shape did. The controls a caller
> supplies are still never silently discarded, because a changed block is refused rather than dropped, and a
> designer block never reaches the code, where it would be compiled as VB. Keeping a form's `Attribute` lines
> when the content leaves them out is unchanged.
>
> **And a member's Attribute lines, since #273 task 4.5b (2026-10-14).** A change to the run under a procedure's
> or variable's declaration is refused the same way, with nothing changed, and the refusal names each member whose
> lines changed, were added or were dropped. Moving a run with its declaration, renaming it with it, or deleting
> it with it is not a change. Left as they are (#273's tasks.md, 4.5b): content that changes both the header and a
> run is told about the header alone, and a refusal says lines were removed or added where only what they describe
> changed.

> **Fixed, in both directions — the entry's case and its mirror (#338).**
>
> A form's leading `Attribute VB_*` block is now **kept** when the incoming content omits it, and the result
> says so rather than adjusting silently. Preserving beats refusing here: omitting the block is what a caller
> does when they mean exactly what they said — *replace the code* — and the block is not code.
>
> The mirror case is **refused**: a whole `.frm`, opening with a `VERSION` / `Begin` designer block, comes
> back as an error rather than being written into the code buffer where it is compiled as VB (#338).
>
> **The asymmetry with the module branch is deliberate.** A module's header carries nothing the model does
> not already own, so stripping it loses nothing. A form's designer block describes its **controls**, and
> this tool does not apply them — so accepting the file would either bury the header in the code or silently
> discard the controls the caller supplied. Strip where nothing is lost; refuse where something would be.
>
> Measured live, all three paths, checking the bytes on disk each time:
>
> ```
> set …  "Attribute VB_Name … / Private Sub Form_Load() … one"   → success
> set …  "Private Sub Form_Load() … two"        (no header)      → success + note
>        disk: Attribute VB_Name = "Form1" survives, body is "two"
> set …  "VERSION 5.00 / Begin VB.Form … "                       → refused; disk unchanged
> ```


**Symptom.** Writing a fresh body to a form removes its attribute block:

```
-Attribute VB_Name = "frmBillOfFare"
-Attribute VB_GlobalNameSpace = False
-Attribute VB_Creatable = False
-Attribute VB_PredeclaredId = True
-Attribute VB_Exposed = False
```

No warning, no error, and the change is written straight to disk — `hasUnsavedChanges` reads `false`
afterwards, because as far as the IDE is concerned the save succeeded.

**How it bit.** Using `set_file_content` to drop a few probe lines into `demo/bill-of-fare`'s form for a
live check. It replaced the whole code section, taking the header and every event handler with it, and the
damage reached a commit before it was spotted in `git diff`.

**Not a defect in the tool.** `get_file_content` returns the attribute block too, so the pair is
self-consistent and a **get → modify → set** round-trip preserves everything. The trap is that "the VB6
source code of a form" *includes* the `Attribute` header, which is easy to omit when replacing only the
procedures. VB6 hides it, but HexIDE showed it even before #273 made it part of the folded, read-only
header. The original entry incorrectly called it invisible in HexIDE's editor. `VB_Name` is load-bearing.

**Workarounds.** (a) Always `get_file_content` first and edit the returned text. (b) For a throwaway probe,
use a scratch project (`--newproject`) rather than a demo or a real one. (c) `git status` before committing
after any live verification — that is what caught it here, one commit late.

**Suggested fix.** Either preserve the `Attribute` block when the incoming content has none — the IDE knows
the form's name and can re-emit the header it just parsed — or refuse the write with "content is missing
the Attribute header; call get_file_content first". Silently accepting a body that destroys a form's
identity is the one behaviour that should not be available.
