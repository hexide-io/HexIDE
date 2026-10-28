# `set_file_content`'s reply does not say when its save rewrote a member's attribute qualifier

**Symptom.** `set_file_content` saves as the document's own Save does, and since #273 task 4.4 that save writes each
member's attribute run with its member's name, as VB6's own Save does whatever the module's state. Content holding
`Attribute Gone.VB_Description` under `Public Function Total()` is therefore written as `Attribute
Total.VB_Description`, and a no-op write-back of what `get_file_content` returned can change the file in that one
place. The reply says the file was written and nothing more, so a caller comparing the file with what it sent finds
a change it did not make, with no reason given.

**`set_control_property` and `add_control` save the same way, and say no more.** Read in code, not run: both save
through `ProjectService.SaveForm` or `SaveModule`, as `set_file_content` does, and both reach `SaveFiles` with
`ownCommand: true`, `SaveModule` through `SaveModuleCore`. So a property set on a form or UserControl, or a control
added to it, also writes a stale qualifier in its code with its member's name, and neither reply says so. #1043
names `set_file_content` alone.

**Workaround.** Read the file back with `get_file_content` after the write, and expect a stale qualifier to have been
re-qualified.

**Suggested fix.** Have the save report what it rewrote and say it in the reply's `note`, not the description, which
is within about 60 characters of its cap. [#1043](https://github.com/hexide-io/HexIDE/issues/1043).


**Fixed (#1043).** The form and module save paths collect the old and new qualifiers they rewrite. All three
tools append that report to a successful reply’s `note`, explaining that VB6’s own Save writes the member’s
name. Headless save tests cover both paths; source guards check all three tool replies. Live verification
remains with the supervising session: on a fresh stale run for each tool, check the note names `Gone` and
`Total`, and read back `Attribute Total.VB_Description`.
