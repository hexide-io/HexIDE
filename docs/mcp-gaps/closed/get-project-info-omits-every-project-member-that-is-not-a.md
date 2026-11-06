# get_project_info omits every project member that is not a form or module — **CLOSED** (#362, 2026-09-22)

> **Closed (#362).** `get_project_info` returns `relatedDocuments` beside `forms` and `modules`
> (`ProjectInfoResult`), seen live on 2026-09-22 as `"relatedDocuments":[]`. The suggested single
> `members` array was not adopted, so the next member kind will need this edit again.

**Symptom.** `get_project_info` returns `forms` and `modules` only. A project carrying a related document (a
file it does not compile) reports it nowhere, so after adding one the tool's output is byte-identical to
before.

**Consequence.** The obvious check after an add — call `get_project_info` and see the new member — silently
answers "nothing happened" for a whole member kind. It reads as a failed feature rather than a blind tool.

**Workaround.** Confirm through the Project Explorer instead: `take_snapshot` shows the node and its icon,
and `dump_visual_tree` reports the node's `dataContextType`, which distinguishes a related document from a
module.

**Suggested fix.** Add a `relatedDocuments` array, and prefer a shape that will not need this edit again the
next time a member kind is added — a single `members` array of `{name, kind, path}` would cover forms,
modules, related documents and whatever follows.
