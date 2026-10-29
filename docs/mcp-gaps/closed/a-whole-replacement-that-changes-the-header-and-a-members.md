# A whole replacement that changes the header and a member's `Attribute` lines is told only about the header

**Symptom.** Read from the code, and recorded when #273 task 4.5b was built. `set_file_content` (and an add-in's
`TrySetContent`) compares the header first and returns on the first difference:

```
set_file_content {"name":"Ledger","content":"<a class header with one line changed, and Total's run changed>"}
→ refused: "The content would change the document's header (its designer block, class header or leading
   Attribute lines), which only the IDE changes. ..."
```

A caller that restores the header and sends again is refused a second time, now for `Total`'s run, a reason the
first reply did not give.

**Workaround.** After a header refusal, compare the members' `Attribute` lines with `get_file_content`'s before
sending again.

**Suggested fix.** Compare the runs whatever the header does, and refuse once with every finding.
[#1054](https://github.com/hexide-io/HexIDE/issues/1054).


**Fixed (#1054).** The shared replacement guard compares member runs even when the header differs. One refusal
names the header first, then every changed, added or removed member run, with the existing repair guidance.
`DocumentContentTests` covers all three findings alongside a header change and verifies the document stays unchanged.
