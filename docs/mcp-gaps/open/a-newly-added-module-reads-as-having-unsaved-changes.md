# A newly added module reads as having unsaved changes although its file matches

**Symptom.** On a saved project, `add_file {"name":"Helpers","type":"Module"}` succeeds and writes
`Helpers.bas`. Then `get_file_content {"name":"Helpers"}` (`project` left at null) answers
`{"content":"","hasUnsavedChanges":true}`, although saving would write the same bytes. The IDE's own
`invoke_menu_item {"path":"Project/Add Module"}` does the same for `Module3`. An untouched module loaded from
disk reads `false`.

**Resolved (#500, #597).** #489 measured that a new document has no file until it is saved, and #500
removed the immediate write. The reply now correctly reports `hasUnsavedChanges: true` for a new
form, module, class, UserControl or PropertyPage. `get_file_content`'s description explicitly says that
a document with no file yet always has unsaved changes. Save Project asks where to write it; after
saving, an untouched document reads `false`. No render baseline should be recorded at creation.

`WriteAtSaveTests.A_new_member_has_unsaved_changes_until_its_first_save` covers the transition for all
five document kinds, in both saved and unsaved projects. The calls above record the original symptom,
before #500; they no longer describe a file written by `add_file`.
