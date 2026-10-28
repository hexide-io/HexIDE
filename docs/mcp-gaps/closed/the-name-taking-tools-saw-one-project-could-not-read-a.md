# The name-taking tools saw one project, could not read a carried file, and refused without saying why — **CLOSED** (#572, #573, #575, 2026-09-22)

> **Fixed.** `open_file`, `view_designer`, `get_form_controls`, `get_file_content` and `set_file_content`
> resolve a name across every loaded project, as the mark tools already did, and take the same optional
> `project`. `get_file_content` reads a carried file, from its editor or else from disk, and
> `set_file_content` refuses one by what it is. A refusal says what the name is when it is something else,
> and lists what each project holds.

**Symptoms**, all measured on a group of two projects, `Carried` (`Module1`, carried `Notes`) and `Second`
(`Form1`, `Module1`, `Module2`), with `project` left at null unless shown:

- `open_file {"name":"Module2"}` refused while `get_breakpoints {"name":"Module2"}` answered for
  `Second/Module2`. The five tools looked only in the startup project (#575).
- `get_file_content {"name":"Notes"}` answered "No form or module named 'Notes' found" straight after
  `open_file` had opened it in a tab (#572).
- `view_designer {"name":"Module1"}` answered "No form or UserControl named 'Module1' found in the project".
  That reads as a wrong name, when the name was right and the tool was not, and like every other miss it
  never said what the project did hold (#573).

**Now:**

```
view_designer {"name":"Module2"}              → 'Module2' is a module, which has no designer. open_file opens its code.
view_designer {"name":"Notes"}                → 'Notes' is a carried file, which has no designer. open_file opens it.
get_form_controls {"formName":"Module1"}      → 'Module1' names more than one document: Carried/Module1, Second/Module1. Pass `project` to say which.
open_file {"name":"Module1","project":"Second"} → success; the tab is "Second - Module1 (Code)"
get_file_content {"name":"Notes"}             → {"content":"# Notes\r\n\r\nA carried file.\r\n","hasUnsavedChanges":false}
open_file {"name":"Nope"}                     → No form, module or carried file named 'Nope' in any loaded project. Carried holds no forms and UserControls; modules Module1; carried files Notes. Second holds forms and UserControls Form1; modules Module1, Module2; no carried files.
```

**Since closed.** `get_project_info` reported only the startup project, which left a refusal as the only
reply that showed the whole group; it now lists every loaded project in `projects` (#581).

**Three tools this did not reach, closed 2026-10-13 (#273).** `set_control_property` still looked in the
startup project only. `move_control` and `add_control` matched the open designers by name and took the first,
so with a group holding two `Form1`s they acted on whichever designer happened to be found first, and never
refused. All three now resolve a name as the five above do, take the same optional `project`, and refuse an
ambiguous name with the candidates listed. `move_control` and `add_control` then look for the designer open on
the document the name resolved to, so a bare `Form1` that used to work while only one of two was open is now
refused, as it is everywhere else. `add_file` takes `project` too, naming the project to add to. The rule moved
out of the tool bodies into `NamedDocuments` in the IDE assembly, where `NamedDocumentsTests` covers the
refusals quoted above. That the tools use it is checked by `ToolDocumentNamingTests`, which reads their source:
a tool that takes `project` must find the name through the lookup and use it for nothing else, so a designer
found by its form's name, which is how `move_control` worked, fails the build.
