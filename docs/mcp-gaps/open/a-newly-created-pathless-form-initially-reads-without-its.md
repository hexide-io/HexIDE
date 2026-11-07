# A newly created pathless form initially reads without its header (#885)

**Symptom, since #273 task 3.18.** Read in code, not run. `get_file_content`
returns a form's whole file, except a form created in this session and not yet saved or changed in the designer,
which has no header yet, so its text is its code alone; the tool's description says so. Its first committed designer
change or save gives it one (`HeaderRefresher.ApplyHeader`), its marks move down by the header's height, and from then
its lines count from the top of that header. So a line number read before then is out by the header's height after
it. **Workaround:** read the content again after any designer change or save before using a line number from it.
Where a created form's first header should come from, so that it holds one from the start, is
[#885](https://github.com/hexide-io/HexIDE/issues/885).
