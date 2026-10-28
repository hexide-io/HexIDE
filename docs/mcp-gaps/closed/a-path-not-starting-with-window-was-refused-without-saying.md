# A path not starting with `Window` was refused without saying where paths come from — **CLOSED** (#2080, 2026-10-26)

**Symptom (2026-10-26, #534's first-contact baseline, Sonnet 5.5, Windows).** Having seen "Editor > General" in a
snapshot of the Options dialog, it sent `interact {"target":"Editor/General","action":"select","window":"Options"}`
and was refused with `path must start with 'Window' (got 'Editor')`. The refusal was accurate, but it didn't say
where paths come from. Sonnet recovered by loading `dump_visual_tree` on its own.

**Fixed (#2080).** An empty target, and one not starting with `Window`, now end with the next step: `a path comes
from dump_visual_tree, whose every node carries its 'path', and its 'match' finds a control by name`. A guessed
path also gets a call to send as it stands, matching its last segment in the window it was looked for in:
`as dump_visual_tree {"window":"Options","match":"General"}`. Guarded by `PathRefusalTests`.
