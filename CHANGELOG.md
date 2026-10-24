# Changelog

All notable changes to HexIDE are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). The leading `0.` is the stability promise:
there isn't one yet. Anything may change between 0.x releases.

## [Unreleased]

### Added

- **Random and Binary files open, and Get and Put run** (#1646). They were refused at the `Open`, so no program that
  keeps records or structs in a file got past it. Measured on vb6.exe first, in eight batches: the bytes of every
  type, String, Variant, array and Type; records and `Len =`; EOF; a second handle and the `Lock` and `Access` clauses;
  and how, after a read past the end, the next `Put` appends where the file stopped. Line Input, Input # and Input read
  a Binary file too.

- **LOF, Loc, Seek and the Input function run** (#1645). Each stopped the program, so `Input(LOF(f), f)`, the usual
  way to read a whole file, never ran. All four were measured on vb6.exe first, with the `Seek` statement, line breaks,
  a Ctrl+Z, the code page and Output and Append files, and run as measured. `Input$` is `Input`; `InputB` waits on byte
  strings (#1510).

- **GetSetting, SaveSetting, DeleteSetting and GetAllSettings run** (#1562). A program that remembered anything between runs used to stop, often in `Form_Load`, before its first form appeared. All four were measured on vb6.exe first. On Windows they use the registry key VB6 itself uses, so a program run in HexIDE and the same program compiled by VB6 share their settings. On Linux and macOS they are kept in `program-settings.json` in HexIDE's user-data directory, which `--user-data-dir` moves.

- **`Like` runs** (#1547). Any program that used it stopped there. It now matches as vb6.exe does: `?`, `*`, `#`, `[list]` and `[!list]`, under the module's `Option Compare`, with error 93 for a pattern it cannot read. That behaviour was measured over about 190 rows, native and p-code. One known difference remains. Under `Option Compare Text`, vb6.exe takes a ligature or a sharp s for the letters it stands for, so `"ß" Like "ss"` is True there and False here (#1479).

- **VB6 filesystem operations** (#1561): `CurDir`, `MkDir`, `RmDir`, `FileLen`, `FileDateTime`, `GetAttr`, `SetAttr`, `FileCopy`, `Name`, `Kill` and stateful `Dir`, using compiled VB6 measurements.

- **Conditional compilation runs: `#If`, `#ElseIf`, `#Else`, `#End If` and `#Const`** (#1112). A module holding any
  `#If` stopped the program as it loaded, and `#Const` did not parse. Now each branch is chosen as vb6.exe chooses it,
  measured with `/make`, native and p-code. The names are the module's `#Const`s, the project's Conditional Compilation
  Arguments (`CondComp`) and VB6's `Win32` and `Win16`. An inactive branch is never parsed, so code written for another
  compiler, such as VBA7's `Declare PtrSafe`, runs unchanged. A directive's error stops the program as it loads, even in a
  branch never taken, as vb6.exe refuses to build. The module saves exactly as written: the selection exists only for the
  parser. Every personality uses VB6's constants for now (#1548).

- **The language server no longer squiggles code vb6.exe never compiles** (#1112). An inactive `#If` branch is no longer
  parsed, so a VBA7 `Declare PtrSafe` behind `#If VBA7 Then`, or a procedure whose `#If` gives it two headers, has no
  error marks. The branches are the project's: the server reads `CondComp` from the `.vbp` that lists the document. A
  directive out of place is reported on its line in vb6.exe's words. Where a condition is beyond the server's reduced
  evaluator, such as a string comparison or a function, nothing inside that block is marked.

- **A control's `ZOrder` method** (#1503). `ZOrder 0`, or no argument, brings a control to the front of the controls
  like it in its container, and `ZOrder 1` sends it to the back, as measured on vb6.exe. A Label never rises over a
  windowed control, a container takes its contents, and the position is 0 or 1 or it raises VB6's error. A program
  that called it used to stop as not implemented.

- **A control HexIDE does not model is shown in the designer** (#157). An ImageList, a Toolbar or any other control
  HexIDE carries without modelling used to be invisible, so a developer could move a button onto it unknowing. Now
  each is a box where VB6's IDE puts one: at its position, sized by its `Width` and `Height`, or 1200 by 480 twips
  when it records neither, as VB6 measures it. The box names the control's class and says HexIDE does not support
  it; it cannot be selected or moved, and the control is still saved exactly as it was read.
- **A ComboBox raises Click, Change and DropDown** (#1429). It raised none, so a program's `Combo1_Click` and
  `Combo1_Change` never ran, whatever the user or code did. Now each comes where vb6.exe raises it, measured with real
  input, native and p-code:
  - **Click** for every item chosen, by the mouse or an arrow key, even the one already selected, and for code's
    `ListIndex` when it moves the selection;
  - **Change** for typing, and for code's `Text` when it changes, but not for choosing an item;
  - **DropDown** as the list opens.

  Typing an item's text into the box selects nothing, as in VB6.
- **A ComboBox has a Text** (#926). It had none, so code reading `Combo1.Text` stopped, and a combo's Text could be
  neither set in the Properties window nor drawn. Now a Dropdown Combo's and a Simple Combo's Text works as vb6.exe's
  does:
  - code's Text selects no item, even one it matches;
  - choosing an item puts its text in the box.

  A Dropdown List's Text is refused at run time until its Style is modelled (#1428), and is never saved, as in VB6. A
  newly drawn TextBox or ComboBox starts with its name as its Text, as VB6's designer gives it.
- **A ListBox's MultiSelect** (#1413). A list set to Simple or Extended used to run single-select, and its `ListIndex`
  read -1 where VB6 reads 0. Now it selects as vb6.exe's does, with real input measured, native and p-code:
  - **Simple** toggles the item clicked, and Space toggles the focused one;
  - **Extended** selects the item clicked alone, Ctrl+click toggles it, and Shift selects a range from the anchor;
  - **`ListIndex`** is the focused item, and `Selected(i)`, `SelCount` and `Text` read and set the selection as VB6's do.

  MultiSelect is in the Properties window, saved as VB6 writes it, and read-only to a program, as in VB6.
- **Shared DAP protocol capture** (#1166). The DAP library can record wire traffic and process diagnostics
  in the existing inspector's conversation log, with bounded retention and source-path pseudonymisation on
  export. A host that launches external debug sessions from the IDE remains subsequent work.

- **DAP adapter process ownership** (#1162). The client library launches explicit adapter commands, retains
  bounded stderr diagnostics and exit status, and cleans up unresponsive processes. Backend selection,
  protocol-inspector capture and external debugging through the IDE remain subsequent work.

- **DAP wire client foundation** (#1160). A stream-based client library supports framed requests, ordered
  events, adapter errors and bounded connection teardown. External debugger launch and IDE integration remain
  subsequent work; this does not yet change F5 or expose an external debugger in the UI.

- **Text clipboard operations** (#1156). `Clipboard.Clear`, `SetText`, `GetText` and `GetFormat(vbCFText)` now
  use the host clipboard, enabling Notepad's Edit menu, Cut, Copy and Paste. Defaults, ANSI conversion, NUL
  truncation and argument errors follow measured VB6 behavior. Non-text operations remain explicitly unsupported;
  setting text over non-text formats is refused until they can be preserved (call Clear first).

- **A TextBox's `MultiLine` and `ScrollBars`** (#1340). They were kept in the file and ignored, so Enter added
  nothing, no long line wrapped and no box drew a scroll bar. Now a MultiLine box takes Enter as a CR LF on every host,
  wraps unless it has a horizontal scroll bar, and draws the bars it asks for. A single-line box draws none, which also
  ends the vertical bar fragment a short one showed (#914). Both read back at run time and refuse a write with 382, as
  vb6.exe measured.
- **A TextBox's `SelStart`, `SelLength` and `SelText`** (#1150). They raised 438. They now read and write as vb6.exe
  measured: setting SelStart drops the selection, positions past the end clamp, a negative one is 380, and SelText
  leaves the caret after what it put in.
- **A form's `WindowState`** (#1152). Reading or setting it stopped the run, and a form saved maximized opened at its
  normal size. It now reads and writes as vb6.exe measured: 0 until the form is shown, then the state it is in, with
  380 for a value other than 0, 1 or 2. A form saved minimized or maximized shows that way.
- **The CommonDialog control** (#1084). A form holding one used to be refused at run time. Now `ShowOpen`,
  `ShowSave` and `ShowFont` show the IDE's own dialogs, and an automation client can answer the pickers with
  `answer_next_file_dialog`. Cancel and `CancelError`, `FileName`, `FileTitle`, `DefaultExt` and the Font properties
  behave as vb6.exe measured, and the control's 102 constants resolve in a project that references it. The form still
  saves byte for byte. `ShowColor`, `ShowPrinter` and `ShowHelp` are still to come.
- **`Command`, and `Left$` in a form's code** (#742). Both stopped a run. `Command$`, `Command` and `Command()`
  now give the project's command line arguments, empty where it has none. `Left$("abc", 1)` in a form's code is
  VBA's `Left$`, as vb6.exe measured.
- **Text files** (#1145). `Open` For Input, Output and Append, `Line Input #`, `Print #`, `Close`, `FreeFile` and
  `EOF` used to stop a run at the first of them. Now they read and write as vb6.exe does, byte for byte:
  - lines end at CR or CR LF, and Ctrl+Z ends the file;
  - numbers are printed with their sign's space, and commas move to 14-column zones;
  - text is in the ANSI code page;
  - the errors are VB6's: 52, 53, 54, 55, 62, 75 and 76.

  Every rule was measured, native and p-code. Random and Binary files, `Input #` and `Write #` are still to come.
- **The Line control** (#163). A form holding one used to be refused at run time, since #857. Now:
  - it loads, runs and saves byte for byte, VB6's About Dialog included;
  - it draws pixel for pixel as VB6 does: dashes, widths, and which of two lines is on top;
  - its members read and write from code, with VB6's types and refusals.

  The designer draws a Line and moves it by the rectangle it covers. Every rule was measured on vb6.exe. Still to
  come: the toolbox entry, and dragging one endpoint at a time.
- **`RGB` and `QBColor`.** They stopped a run with "Sub or Function not defined"; both now return VB6's Long,
  with its rounding, clamping and errors, measured.
- **The protocol inspector says when a server's bytes actually moved**, not only when a message was prepared. For
  a server started over standard input and output or a named pipe, the timeline now shows when the first message
  began to be written, when it was handed to the server, when the IDE began waiting for a reply, and when the
  server's first bytes arrived. A server that seems to ignore the IDE can now be told apart from a message that
  never reached it.
- **`--ansi-code-page <n>`, in Debug builds**, reads and writes VB6 sources as a machine whose ANSI code page is
  `n` would, so a file from, say, a Japanese machine can be checked on a Western one.

### Changed

- **The code window shows the whole file, header included, and one line number now means the same thing
  everywhere** - to the editor, to a language server, to the interpreter and to the debugger. A form's
  designer block and a module's `Attribute` header are no longer hidden from the code window.
- **That header opens folded**, into one line at the top of the window, so a form's code still starts where
  it did. Expand it and it stays expanded in that window, through formatting, a change in the form designer
  and a reload, until you fold it again. It needs no language server. A module whose header is a single line
  has nothing to fold.
- **The header and a procedure's `Attribute` lines are drawn in grey**, so the lines only the IDE writes look
  it. Each theme sets its own grey, and each stays legible on that theme's editor. A folded section's label
  takes the same colour, which makes those labels legible on a white or Dark editor, where the old grey was
  not. A web or mail address in those lines is greyed with them and is no longer a link. A theme pack sets
  the colour as `ReadOnlyText`.
- **Undo in the code window never undoes a designer change.** A change committed in the form designer
  reaches the code window as a rewrite of the header at the top of it, and Ctrl+Z there undoes the last
  thing typed in that window, exactly as before. **One consequence is worth knowing:** undoing a code edit
  made *before* a designer change costs the redo for it. The header has to be put back afterwards, and
  putting anything back is itself a change, which is what clears a redo stack. The alternatives were worse -
  discarding the whole history, or refusing to undo past the designer change at all.
- **Opening a file leaves nothing to undo.** Previously the first Ctrl+Z in a newly opened code window
  emptied it, because loading the text counted as an edit.
- **Reloading a file changed outside the IDE discards that window's undo history**, as reloading it into
  the form designer already did. The history described a document that is no longer there.
- **Only the IDE rewrites a file's header or a procedure's `Attribute` lines.** Now that the code window
  shows them, the commands that write into it keep off them. Formatting leaves them as they are. Find and
  Replace do not search them, as VB6's Find never searched the header it hid: searching a form's code for
  `Command1` finds it in the code rather than on the control's line in the designer block, and replacing
  every `Command1` does not rename the control behind the designer's back. Insert File puts its text after
  them. A rename that would reach them is
  refused with a message saying why - except for the renamed procedure's own `Attribute` line, which
  follows it. Add-ins and automation clients are refused the same writes, and told so. Typing keeps off
  them too, and cannot split a procedure from its `Attribute` lines: nothing can be typed at the start of
  one, and Delete at the end of a declaration cannot join the line below onto it. Nor can Replace, a
  rename, formatting or an add-in. Deleting or cutting a whole procedure takes its `Attribute` lines with
  it.
- **The bundled language server keeps to the same rule itself**, rather than leaving the code window to
  discard what it should not have sent. It reports nothing inside a header or a procedure's `Attribute`
  lines, its formatting touches only the lines that change, and rename and highlight leave those lines
  alone. So renaming a local that shares its name with a property the layout sets - a `Caption`, a `Text`,
  an `Index` - renames it in the code instead of being refused because the layout uses the same word. A
  control's or the form's own name is still not renamed from the code, and the code window now says so
  before asking for a new name: rename it in the Properties window. Another language server still gets the
  code window's protection.
- **The warning above the code of a form HexIDE cannot save now says what it means**: the file will not be
  saved, its code included, so typing in that window is refused. Typing now is refused everywhere in it:
  Enter, and accepting a completion, used to write a line break or a word there despite it, and an automation
  client's `type_text` and `press_key` are refused with the reason. It used to say the code could not be edited,
  which read like the header's read-only lines and was not true: Replace All, formatting, a rename, Insert File,
  undo and redo, an add-in or an automation client's `set_file_content` can still change the code there, and
  nothing they change is saved ([#701](https://github.com/hexide-io/HexIDE/issues/701)).
- **No breakpoint or bookmark can be set on a file's header or a procedure's `Attribute` lines**, by F9,
  Ctrl+F2, a click in either margin or an automation client; the status bar says why. Those lines never run.
  One already there can still be removed, and one found there among a project's saved marks is dropped as the
  project opens, with a note in the log.
- **Bookmarks and breakpoints saved by an earlier HexIDE stay on the statements they were set on.** They were
  counted from the first line of code and now count from the top of the file, so the file a project keeps them
  in (`<project>.user.hexproj`) is migrated as the project opens and rewritten once, as version 2, with the
  marks under new keys, `fileBookmarks` and `fileBreakpoints`. A mark for a document whose file is missing is
  kept as it is, and moved by the first opening that finds the file. A mark the old code window allowed on what
  is now a read-only line, such as a form's `Attribute VB_Name`, is dropped instead. **Going back to an earlier
  HexIDE afterwards costs those marks:** it shows none of a migrated file's bookmarks or breakpoints, and HexIDE
  0.2.0 and earlier delete them the next time they write that file, which saving the project or setting or
  clearing any mark does.
- **Editing code while a program is paused asks about resetting it only when the edit actually lands.** A
  keystroke in the header writes nothing and no longer asks, and nor does a change made in the form designer.
  Paste, cut and deleting a selection now do ask: until now they slipped past the prompt by every route, the
  keys, the menus and the toolbar alike.
- **For automation clients: `get_file_content` returns a form's, module's or class's whole file**, header
  included, whether or not it is open in the IDE, where it used to return the code alone. Its line 1 is the
  file's first line, as the breakpoint, diagnostic and debugger tools count, and an editor offset indexes it.
  `set_file_content` takes that text back unchanged, or the code alone as before. A whole `.frm` is no longer
  refused for being one: only a header that differs from the document's is refused, and nothing changes. Its
  reply now counts the lines the document holds from the top of the file. `hasUnsavedChanges` means what it
  did. A client that edits what it read and sends it back needs no change; one that searched `content` for
  line numbers now finds them counted from the top of the file.
- **For automation clients: every tool that takes a form, module or class by name finds it the same way** -
  in any loaded project, without regard to case, with an optional `project` for when a group holds two of one
  name. Without `project`, such a name is refused and the refusal lists both. `set_control_property`,
  `move_control` and `add_control` were the exceptions: the first looked only in the startup project, and the
  other two used whichever open designer of that name came first. So with a group open, a bare name those two
  accepted may now be refused as ambiguous; pass `project`. `add_file` takes `project` too, for the project to
  add to. The reply of every tool that takes a document's name now names what it found in `project` and
  `document`, spelled as the IDE spells them, and so do `add_file`'s reply and each `get_diagnostics` item; a
  diagnostic for a file two projects of a group share names neither. Where a reply also carries a `uri`, the
  tool's description now says what it is: the name the language server knows the document by. `get_debug_state`
  and `get_call_stack` name the running project. Not yet: the run-control replies' "Paused at" note, the
  locals' and watches' `context`, the designer undo, redo and format tools, and the tab tools, which take and
  report a tab's title, still name a form or module without its project.
- **For add-in authors: `AddinDocument.Content` is now the document's whole file**, as the code window shows it - a
  form's designer block, a class's header and the leading `Attribute` lines first, then the code - where it used to
  be the code alone. Every position on the add-in surface counts from the top of that file - selections, edits,
  navigation and diagnostics - where in 0.2.0 it counted from the first line of code, so line N of `Content` is
  line N everywhere. An add-in that counted lines itself is out by the header's length until it counts them in
  `Content`. `SetContent` takes `Content` back unchanged, or the code alone, and keeps the header either way; a header
  that differs is refused. **New `TrySetContent`, `TryApplyEdits` and `TryNavigateTo` say why a call was refused**:
  an unknown or ambiguous name, a header the call would change, or a line the document does not have. **New
  `IDiagnosticsAccess.TryGetFor` says why a name found no single document**, where `GetFor` answers an empty list for
  that and for a file with no diagnostics alike. The existing members keep their signatures and answers, so a
  packaged add-in keeps running; and the new ones have default implementations, so a hand-written `IEditorAccess` or
  `IDiagnosticsAccess` keeps compiling. A line outside the document passed to
  `NavigateTo` now moves nothing, where it closed the IDE, and navigating to a document open behind another tab
  brings it to the front, where the caret used to move out of sight.
- **The AI Chat panel is told, and tells you, when the IDE refuses an edit.** It is shown the whole file, header
  included, and told the header is read-only. An edit it asks for, or a block you apply, that would change the
  header is refused: the reason is shown in the transcript and the assistant is told it, on both paths, where
  before the edit was reported as done whether it was or not. Asking for the diagnostics of a file name two
  loaded projects share, or one none has, is refused with the reason rather than answered with no diagnostics.
  Its privacy notice now says the active file's contents are sent, layout and captions included, and is
  shown once more to anyone who saw the old one, which said "code".

### Fixed

- **Bundled-server completion labels are unique and carry declaration kinds** (#940). `Date` appears once as a function; declared names take precedence over keywords and built-ins, and procedures, properties, constants, enums, enum members and types no longer appear as variables.

- **MCP guarded-edit refusals name the region** (#1042): the file's header or the Attribute lines describing a named member. Backward deletion explains how to remove a declaration with its Attribute lines instead of sending the caller back to the same caret offset; insertions still give a new editable offset.

- **A Double rounds to Currency from its exact binary value** (#966). Storing `1.00005`, calling
  `CCur(1.00005)` or declaring it as a Currency Const now gives 1.0001, and `CCur(0.00015)`
  gives 0.0001, as measured on vb6.exe. In `+` and `-` beside a Currency, a Double becomes a Currency the same way before the sum, so `2@ + 0.00005` is 2.0001.

- **MCP can double-click a running form** (#1389). `interact`'s `click` accepts `double` in its value, as in
  `10,5 double`, `10,5 right double` or plain `double`. It presses and releases twice, so the program's DblClick
  runs, waiting for the first click's work before the second; if the target hides or closes, it stops and says so.
  `double_click` still raises DoubleTapped and selects for the Project Explorer, but its reply now says no pointer
  press was sent and points to `click` with `double` for a running form's DblClick.

- **Module-qualified stores and array-element reads reach the module's variable** (#1006, #965).
  `Module1.G = 8` and `Module2.g(2) = 7` raised Object required, and `Module2.g(1)` read the whole array.
  Both stores now work and the read returns its element. `Set Module2.pq = New C1` holds the object,
  and `Set Module2.pq = Nothing` releases it, as measured on vb6.exe.

- **String functions honour the calling module’s `Option Compare`** (#1572). With no compare argument,
  `InStr`, `InStrRev`, `Replace`, `Split`, `Filter` and `StrComp` now use that module’s setting, including
  calls written in classes and forms. An explicit compare still wins. `InStr("ABC", "b")` in a Text module
  now answers 2, as measured on vb6.exe, instead of silently answering 0.

- **MCP paths survive growing editor text** (#1436). A collapsed layout wrapper no longer appears when its
  content begins to scroll, so the Immediate window's text-area path stays usable. Range capability keeps a
  node regardless of its current bounds. A refused path that skips a visible wrapper names it and gives the
  complete corrected path when there is one unambiguous continuation.

- **MCP project saves report unused file-dialog answers** (#1335). `invoke_menu_item` for Save Project / Save Project As and `interact` on their buttons now say when an answer armed before the call remains pending. A document save through a menu, key or click can still wait on formatting after the reply; `answer_next_file_dialog` explains that limit.

- **MCP commands bound through another view model are reachable** (#1336). `inspect_element` lists a menu item’s or button’s resolved `Command` and its binding; `invoke_command` accepts `Command`, checks `CanExecute` with the control’s `CommandParameter`, and runs it. A wrong name explains the binding and how to invoke it, including on the designer’s Delete item.

- **The code editor keeps its caret and view model in agreement** (#1608). An offset past the text now goes to its end,
  and a negative offset goes to its start; the view model reads back the position the editor took.

- **MCP keys, text and hover skip rows their list has released** (#1327). Addressing a list no longer sends input
  into a recycled row omitted from the control tree or reports the list’s path as confirmation.

- **MCP shared context menus belong to the control that opened them** (#1323). A tab menu appears only under its current tab in the dump; expanding it on another tab moves it there, and collapsing it from another tab leaves it alone. Menu headers also read back opens and closes, refusing an action a handler cancelled.

- **A name declared twice is vb6.exe's compile error** (#1622, #1499). A module that declared one name twice ran,
  keeping one declaration, and so did a procedure, so `Dim n As Long` then `Static n` gave an ordinary Long. At module
  level it is now "Ambiguous name detected", and in a procedure "Duplicate declaration in current scope", parameters
  included, as the module loads. A variable beside an Enum or Type of its name, and a Property's Get beside its Let,
  still compile, as they do in vb6.exe.

- **MCP `get_form_controls` reports absent properties as null** (#1635). A Line or Shape no longer claims to be enabled when its class has no Enabled property. The summary's `visible` and `enabled` now follow `caption` and `text`: null where the control's class has no such property.

- **MCP replies distinguish dialogs sharing a title** (#1365). The modal note names a message box or run-time error dialog, its first line of text and its actual buttons; other dialogs include their buttons. The window parameter explains how to discover open windows through an unknown-name refusal.

- **MCP replies name ignored arguments** (#1315). A misspelled optional parameter still lets the call run, but
  now appears in `ignoredArguments` in a JSON-object reply, or in a separate text block, with a suggested spelling
  for case, snake_case or kebab-case mismatches. Binding refusals check each parameter's actual type, so an
  overflowing `int`, numeric string or array item is named without blaming a valid `long`; serializer paths stay.

- **A Simple Combo shows its list under its edit box** (#1603). It was drawn and run as a Dropdown Combo, with a button
  that dropped its list. Its list is now always shown, in the designer and at run time, and scrolls when its items
  don't fit. A click on it picks an item as vb6.exe's does: the one already selected again, the last one from below
  the items, and a double click as Click and then DblClick, with no Change or DropDown.

- **MCP `interact set_property` reads back the value it set** (#1326), naming both sent and actual values when
  they differ. For an editor's `CaretOffset`, it reads the editor itself, so an offset the caret never took is no
  longer reported as set.

- **A numeric literal ending in its point loads, as vb6.exe compiles it** (#1615). `x = 5.` and `5.D1` failed to
  load, and the editor marked them. Both grammars now read them, as the literal with a digit after the point. A
  literal with a point and `%` or `&`, such as `2.5%`, is now vb6.exe's compile error "Syntax error", where HexIDE
  rounded it to an Integer or a Long.

- **The designer draws a Dropdown List's box with the control's name** (#1604). It drew the box empty, as the running
  control is. VB6's designer shows the name the control has now, after a rename too, and not a Text the file saved.
  A Dropdown List renamed and then set back to Style 0 writes its new name as its Text, as VB6 SP6's designer did.

- **MCP `hover` cleans up after a throwing pointer handler, reports a cover's tip over a disabled target, and refuses
  targets that are not showing** (#1328). Containers prefer a showing editor; the tool explains the real mouse's effect
  on the tip, cursor and pointer-over.

- **The arrow keys move between controls from an option button, a check box and a command button** (#1625). They did
  nothing there. Up and Left now go to the previous control in the same container and Down and Right to the next, by
  TabIndex and wrapping, as VB6's do, and an option button they land on is selected. As in VB6, a control whose
  TabStop is False is reached, and one that can't take the focus, such as a Label or a Frame, is passed over.

- **A Const declared with a type character runs, as vb6.exe's does** (#1614). `Const K% = 2.5` stopped the program as
  the module loaded, with "Const type hints not supported". The character is now the As clause it stands for: `K` is
  the Integer 2, `Const K$ = 5` the String "5", and `Const K% = 40000` is vb6.exe's compile error "Overflow".

- **MCP reports keyboard focus and presses where it is** (#1627). `inspect_element` reports `isFocused`, and `dump_visual_tree` names the focused path, or explains when no control in that window has focus. `press_key` at a container keeps the focus inside it, so Tab advances from the current control. An explicit target still takes focus first; the reply names where that move came from, since it raises a running program's focus events.

- **A `D` exponent in a numeric literal loads, as vb6.exe compiles it** (#1611). `x = 1D3` failed to load, and the
  editor marked it. `D` is now an exponent marker wherever `E` is, in both grammars. A literal vb6.exe refuses to
  build, such as `1E400`, `1E3%` or `1E20@`, is now its compile error "Syntax error", where HexIDE gave infinity, a
  Long or a Currency beyond Currency's range, or stopped with a .NET error.

- **A refused MCP call names a blocking dialog once** (#1325). Closing a form under a MsgBox, or typing or pressing a key under stacked dialogs, no longer repeats the dialog in the reply’s note. A refusal never claims the call reached past a dialog; calls that acted behind one still say so.

- **The Properties window shows Left, Top, Width and Height in VB6's units** (#1636). It showed the pixels HexIDE
  keeps them in, so a new form read 312 by 206 where VB6's reads 4920 by 3675. A control's position is now in its
  container's ScaleMode, twips by default and twips inside a Frame, and a form's own size is twips with its frame, as
  VB6's window shows them. A value typed in is in the same units. `get_form_controls`' rows and `set_control_property`
  follow the window, each row naming its unit; the summary and `move_control` stay in pixels.

- **A CommandButton, TextBox, ComboBox, ListBox and Frame have ToolTipText** (#1403). Only five controls declared it,
  so on these the Properties window did not list it, a running form showed no tip, and code could not read or set
  it. Measured on vb6.exe across every intrinsic control: these five have it, and the scroll bars, Shape, Line, Timer
  and menus do not. On all ten that have it, ToolTipText now reads and takes values at run time as vb6.exe's does.

- **A ComboBox's Style is modelled, and a Dropdown List runs as one** (#1428). Style was kept as a preserved line, so
  the Properties window could not show or set it, and a program reading or writing a Dropdown List's Text was stopped
  as not implemented. Now Style is offered with VB6's three names and reads as an Integer, and a Dropdown List's Text
  selects the item it names, or raises 383 as vb6.exe does. Setting Style to Dropdown List in the designer resets
  the Text to the control's name, as VB6's designer does.

- **MCP `type_text` refuses hidden targets before finding an editor** (#1317), in both modes, so a hidden dropdown is no longer told to expand itself. Its target description explains ambiguous surfaces and non-editable dropdowns; `select` now also refuses realized rows whose visibility cannot be read.

- **MCP command guards follow the compiled XAML binding** (#1324). `invoke_command` now finds private guards
  on base types and ignores guards below the bound method's type, so its refusal agrees with the menu.

- **A UserControl's and a PropertyPage's Properties windows list their own kind's properties** (#1585). Both listed
  a form's, Caption, Icon, Left, Top, Visible and the window's buttons among them. Each now lists only properties
  VB6's Properties window gives it: a UserControl has no Caption, and a PropertyPage has no Enabled. A line a
  loaded file holds that its root doesn't list is kept as the file's own text.

- **A Let into an array variable takes only what vb6.exe's does** (#998, #1588). It stored whatever it was given:
  `a = Array(1, 2)` left an `Integer()` a Variant array, Empty left a `String()` Empty, and `b = "ab"` left a `Byte()`
  a String. Now an array of exactly the element type is copied, a String into a `Byte()` becomes its bytes, and anything
  else is error 13 and leaves the array as it was, or VB6's compile error "Can't assign to array" where the statement
  shows the type. A Function declared to return a `Byte()` converts a String too, where it stopped the program.

- **MCP paths for indented list items resolve** (#1357). `dump_visual_tree` trims names in paths while keeping
  the reported name as drawn. Names that become duplicates after trimming use positional paths, as do blank names.

- **Ending an external debug session discards pending evaluations immediately** (#1599). A late adapter reply could
  reach the Immediate window while End was waiting for disconnect. The stopped context now expires before disconnect
  is sent, so an answer arriving during teardown cannot be shown as a current evaluation.

- **Numeric source literals keep their value on every locale** (#781). A made EXE on German regional settings read `0.6 * 10` as 60 and `1.5!` as 15; French settings could stop the program with a parse error. Literal parsing now uses the invariant culture, including suffixes, exponents and conditional compilation. Runtime string conversions retain their locale rules.

- **The Tab key stops once in each option group** (#1583). It stopped at every option button, where VB6 stops
  only at the selected one, or, in a group with none selected, at the first in the key's direction. That option is
  selected there, with Click after GotFocus, as under `SetFocus`. An option is now selected before its GotFocus
  runs, which reads it selected, as vb6.exe's does.

- **Persian words in MCP replies remain readable** (#1316). A zero-width non-joiner between Arabic-script
  letters (including their combining marks) is written as itself. Joiners elsewhere remain escaped.

- **A helper's `On Error Resume Next` no longer sees its caller's stale error** (#877). `On Error`, `Resume` and leaving a procedure from its handler now clear `Err`, as vb6.exe does, so `If Err.Number <> 0 Then` after them reads only what has happened since. While a handler runs, an error in it now goes to the caller even after `On Error Resume Next`, where HexIDE used to trap it and run on. `On Error GoTo -1` runs, and `Err.Raise` with no Description keeps the one already set, as vb6.exe's does.

- **MCP window wording matches how `auto` picks** (#1321). `interact` now says it usually picks a running form, and the unknown-window refusal describes its preference for another visible window, a dialog first.

- **A running form has the window its file describes** (#1575). Every form ran as a sizable window with a title bar,
  minimize and maximize buttons and a close box, whatever its BorderStyle, ControlBox, MinButton and MaxButton said. A
  Fixed Single or Fixed Dialog form can no longer be resized by its border, a borderless form has no frame, a form
  with ControlBox False has no close box, and the tool windows have a tool window's caption. On Windows the window is
  given the styles vb6.exe gives it, measured for 20 combinations.

- **MCP refuses arguments containing an unpaired UTF-16 surrogate** (#1337). The reply now names the tool and
  parameter, says the argument is not valid text and that nothing was done. It previously returned only the SDK's
  `-32603 "An error occurred."` before any tool ran.

- **MCP `press_key` reaches access keys and menus** (#1523). Keys now pass through the window input path,
  so Alt+W activates a button captioned `One &Wide`. The reply names `window-input`, or `routed-key`
  when focus or platform input is unavailable and the previous routed delivery is used.

- **A carried control's box is drawn in its place among the designer's controls** (#1553). The designer drew the box
  for every control it carries as text over every control it models, so a control brought in front of one was saved in
  front of it (#1498) but still drawn under its box. The boxes now stack with the controls, as VB6 stacks the control:
  over its Frame or PictureBox, and over or under each control as the file and Bring to Front or Send to Back put it.

- **`Like` runs in an `#If` or a `#Const`, and a directive orders text as vb6.exe does** (#1598). `Like` there was
  still refused as not implemented after `Like` itself ran. It now matches as text in every module, as vb6.exe's does,
  and a pattern it cannot read is vb6.exe's compile error. A directive ordered two strings case-insensitively by code
  unit, so `#If "_" < "a"` took the other branch from vb6.exe's; it now orders them by vb6.exe's measured text order.

- **The designer draws and names a UserControl or PropertyPage as VB6's does** (#1544). Each was drawn in a form's
  window frame, with a title bar and caption buttons, a PropertyPage's tab read `(Form)`, and the Properties window's
  object box gave both the class `Form`. Each is now a borderless surface, as neither has a window of its own at run
  time, its tab reads `(UserControl)` or `(PropertyPage)`, and the object box names its class.

- **Every focusable control raises GotFocus and LostFocus, and a form or UserControl saved disabled runs disabled**
  (#1101). Only the TextBox and the CommandButton raised them. The CheckBox, OptionButton, ListBox, ComboBox, scroll
  bars and PictureBox now do, as vb6.exe does: one GotFocus as a control is entered, however its items or edit box take
  the focus. The ListBox, scroll bars and PictureBox take the focus, where `SetFocus` on one was refused. An option
  button given the focus by `SetFocus` is selected, raising Click after GotFocus. A form or a UserControl's `.ctl`
  saved with `Enabled = 0 'False` dropped that line and ran enabled. A disabled form is shown without taking the focus.

- **MCP reply numbers use a decimal point in every culture** (#1550). Coordinates, sizes, scroll percentages and
  wait durations now use invariant formatting, so a fractional click point can be read and sent back unchanged.

- **A form's Width and Height are its outer size at run time** (#1072). They read and set the client area, so a
  program got a form 90 to 240 twips narrower and 345 to 885 shorter than vb6.exe reports. They now include the frame
  VB6 draws on the themed Windows desktop for the form's BorderStyle, caption bar and menu bar, read back as assigned,
  and leave the client area that is left inside the frame. `Width = (Width - ScaleWidth) + needed` lands as before.

- **Surplus call arguments are refused** (#741): procedures and intrinsics with known limits raise VB6's
  "Wrong number of arguments or invalid property assignment"; a declared scalar called with arguments raises
  "Expected array". Parameterless Variant and object results still index as before (#1476).

- **A running form raises Activate and Deactivate, and Resize once as it is shown** (#994). Activate and Deactivate
  were never raised. They now come as VB6 raises them: when another of the program's forms is shown, given the focus
  or left active by a Hide or an Unload, after the statement that did it, and not when the user switches to another
  application and back. A form raised Resize two or three times as it was shown, as the window settled. It now
  raises one, inside the Show, and after that one for each change of size or state. A form shown again after Hide
  raises none.

- **Cancelling a language-server request now reaches the server** (#944). The cancellation notification
  failed to serialize on every keystroke that abandoned a completion request. It now sends the original
  request’s bare ID, so a server can stop that work; the client still awaits the server’s response.

- **A disabled menu item says it is disabled in automation** (#1534). `invoke_menu_item` used to blame keyboard
  focus when a disabled item had a routed command. It now reads the materialised item's enabled state first;
  when the menu has not been read, the refusal says its disabled state is unknown and how to check it.

- **An automation click outside its target says so** (#1530). When the point lands on another control,
  `interact` now gives the target's size and names what took the press, rather than saying it covers the target.

- **Ctrl+S on a new UserControl or PropertyPage saves its .ctl or .pag** (#474). From its designer or its code window,
  Ctrl+S saved the designer half as a form: it asked for a `.frm`, wrote the edit there, and left the `.ctl` the project
  names unwritten. A save from either half is now the module's save. It asks for the `.ctl` or `.pag` once, and writes
  it in place after that.

- **A UserControl or PropertyPage is named by its `Begin` line, as VB6 names it** (#1411). HexIDE named it after its
  file, so `Gauge.ctl` holding `Begin VB.UserControl ctlDial` was "Gauge". A form using the control as VB6 does, as
  `Begin probe.ctlDial`, then found no such control, and kept it as text it could neither draw nor run. Where its
  `Begin` line and `VB_Name` disagree, the project now opens with VB6's warning and makes `VB_Name` agree, as #1407
  does for a form.

- **Bare Print no longer prevents a form from loading** (#993). The interpreter and editor accept its output list.
  Reaching it still stops the run at the form's unimplemented Print method (#989); standard and class modules refuse
  it at runtime, naming #993, pending a VB6 measurement.

- **Indexing a call's result runs** (#1476). `Split(s)(0)`, `Array(1, 2)(1)`, `F()(1)` and `a(1)(0) = 9` stopped the
  program. They now run as vb6.exe runs them, evaluating in its order. A parameterless Function that returns a Variant
  now reads its arguments as an index into its result, so `F(1)` is `F()(1)`; it used to drop them and return the whole
  array. A method's result indexed after its arguments, `k.Echo("m n")(1)`, used to call it with no arguments at all.

- **A form's `Controls` collection runs, and `TypeOf` knows a control's class** (#1060). `For Each c In Controls`,
  `Controls.Count`, `Controls(i)` and `Controls("Name")` stopped the run as not implemented. They now hold every control
  in the order the file holds them, menus and each control-array element included, as vb6.exe does, with VB6's
  errors for an index out of range or a name not there. `TypeOf c Is TextBox` was False for every control, so the
  usual loop over a form's controls would have done nothing, silently. It now matches a control by its class, as a
  `Control` and as an `Object`, and a form as a `Form`. `Unload c` unloads a control-array element held in a
  variable. `Controls.Add` and `Remove` are still refused (#1556).

- **A new form, UserControl or PropertyPage is the file VB6 makes, byte for byte** (#1538). A new form was 6000 × 4500
  inside and had no `ClientLeft`, `ClientTop` or `LinkTopic`. A new UserControl had a `Caption`, which a UserControl
  doesn't have, and `Attribute VB_Name` alone. Each now carries what VB6's Add Form, Add User
  Control and Add Property Page write (measured in the IDE): a form 4560 × 3015 at 120, 465 with its `LinkTopic`, and a
  UserControl or PropertyPage 4800 × 3600 at 0, 0 with its own five `Attribute` lines.

- **A new form has no code, and an event procedure is added as VB6 adds one** (#1532). A form HexIDE created started
  with an empty `Form_Load` procedure, and its first save wrote it to the file. VB6's Add Form saves a form ending at its
  `Attribute` lines (measured in the IDE), and adds `Form_Load` when the form is double-clicked. HexIDE's double-click
  wrote that procedure `Public`, in bare LF, after two bare line feeds. Now a double-click, or an event picked in the
  Procedure box, writes it `Private`, in the file's line breaks, at the end and one blank line below any code, with the
  caret on the blank line inside.

- **Object and Collection arrays** (#1482). Elements start as Nothing, accept Set with the same type checks as
  scalar variables, and release their objects on Erase. Class arrays now report TypeName "Object()", too.

- **View Code opens with the caret in the code** (#1518). It opened at the top of the file, on the folded header that
  only the IDE may change, so the first keystroke was refused and nothing appeared. Since #1369 that was every new
  form's first View Code too. Now a code window opened with nowhere asked for puts its caret on the first line below
  the header, as VB6's View Code does. Navigating to a procedure or a breakpoint still puts it there.

- **`Write #` and `Input #` run** (#1521). Both stopped the program as not implemented. `Write #` now writes
  as vb6.exe does: quoted strings, `.5` with no leading zero, `#TRUE#`, `#NULL#` and `#2003-01-02#`, with a point under
  any locale. `Input #` reads each field for the variable it fills, so a Variant comes back typed by what the field holds,
  as an Integer, Currency, Decimal, Double, Boolean or Date. A typed variable takes vb6.exe's rules: 0 for text in a
  number, and 6 or 13 leave the variable as it was. A program that saves its settings with one and loads them with the
  other now runs.

- **`CCur` and `CDec` preserve decimal precision in Strings** (#1531). Plain decimal numbers are read in the
  program's locale without rounding through a Double, including the Currency and Decimal maxima. Out-of-range
  Strings raise run-time error 6. Hex, octal and exponent forms keep their existing conversion.

- **StrComp, StrConv, Environ, Switch and Error() run** (#1560). Each stopped the program that called it. All five were measured on vb6.exe first: StrConv's case goes through the ANSI code page as VB6's does, Switch reads its conditions only until one holds, and Error(n) answers from vb6.exe's text for every number from 0 to 65535. Err.Raise with no Description now takes that same text, so `Err.Raise 340` is "Application-defined or object-defined error", as in VB6, and 21 messages that had other words have vb6.exe's. UCase and LCase now keep the case of µ, ı and four others, as vb6.exe does, where they took .NET's.

- **`Option Compare Text` compares as vb6.exe does** (#1567). A module saying it still compared strings as Binary in `=`, `<`, `>` and `Select Case`, so `"abc" = "ABC"` was False. Now those compare by vb6.exe's own text order, measured character by character: case and accents, the hyphen and apostrophe that a Windows sort sets aside, and ß, æ, œ and þ as their two letters. The same compare now serves a Collection's keys, and a compare of 1 in InStr, InStrRev, Replace, Split and Filter, which had read it as "ignore case" and kept "straße" and "strasse" apart (#1479).

- **A duplicate Collection key's error 457 has vb6.exe's words** (#1567): "This key is already associated with an element of this collection". The oracle had recorded them since the Collection was measured, and `Err.Description` still gave other words.

- **`InStr` and `InStrRev` agree with vb6.exe at the ends** (#1567). `InStr` with an empty string to find returned 0 once the start was past the end, where VB6 returns the start, and `InStrRev` found text before a start past the end, where VB6 returns 0. `InStrRev("abc", "")` was 4, not 3.

- **An intrinsic called with too few arguments raises VB6's compile error** (#1512). Calls such as `Chr()`
  now report "Argument not optional" or "Syntax error", as measured in VB6, instead of stopping with a .NET exception.

- **A control added, brought to front or sent to back stacks past the controls HexIDE carries as text, as VB6's does**
  (#1498). A control HexIDE does not model, such as an OCX's, was written back where it was read, whatever the designer
  did around it. So a button added to Web Browser.frm went in after its toolbar, and the form's ImageList was written
  ahead of a PictureBox nobody had moved. VB6 stacks an OCX control like any other (measured): a new control goes in
  front of the toolbar, and Send to Back puts it behind the ImageList. A form loaded and saved untouched is unchanged.

- **`Print #` writes a character the ANSI code page lacks as its best fit** (#1511). On code page 1252, Ā is written as
  "A", Ł as "L" and α as "a", as vb6.exe writes them, and Ж, which has no fit, as "?". Every one of them was "?".

- **`CVar` runs** (#1466). It stopped the program as undefined. Now it returns its argument as a Variant with the
  argument's own type, an object as the object, as vb6.exe's does. The result reads as a Variant's, so `CVar(5) * 7000` is
  a Long 35000 where `5 * 7000` overflows.

- **OK on the References dialog no longer drops the project's OLE Automation reference** (#1026). Its first row
  carried OLE Automation's GUID, so the project's own reference was hidden, and OK, even with nothing changed, took it
  out. The next save wrote the project without it, and VB6 then failed to build one that uses `StdFont` or
  `OLE_COLOR`. The dialog now lists what VB6's does, as measured: three fixed rows (Visual Basic For Applications and
  VB's two libraries), then every reference of the project in its order, OLE Automation among them. Nothing moves
  above the fixed rows, and it opens on the last checked row.

- **A form whose `Begin` line and `Attribute VB_Name` disagree opens as VB6 opens it** (#1407). HexIDE already named
  such a form by its `Begin` line, as VB6 does, but said nothing and saved the contradiction back. HexIDE itself wrote
  such files before #1059. Opening the project now shows VB6's "Conflicting names were found…" warning. The form's
  `VB_Name` is made to agree, and the form comes up edited, so its next save writes it, as VB6's does. Case alone
  counts as a disagreement, as it does in VB6.

- **The form designer draws a form at the size it runs** (#924). The designer drew the whole window, frame and
  caption bar included, at the form's client size, so the area controls were designed in was 8 pixels narrower and
  about 25 shorter than the running form. A layout drawn flush to the edges ran with a margin, and resizing the form
  saved a size nobody saw. The frame is now drawn around the client, and a resize saves the client it leaves.

- **Six VB6 system colours no longer draw hot pink** (#908). A control given Scroll Bars, Active Title Bar, Active
  Border, Inactive Border, ToolTip Text or ToolTip (`vbInfoBackground`) painted DeepPink, in the designer and at run
  time, because no colour was defined for them. They now take Windows 10's values, as measured on vb6.exe, and a
  system colour that still has none is named in the log.

- **A caption's `&&` and `_` are drawn as VB6 draws them** (#920). `Tom && Jerry` drew `Tom _ Jerry`, and
  `File_Name` drew `FileName` with Alt+N as its key, on buttons, check boxes, option buttons, labels and menus, in the
  designer and at run time. A Frame drew `Fra && Me` with both ampersands. Now `&&` is one `&`, an underscore is a
  letter, a trailing `&` is not drawn, and a caption marking two letters takes the one VB6 does, all as measured on
  vb6.exe. A caption with an underscore before its access key now has no key, because Avalonia would take the
  underscore as the key instead.

- **A control array's elements name themselves with their index in automation** (#1320). The Properties window's
  object selector and the designer's canvas named every element of `optSize` as `optSize`, so a caller could not tell
  which was selected, and `select "optSize(1)"` matched nothing. Each element is now `optSize(0)`, `optSize(1)` and so
  on, the spelling `set_control_property` and `move_control` take.

- **A control-array element renamed out of its array can be renamed back** (#1276). Renaming `optSize(0)` to
  `optBoard` worked, but every way back was refused as a duplicate name, so the array stayed split. A name is now
  taken by name and Index together, as in VB6: an element may take an array's name where its Index is free there.

- **The Project Explorer's Add Form adds a form as Project > Add Form does** (#468). It added a form with no file and
  opened nothing, a second Add Form of its own, and closing the IDE then left that form out of the project file
  (#1359). It now writes the form's file and opens its designer.

- **A new form is written as VB6 writes one** (#931, #932, #516). Its code section was bare LF with no final line
  break, it had none of the five `Attribute` lines VB6 writes for a form, and it had no `StartUpPosition` line, which
  VB6 reads as Manual: the form opened at the screen's origin with its title bar off the top. A form HexIDE creates now
  has all three, and says `StartUpPosition = 3 'Windows Default`. A form with no such line, as VB6 writes a Manual
  one, now runs where its file puts it rather than centred, which is how vb6.exe reads it.

- **A running form stacks overlapping controls as VB6 does** (#1497). The first block in a container is drawn on top,
  and a Label, Shape, Line or Image under every other kind of control, whatever the order. Among those, the first is on
  top, so a Line no longer goes under every Label. A control-array element loaded at run time goes under the controls
  already there. HexIDE drew the last block on top, so a form's overlapping controls came out upside down. The
  designer stacks Lines the same way, so a Line in a Frame is drawn over the Frame rather than under it.
- **The designer draws and saves controls in VB6's stacking order** (#1481). In VB6 the first `Begin` block in a
  container is the control on top, and windowless controls (Label, Shape, Line, Image) are drawn under the windowed
  ones and written after them. HexIDE drew the last block on top and wrote a new control last, so a control added here
  was the bottom one when VB6 opened the file, and Bring to Front and Send to Back each saved the opposite of what they
  showed. Now a new or pasted control is written first in its plane, Bring to Front and Send to Back move a control
  within its plane and take a container's contents with it, and a loaded form is drawn as VB6 draws it. A pasted
  container keeps its source's stacking, where VB6 turns its contents over.

- **`Debug.Assert` runs** (#1467). It stopped the program, even when its condition held. Now, in the IDE, an assertion
  that fails enters break mode at the statement, as `Stop` does, and one that holds goes on, the condition read as `If`
  reads it. A compiled program has no `Debug.Assert`, its argument not even evaluated, so the Make EXE runner skips it,
  as vb6.exe's EXE does. `get_debug_state` reports the break as `Assertion`.

- **ChrW and AscW run, and Chr and Asc go through the ANSI code page** (#1134). `ChrW` and `AscW` stopped the program
  as undefined. `Chr` and `Asc` ran, but in UTF-16 units, so `Chr(&H80)` was a control character where VB6 on a 1252
  machine gives "€", and `Asc("€")` was 8364 where VB6 gives 128. All four now follow vb6.exe, measured:
  - **ChrW:** a negative code is its unsigned unit, so `ChrW(&HF050)` is U+F050.
  - **AscW:** a signed Integer.
  - **Asc:** best-fits a character the code page lacks, so Ā is "A".
  - **Errors:** each raises 5 out of range, 94 for Null and 6 past a Long. `Asc(Null)` is 94 where it returned Null.

  A `$` form's result is now a typed String, as VB6's is, so `Select Case Mid$("9", 1)` compares text. Plain `Str` is a
  Variant, as `Mid` and `Chr` are.

- **Make Project Group is offered only with a group open, as VB6 offers it** (#746). With one project loaded it
  was offered anyway, then failed on the missing group and said nothing. It is now greyed out until a group is
  open or a second project is loaded. With a group, it used to save every project and the group under a Make
  label. VB6's builds them, which HexIDE can't do yet (#1515), so it now says the feature isn't implemented and
  writes nothing. Save Project Group still saves.
- **An undeclared variable passed ByRef takes the procedure's write** (#1505). `Bump a12`, with `a12` never declared
  and `Sub Bump(x)` adding 1 to `x`, left `a12` Empty, so the program ran on with a wrong value and nothing said why. VB6
  declares the variable where it is passed, and the write lands in it. Now HexIDE does the same for a Sub, a Function, a
  class's method and a named argument, under DefType too. A name in parentheses, or given to a ByVal parameter, is still
  a copy, as in VB6.

- **A new control is numbered from 1, as VB6 numbers it** (#540). The first button on a form was Command0, and a
  pasted control took its number from the count of controls on the form, so its name skipped a gap VB6 would have
  filled. A new or pasted control now takes the lowest number from 1 that no control on the form has. That is
  Command2 beside Command1 and Command3, Text1 beside Text5, and a deleted control's number again, as measured.
- **`DefInt A-Z` and the rest of DefType run** (#169). A DefType statement stopped the program. Now each declaration with
  no As clause takes its type from the first letter of its name, as vb6.exe does. That covers an undeclared variable, a
  `Dim` or `Static`, a field, a parameter and a Function's return, and under DefObj each is an object variable. A `Const`
  keeps its value's type. A DefType that overlaps another, follows a declaration or sits inside a procedure is VB6's own
  compile error. Two defects found on the way are fixed too:
  - a `Set` into a name never declared raised 424, where VB6 declares it, as a Let does;
  - a Function `As Object` or `As Collection` returned Empty before any Set, not Nothing, so `F = 5` in one took the
    value where VB6 raises 91.

- **A control added by double-clicking its tool takes the size VB6 gives it** (#1473). Every class came out 50 x 50
  pixels, a square CommandButton among them, and that size went into the saved file. Now a CommandButton is 1215 x 495
  twips, as are most classes, while a ComboBox, ListBox and either scroll bar each have their own size, all as
  measured. Each is centred by its own size and put on the grid, where VB6 puts it.
- **Val, Round, Split and 13 more intrinsics take named arguments** (#1475). `Val(String:="12")`,
  `Round(Number:=2.567, NumDigitsAfterDecimal:=1)` and `Split(Expression:="a b c")` stopped the program, because
  signature help had no entry for them, and so neither did the interpreter's table of names. All 16 are now measured
  against vb6.exe and listed, each with a tooltip: their names, how many are required, and what a wrong or missing name
  gives. Now, Date, Timer and the other intrinsics with no parameters refuse a name with VB6's own compile error.

- **A Let into an object variable no longer drops the object** (#1493). `k = 5`, with `k As C1` holding an instance,
  released it, running its Terminate there and then, and stored 5, so the program ran on without it. VB6 hands the
  value to the object's default member: a class with none raises 438, and `k` keeps the object; Nothing raises 91. That
  holds for `As Object`, object parameters, an object Function's return name and a class's object fields too. A
  Variant still takes the value and drops the object, as VB6's does.

- **`Static` runs, and a `Static Sub` keeps its locals** (#1491). A `Static` local stopped the program. Now it keeps its
  value from one call to the next, as vb6.exe does: shared by recursive calls, kept per instance in a class or a form,
  and released with the instance. The `Static` keyword on a procedure was read and then ignored, so a `Static Sub`'s
  locals silently started again at zero on every call. Now they keep their values too, though a Static Function's own
  return value starts again, as VB6's does.

- **`Dim o As New C1` runs** (#435). The whole module stopped loading at the first `As New`. Now a variable declared
  `As New` holds Nothing until it is used, and its first use makes the object, as vb6.exe does, for a class and for a
  Collection, at procedure and module level and as a class field. Every use counts: `If o Is Nothing` makes the object,
  so it is never True. After `Set o = Nothing`, the next use makes a new one. An array `As New` (#1482) and a Type's
  field `As New` (#1488) still stop the run, by name.

- **An empty Collection refuses an index as VB6's does** (#1492). Every index into an empty Collection is 5 in VB6: a
  position, a key, Null or Empty, for Item, Remove, and Add's Before and After. HexIDE gave what a Collection with items
  gives, 9 for a position and 13 for Null. Assigning an item, which no Collection allows, now looks the index up first,
  as VB6 does: `c(3) = x` past the end is 9, and `c("zz") = x` with no such key is 5. Only an item that is there gives
  424, and late-bound it is 451 or 438, by form, as measured.

- **A control added or pasted no longer takes the TabIndex of a control HexIDE does not model** (#925).
  Such a control, an OCX's Toolbar for one, is carried as text, and its TabIndex went uncounted, so a new control could
  be given the same number. It now counts, nested ones included.

- **VB6's Collection runs** (#1471). There was none, so a program that made one stopped at `New Collection`. Now
  `Add`, `Item`, `Remove`, `Count`, For Each and `c!key` give vb6.exe's answers and errors, each measured native and
  p-code. That includes the two that surprise people:
  - For Each walks the Collection itself, so removing the current item goes on to the next;
  - a key that isn't a String counts as a duplicate when it names an existing position, so `c.Add x, 5` is 457 in
    a Collection of five and 13 in one of four.

  Objects held in a Collection are released when the last reference goes, in VB6's order. Locals and Watch open a
  Collection to its items. Still to come: arrays of Collection (#1482), and keys
  compared as VB6's text compare does rather than only ignoring case (#1479).

- **A control added or pasted takes the TabIndex VB6 gives it** (#925). The first control got 1, a Timer or a Shape used
  a number up, a Label or a Frame was saved without one, and a pasted control kept its source's, so the file held two
  controls at one TabIndex. A control on a loaded form could also take a Label's or a Frame's number, which HexIDE
  couldn't see. Now each takes the next after the highest on the form, as VB6's designer does, measured: the first is 0,
  a Timer takes none, a Label and a Frame have theirs, and a paste numbers its container and then what it holds.

- **Center in Form, Align Centers and Make Equal spacing put a control where VB6 does** (#929). They worked in
  fractional pixels, so a control centred on an odd difference landed half a pixel off, and a save wrote it as
  `Left = 4192.5`. Each now works as VB6's does, measured in its IDE: in whole twips, halving each size on its own and
  truncating. Make Equal truncates the gap and leaves the last control where it was. A control added by
  double-clicking a tool is centred the same way, then put on the nearest grid line when Align to Grid is on.

- **Named arguments run** (#1465). `MsgBox Prompt:="Done", Title:="App"` and `Foo Count:=3` stopped the program with
  "Assign is not implemented". A named argument now goes to the parameter of its name in a call to the program's own
  procedures and methods, and to the 57 intrinsics vb6.exe takes names for, with VB6's compile errors for an unknown or
  repeated name. The arguments are evaluated in parameter order, not the order written, as vb6.exe evaluates them, so
  `Foo c:=F(), a:=G()` calls G first. A named argument to a control's, a form's or an object's method is still refused
  (#1474).

- **IIf and Choose run** (#1461, #1464). Neither existed, so a program that called either stopped there. Both now give
  vb6.exe's answers, including the two that surprise people: IIf evaluates both parts, so `IIf(n <> 0, 1 / n, 0)` still
  divides by zero, and Choose truncates its index, so `Choose(1.6, "a", "b")` is "a".

- **ReDim Preserve runs** (#1459). It was refused as not implemented, so a program that grows an array as it goes stopped
  at the first one. It now keeps the elements in range and changes only the last dimension's upper bound, and refuses
  everything else as vb6.exe does, each case measured: another bound changing is 9, too big is 7, and a Variant holding
  no array is 13, each leaving the array as it was.

- **Cutting and pasting a control keeps what HexIDE carries without modelling** (#1453). A Frame or PictureBox cut
  and pasted lost the controls inside it that HexIDE does not model, such as an ImageList, and any control lost its own
  properties HexIDE does not model. The next save left them all out, with nothing said. They now go with the control,
  with the companion records they cite. A copy takes the properties too. It still leaves the controls inside a
  container behind, because their names would repeat the originals' (#1458).

- **An array element passed ByRef is the element itself** (#1455). `Swap s(0), s(1)` swapped nothing, so a sort written
  the usual way ran to the end and left its array as it was. The element is now passed by reference, as in VB6, and
  while the call runs its array is locked: ReDim, ReDim Preserve, Erase, or a store into the array's variable raises 10.

- **A UDT field passed ByRef is the field itself** (#1451). `PL u.l`, with `PL(x As Long)` storing `x = "9"`, left `u.l`
  unchanged: the field was copied in and never written back. A procedure that fills in a record through ByRef
  parameters now fills it in, as in VB6, and a store through the parameter converts into the field's type.

- **A store into a UDT field converts into the field's type** (#221). `u.l = 5`, with `l As Long`, read back as an
  Integer, `u.dt = "1/2/2003"` stayed a String, and `u.i = 40000` with `i As Integer` stored 40000 with no error. Each
  now converts as a store into a variable of the type does, as VB6 does, and eleven corpus cases that measured it match.
- **A typed parameter converts what it is given** (#874). `Sub S(ByVal x As String)` called with `5` received an
  Integer, `B "abc"` with `ByVal x As Boolean` ran with a String where VB6 raises 13 at the call, and `NI "12"` with
  `ByVal x As Integer` received 0. A parameter, ByVal or ByRef given a literal, and an Optional default now convert as a
  store into a variable of the type does.

- **A typed Function or Property Get converts what is stored into its name** (#1444). `FS = 5`, in a Function
  `As String`, returned an Integer, `FD = "0.5"` `As Date` a String, and `FI = 40000` `As Integer` raised its Overflow in
  the caller, after the Function had returned. Each now converts at the store, as VB6 does, so a failure is trapped by
  the procedure's own handler.

- **A String stored into a Date reads as VB6 reads it** (#967, #800). `d = "1/2/2003"` was Type mismatch, and
  `CDate("36526")` was too. Now `CDate`, a store into a Date, a Date `Const` and the date functions share one reading: a
  date or time in the program's locale first, then a serial. Under en-GB, a dot between digits separates a time's parts
  (`"1.5"` is 01:05), so `"1.2.2003"` is no date, as in VB6. `IsDate` agrees with it, and `CVDate`, which was missing, is
  `CDate`. `DateValue` and `TimeValue` still take no serial, as in VB6.

- **Another module's Public Enum member reads bare** (#889). `colMax`, declared in Module2's `Public Enum Cols`, read
  as Empty in Module1 with no error, so `For i = 0 To colMax` ran once. It now reads as the Long VB6 gives it, from a
  class's Public Enum too, and sizes a module-level array or folds into a Const. Two modules' members of one name are
  "Ambiguous name detected" where used bare, as in VB6.

- **A lone CR ends a line when a program runs** (#1426). VB6 ends a line at CRLF, a lone LF or a lone CR. The
  interpreter took only the first two, so a form whose code had a lone CR, which VB6 builds and runs, was a syntax
  error to F5. A lone CR now ends a statement, a line continuation and a comment, as each was measured to.

- **A form whose OCX control keeps pictures in the `.frx` can be saved, and keeps them** (#60). A picture cited by a
  control HexIDE does not model, such as an ImageList's, or by a property it does not model was dropped with its line,
  so the form was held read-only. Now the line and the picture go back out together, and a save rewrites the citation
  to wherever the picture lands, so a picture HexIDE does model that grows moves the others without losing them. VB6's
  own `Web Browser.frm` was the last of its template forms held read-only; it now saves byte for byte, `.frx` included.
- **A control array named through its form, `Form1.Text1(0).Text`, reads and writes** (#1386). It stopped the program
  with "Method or data member not found", in code and in the Immediate window alike, because the index was dropped. A
  missing element is VB6's 340, whose message now names the index, as vb6.exe's does.
- **A `?` line in the Immediate window prints as VB6's does** (#1386). `? 5` prints ` 5 `, as Print does, and a list
  such as `? a; b` or `? a, b` prints as `Debug.Print` prints it, where it was "Syntax error". A compile error raised as
  the line is evaluated now says what it is, and only a line that doesn't parse is a syntax error.

- **A control's Name and Index can be read at run time** (#1431). Reading `Combo1.Name`, or `ctl.Name` in a generic
  handler, stopped the program with a compile error that `On Error` couldn't trap, and so did an element's `Index`. Both
  now read as vb6.exe's do, measured on every kind of control:
  - **Name** is the control's name, its array's for an element, and a menu's too.
  - **Index** is an element's index, and 343, "Object not an array", for any other control.
  - **Assigning either** is VB6's 382, "property cannot be set at run time", which `On Error` can trap.

- **Ctrl+End after a selection made in code goes to the end of the text** (#1385). After code selected text that ran
  to the end, such as the Notepad demo's Select All, Ctrl+End put the caret at the start, so a paste went in there and
  Find Up searched from the middle. Ctrl+End and Ctrl+Home now land where VB6's do, measured with real keys.

- **A project's description is saved in its project file** (#42). Project Properties set it, and nothing wrote it,
  so a description typed there was lost at the next save. It is now read from and written to the `.vbp` as VB6
  writes it: `Description="..."` with a quote inside doubled, after `HelpContextID`, and no line when there is
  none. Measured with VB6's own Save Project As.

- **A `.bas` whose lines end with a lone CR no longer loses its code on Save Project** (#1020). The header split, the
  `VB_Name` retarget, the read-only regions and the guard ended a line only at LF. So a CR-only module was one line to
  them, taken whole for its header, and Save Project wrote the file back as its `VB_Name` line alone. One stray CR lost
  the line it joined, and a CR-only form lost its whole code section on the first nudge of a control. They now end a
  line at a lone CR and a lone LF as well as at CRLF, which is what VB6 does (measured with `vb6.exe /make`). A module
  with lone CRs is written back with CRLF throughout. To VB6 it is the same file.

- **Save Project no longer writes a second header into a `.bas` or `.cls` whose first line is blank** (#472). The
  header reader looked for the header on line 0 alone. So one blank line above it left the whole file as code, and
  Save Project, which writes every module, put the canonical header in front of it, unasked. For a class, that
  header contradicted the file's own instancing. The reader now finds the header below blank lines, and keeps them.
  A save no longer adds a header to code that carries its own `VB_Name`. A class whose `BEGIN` has no `END` no
  longer loses its code to the header, or grows by a line on every save.

- **`FormCodeText.PreserveAttributes` is gone** (#686). It could weld a header with no line ending onto the code
  after it, but it has had no caller since #273's phase 3 kept a form's header with `GuardedContent` instead, so it
  was deleted rather than repaired.

- **A ListBox's `Selected(i)`, `SelCount` and `Text` work** (#1395). Each was "Method or data member not found", so
  code that read which items were chosen, or chose one by its text, stopped there. Now each does what vb6.exe does on a
  single-select list:
  - `Selected(i)` reads and sets the selection, and is 381 outside the list;
  - `Text = s` selects the first item whose whole text is `s`, without regard to case, and raises Click every time;
  - Click comes from `Selected(i) =` only when the selection moves, as from `ListIndex`.

- **Save Project As names a designer, the resource file and the build folder from the new folder** (#1406). VB6's
  Save As rewrites the paths in `Designer=`, `ResFile32=` and `Path32=` so that they still name the same files.
  HexIDE kept them as read, so from another folder they named files beside the new project file. A reference's
  relative library path follows too. `HelpFile=` stays as typed, as VB6 leaves it.

- **`Debug.Print` prints every item, as VB6's Immediate window does** (#1380). Only the first item reached the Immediate
  window, so `Debug.Print "x ="; x` showed `x =` alone, and a trailing `;` still ended the line. Now `Debug.Print` writes
  what `Print #` writes, which is what VB6 does, line for line, as measured in its IDE:
  - numbers have a space for the sign and one after;
  - a comma moves to the next 14-column zone;
  - a statement ending in `;` or `,` leaves its line open for the next, even in the next run.

  `Spc(n)` and `Tab(n)` now work in both, where `Print #` refused them.
- **`Debug.Print ,` and `Debug.Print ;` load and run** (#1409). A `Debug.Print` of separators alone was a syntax error,
  so a module holding one could not load at all. Now it prints as VB6 does: each comma moves to the next zone, and the
  line stays open. A call of an ordinary procedure with separators alone, `Foo ,`, is still a syntax error, as in VB6.

- **Save Project As to another folder no longer cuts off the members the project keeps only as a line** (#737). A
  form whose file is missing, one that failed to load, or a UserDocument is kept in the project file without being
  loaded, and its line was written back as it was read. From a new folder that line named a file beside the new
  project file, where there is none, and the next open lost the member. It now names the same file from wherever the
  project file is written, as do the original lines of carried files.

- **A quote inside a form's string property is read and written as VB6 does** (#218). VB6 writes a quote inside a
  caption, tag or text doubled, and reads the pair back as one. HexIDE kept both quotes on read, so a VB6 form's
  `"He said ""hi"""` showed as `He said ""hi""`. It wrote a lone quote single, which vb6.exe refuses: the whole form
  fails to load, and the build fails.

- **A Label's, an Image's, a Frame's and a PictureBox's Click needs the release over them** (#1373). A press on one,
  dragged off and released on the form or another control, still raised Click, so dragging off a control never
  cancelled its click as it does in VB6. Now Click comes only for a release over the control again, as vb6.exe
  measured; a TextBox and a ListBox, which VB6 lets click wherever the release lands, are unchanged.

- **A control can no longer be given a name already on its form** (#854). The designer checked a new name only
  against the controls it models, and mostly with case. A rename, a control drawn from the toolbox, or a paste could
  take the name of a control HexIDE keeps as preserved text, such as a Data or OLE control, or a sibling's name in
  another case. vb6.exe builds such a form without either control, and logs nothing a build would stop on. Every name
  on the form now counts, compared without regard to case, as vb6.exe compares them.

- **A list's `ListCount`, `List(i)`, `RemoveItem` and `AddItem`'s index work** (#821), on a ListBox and a ComboBox.
  `ListCount` and `List(i)` were "Method or data member not found", `RemoveItem` ended the run as not implemented, and
  `AddItem "x", 0` appended, leaving the list in another order than the program built. Each now does what vb6.exe
  does, in range and out: `List(i)` reads "" outside the list, a write at `ListCount` or -1 appends, an index out of
  range is 5 or 381, and the selection follows its item.

- **A ListBox raises Click where VB6 does** (#1374). It raised Click after MouseUp on every release, and never when
  code set `ListIndex` or an arrow key moved the selection. Now a left press on an item raises Click before MouseUp,
  the selection follows the pointer while the button is held, `ListIndex = n` and an arrow key raise it when the
  selection moves, and a right or middle click, or a press below the items, selects nothing and raises none, as
  vb6.exe does with real input.

- **MousePointer has all seventeen of VB6's values** (#120). Icon (4), Size (5), Arrow and Hourglass (13) and Arrow and
  Question (14) were missing. A form using one saved it without the name VB6 writes beside it, the Properties window
  showed a blank, and the control drew the default pointer. All four now carry their names and draw the pointers
  vb6.exe draws, as measured.

- **DblClick runs** (#1031). It was declared nowhere and raised by nothing, so a `List1_DblClick` or `Form_DblClick`
  never ran. Now a double click raises it on the form and on each control VB6 gives one, in vb6.exe's order with real
  input: MouseDown, MouseUp, Click, DblClick, MouseUp, with no MouseDown for the second press. An OptionButton raises
  MouseDown then DblClick there, and a CommandButton or CheckBox, which have none, two clicks.
- **A form's own Click runs** (#1364). A click on the form's area, over no control, ran `Form_MouseDown` and
  `Form_MouseUp` and never `Form_Click`. Now Click follows MouseUp, for the left, right and middle buttons, as
  vb6.exe does with real input. As in VB6, a press released over a control or outside the form gives no Click. A
  double click gives one Click, not two.

- **Collapsing a language server's fold no longer hides its closing line** (#1363). The editor folds a server's
  range by whole lines but never said so, so a server could end a fold part-way along its closing line. clangd
  ends each fold just before its closing brace, and collapsing the fold hid the brace. The client now declares
  `lineFoldingOnly`, and such a server ends the fold on the line before.

- **`FontSize`, `FontBold`, `FontItalic`, `FontUnderline` and `FontStrikethru`, and a TextBox's own Font** (#1343,
  #915). The five members stopped a run at the first read or write, and a TextBox had no Font of its own, so Notepad's
  Format > Font stopped at its first line. Now every control with a font, and the form, reads and writes them as
  vb6.exe measured: sizes snap to whole pixels, and GDI's bitmap fonts to the heights they have, turning into Arial past
  a size of their own; the Booleans coerce and refuse as VB6's do. A TextBox draws in its own font, and its Font block
  saves byte for byte.
- **The Font dialog's sample draws in the face chosen** (#1353). It always drew in the default face, because a family's
  name was bound where a FontFamily was wanted.

- **A form the Project Explorer adds is saved into the project** (#1359). Closing the IDE wrote the form's file and
  left the project file without it, so the next open had no such form, even after Yes to everything in the save
  prompt. A project holding a form or module that has never been saved now counts as changed, so closing saves its
  file listing them.
- **A project is not saved without its never-saved forms** (#497). With the project ticked in the save prompt and a
  never-saved form not, closing wrote a project file without the form. Where that was the startup form, vb6.exe
  built the file in silence into a program that starts at `Sub Main`. Now nothing is written, a warning names the
  files, and the project stays open.
- **Add Form and Add Module no longer write over a file already in the folder** (#738). A new member's file replaced
  any file of its name there, such as another project's `Module1.bas` in a shared folder. The Add commands now skip
  to a number whose file is free.
- **A new form or module has no file until the project is saved, as in VB6** (#500). Each Add wrote a file at once,
  and in a project never saved that went into a temporary folder the developer never chose. Its first Save Project
  then wrote the `.vbp` where the developer asked and left the members in that folder, named from the project file
  by a path into it, so the next temp clean-up lost them. Now Save Project asks where to put each new form and module,
  as VB6 does. A new document's code window still shows the header its first save will write. Additions that have
  never been saved no longer survive a crash in that folder: there is no crash recovery yet (#499).
- **A language server that waits to be told sends document symbols as a tree** (#463). HexIDE read the tree
  but never declared that it could, so a server that honours the declaration, clangd among them, sent the flat
  form and every outline from it lost its nesting.

- **Two code windows over one name are one document to a language server** (#721). One file shared by two projects
  of a group opens two windows that name it alike. Each kept its own version count, so a server was opened twice and
  discarded whichever count was behind, and closing either window closed the document for both. The windows now
  share it: opened by the first, one sequence of versions, closed by the last.

- **An edit made while a language server starts reaches it** (#719). It was dropped, and the server analysed the
  text from before the edit until the next edit or a save. With two servers on one language, the second was also
  opened twice, the second time at a lower version with the older text. A change made during a start is now
  recorded and carried by the open the server receives, which is sent once and before any change.
- **A font's size is drawn in points** (#910). It was drawn as pixels, so every VB6 font came out a quarter too small:
  a Label in 12-point Arial drew at 12 pixels instead of 16. The default font was held as 11, the pixels
  VB6's 8.25 points draw at, and the designer's Font dialog wrote that into a .frm as `Size = 11`. It also turned 8.25
  and 9.6 into 8 and 9, and reset the charset, underline, strikethrough and weight on OK. Now sizes are points
  throughout, the default is 8.25, and the dialog keeps what it has no say over. A control on the default font draws
  as it did. The dialog's sample now redraws as a size or a style is chosen; it kept the ones it opened with.
- **Missing VB6 global objects fail explicitly** (#871). Reading `Screen`, `Clipboard`, `Printer`, `Printers` or
  `Forms` no longer silently returns Empty without Option Explicit. Bare and member reads report an unsupported
  object, which On Error cannot swallow; declared variables and user functions still take precedence.

- **Multiple file numbers on one source line** (#1146). `Line Input #1, s: Print #2, s` and `Close #1, #2`
  now parse in both the interpreter and language server. The date-literal token no longer consumes the file statements.

- **FontName reads and sets at run time** (#1141), on a control or the form. `Label1.FontName = "Wingdings"` stopped
  the run with "Method or data member not found"; now it switches the font, with the charset that makes a symbol font
  draw its symbols. As vb6.exe measured, a family the machine doesn't have is 380, "Invalid property value", which a
  program can trap to fall back. On Linux and macOS, Windows' text fonts are taken and drawn in a substitute, but a
  missing symbol font is 380.
- **A caption in a symbol font draws the font's symbols** (#1133), where the font is installed. A Wingdings Label
  captioned `PMJ` drew those letters; now it draws a flag, a bomb and a smiling face, as vb6.exe does, and Marlett draws
  its ticks, cross and arrows. Labels, CommandButtons, CheckBoxes and OptionButtons are covered. The caption still reads
  back as the letters. On a machine without the font, as Linux and macOS are for Wingdings, the letters are drawn as
  before.
- **A menu's Click as it opens, and a shortcut's parents' Clicks** (#1344). A menu that holds items never raised its
  Click, so a program could not enable or disable the items before they showed: Notepad's Edit menu offered Cut with
  nothing selected. A shortcut raised its own item's Click alone, even for an item disabled or hidden. Now opening a menu
  raises its Click, and a shortcut raises each menu's from the top down to its item's, stopping at the first that is
  disabled or hidden, with each asked after the Clicks above it have run, as vb6.exe measured.
- **A program can use its menus** (#1126). Naming one in code stopped the run with "Variable not defined", so
  `mnuFile.Enabled = False` or a ticked level menu couldn't run, and choosing an element of a menu array called its
  Click without the Index. Now each menu has its name, a menu array works like a control array, Index on its Click
  included, and Caption, Checked, Enabled, Visible and Tag read and write with VB6's refusals. `Load` and `Unload`
  grow a menu array, as a recent-files list does. Choosing an item no longer ticks or unticks it, which is the
  program's to do. Every rule was measured on vb6.exe.
- **A form's fonts are drawn in the family they name** (#1132), when it is installed. Every VB6 font used to draw in one
  sans serif: Courier New wasn't monospaced, Times New Roman had no serifs. The family was looked up only in a bundled
  collection that holds no fonts; now the installed fonts come first. VB6's own default, MS Sans Serif, isn't shipped,
  so a form on it still draws in the fallback.
- **A Label with `BorderStyle = 1` draws its border** (#1077), at run time and in the designer. It drew none, so a
  grid of bordered Labels, such as the Battleship and Minesweeper demos' fields, was one block of colour. As vb6.exe
  draws it: a sunken edge two pixels deep when Appearance is 3D, and one pixel of the window-frame colour when Flat.
  `vbWindowFrame`, which had no colour, now has Windows 10's.
- **A form with a menu bar runs with its whole client area** (#1127). The bar sat inside the form's ClientHeight, so
  the bar's height was cut off the bottom of the form, and reading `ScaleHeight` stopped the run. As in VB6, the bar is
  now above the client area, 19 pixels high, and `ScaleHeight` is the area below it, so a form can fit itself to its
  content with `Height = (Height - ScaleHeight) + needed`.
- **A form holding a control HexIDE cannot create is refused, rather than run without it** (#857). Examples are a
  `VB.Data`, a `VB.OLE` and any ActiveX control. The form used to run with the control, and everything inside it, left
  out. Code naming it read Empty or wasn't defined, a call on it under `On Error Resume Next` was skipped with
  `Err.Number` 0, and a form whose code never named it ran as a different program. Now the run stops before the form
  is built, naming each control it can't create, and the program can't trap it. The designer still opens the form.
- **A run-time error nothing handles halts the program where it stands, as VB6 does** (#1011). The event procedure that
  met it used to be abandoned while the run carried on, so a Timer tick or a click still ran, on whatever state it had
  left half done. Now the dialog offers End and Debug, and nothing runs behind it. Debug breaks at the statement with the
  procedure still live, so Locals and the Immediate window work there, and Continue runs the statement again. A handler
  in the code that raised an event, `RaiseEvent` included, no longer catches the event's error, as in VB6.
- **`SetFocus` runs** (#1061). It stopped the run as not implemented, on every control. Measured on vb6.exe:
  - the control has the focus at once;
  - its LostFocus and GotFocus come after the handler that called SetFocus;
  - it is error 5 on a form not yet shown, or on a disabled or invisible control.

  A TextBox now raises GotFocus and LostFocus too.
- **A TextBox, Frame or scroll bar saved disabled runs disabled** (#1061). `Enabled = 0` in the form was dropped, and the
  control ran enabled.
- **Operators group as VB6 groups them** (#1074). Each had a precedence level of its own, so `10 - 2 + 3` was 5,
  `5 * 5 \ 2` was 10, `1 < 2 = True` was False and `Not False And False` was True, with no error. Measured on
  vb6.exe across the whole ladder: `*` and `/` share a level, then `\`, then `Mod`, then `+` and `-`, then every
  comparison, and operators of one level go left to right. A Boolean beside a number now compares as that number,
  True as -1, where it raised Type mismatch.
- **Formatting no longer changes a file's line endings.** The bundled language server joined its formatted
  text with bare line feeds, so a file VB6 wrote, with CRLF endings, was never already formatted: every
  save sent an edit covering the whole file, and applying it put LF endings into the code window.
- **Renaming a form now renames it in its file's `Attribute VB_Name` line as well as its `Begin` line.**
  Previously a renamed form was saved naming two different forms, one in each. The code window follows
  the rename as it happens, and Ctrl+Z there does not undo it.
- **Find with Use Pattern Matching and Find Whole Word Only no longer gives up at the first match that is
  not a whole word.** It reported the text as not found, however many whole-word matches came after it.
- **An open code window now follows a reload that changes whether its form can be saved.** Previously,
  when a form's file changed on disk so that HexIDE could now reproduce it, or could no longer reproduce
  it, the code window kept its old answer until it was closed and reopened. It stayed locked with the
  warning showing, or stayed typable with no warning. The designer already followed the reload.
  ([#475](https://github.com/hexide-io/HexIDE/issues/475))

- **A call written with a `$`, such as `Trim$(s)` or `Left$(s, 1)`, runs** (#1071). It stopped the run. The 23 library
  functions that have a `$` form answer as their plain form with the result made a String, so a Null is error 94, all
  measured on vb6.exe. A program's own String Function answers its `$` form. Any other `$` still stops the run,
  naming #742.
- **A control's `Move` runs, and a form's `ScaleWidth` and `ScaleHeight` can be read** (#1071). Both stopped the run.
  Move sets Left, Top, Width and Height as given, and checks before it moves, as vb6.exe was measured to: a negative
  size is error 5, and nothing moves.
- **Arithmetic on a numeric String works, as in VB6** (#1047). `Text1.Text * 2` used to stop the run with Type mismatch.
  - **The reading:** every operator now reads a String operand as `CDbl` does, in the program's locale, and the result
    type is the one a Double would give.
  - **`+`:** a String beside a String or Empty concatenates.
  - **`CDbl` and its relatives** read a thousands separator, so `CDbl("1,000")` is 1000 under en-GB (#770). They also
    read a trailing sign (`"6-"`), parentheses (`"(6)"`) and a `D` exponent (`"1D3"`), all measured on vb6.exe.
- **Two overflow edges now match VB6** (#234). Both were measured as p-code and in vb6.exe's IDE.
  - **`0 / 0` raises Overflow (6)**, not Division by zero (11), for every operand type except Decimal, which keeps 11.
  - **A Variant Single that overflows** widens to Double, as a Variant Integer widens to Long, instead of raising
    Overflow. Two typed Singles still raise.
- **`Unload Me` unloads the startup form, as in VB6** (#973). It used to stop the run as not implemented. Measured on
  vb6.exe:
  - **The events:** `Form_QueryUnload` (UnloadMode 1 from code, 0 from the close box), then `Form_Unload`, each with a
    Cancel that keeps the form loaded.
  - **What comes after:** the rest of the handler still runs. Then `Form_Terminate`, the form's fields are released,
    and the run ends.
  - **The close box** asks the same way, and a refusal keeps the window open.
  - **`Unload Me` in `Form_Load`** ends the run without showing the form. A second Unload, or one from inside
    QueryUnload or Unload, does nothing.
- **MouseDown, MouseUp and MouseMove are raised** (#974) on the form and on every control that has them in VB6:
  CommandButton, Label, TextBox, PictureBox, Frame, CheckBox, OptionButton, Image and ListBox. Before, no handler for
  them ever ran. Measured on vb6.exe:
  - **Order:** a CommandButton and a CheckBox raise Click between MouseDown and MouseUp, and the others after MouseUp.
  - **Arguments:** Button is 1, 2 or 4; Shift is 1, 2 and 4 for Shift, Ctrl and Alt; a control array's Index comes first.
  - **X and Y** are twips from the control's client area, even on a pixel form, and the form's follow its own ScaleMode.
  - **Image:** an Image takes the mouse, and Click, across its whole rectangle, with or without a picture (#861).
  - **No passing up:** an event a control doesn't handle is not passed to its container, as in VB6.
- **A running program's event handlers run one at a time, as in VB6** (#766). Under the debugger, a handler that gave up
  the thread at its time slice used to let any other event start and finish first, and let the statement that raised
  it run on. Measured on vb6.exe:
  - **What waits:** nothing runs inside a handler unless it calls `DoEvents`. An event that comes meanwhile waits its
    turn.
  - **Nested events:** a handler a statement raises (`Text1.Text = "x"` raising `Text1_Change`) finishes before the
    raiser goes on.
  - **`DoEvents`:** it now runs what is waiting, inside the caller, so a `Do: DoEvents: Loop Until clicked` loop sees
    the click.
  - **Timers:** a Timer delivers one tick, not several, once it is free, and never inside its own handler.
- **A control's Left, Top, Width and Height are twips at run time** (#933), as VB6 has them, measured on vb6.exe:
  - **What changed:** a program read and wrote them in pixels, as a Double. A .frm Left of 120 read 8, and
    `Text1.Left = 1200` moved the control 1200 pixels, fifteen times too far. They are now twips, as a Single.
  - **ScaleMode:** they are read in the container's ScaleMode: the form's, a PictureBox's for the controls inside it,
    and twips inside a Frame. A form's own Width and Height are twips whatever its ScaleMode.
  - **Rounding:** an assignment is rounded to whole twips, half to even, so `Left = 1.5` reads back 2 (#791).
  - **Labels:** a Label is at least a pixel wide and high.
  - **Not yet:** a User, centimetre or millimetre ScaleMode is refused (#990). The form's own size is its client
    size, without the frame (#852).
- **A module's Public variables and Consts are visible bare from every other module** (#880), as VB6 makes them,
  measured on vb6.exe:
  - **What changed:** `Debug.Print M`, where Module2 has `Public Const M = 3`, is 3, and `G = 7` stores into Module2's
    `Public G`, from a module, a class or a form. Before, both made a new variable of their own: a read gave Empty,
    `For i = 1 To M` ran no times, and under Option Explicit both were "Variable not defined".
  - **Order:** the module's own names come first, a procedure's local shadows them, and in a form the form's members
    come first too. They are found before a built-in constant of the same name.
  - **Clashes:** a name two modules export is "Ambiguous name detected" where it is used bare, and so is one module's
    variable beside another module's Public Sub or Function of that name.
  - **Private:** a Private variable, a module-level `Dim` and a `Private Const` stay invisible from other modules.
  - **Consts:** a module-level Const or array bound reads another module's Public Const bare, where it was refused, and
    a store into one is "Assignment to constant not permitted".
- **A bare name in a form's code is the form's member** (#995), as VB6 resolves it, measured on vb6.exe:
  - **The common case:** `Caption = "Ready"` sets the form's caption, and `If Width > 3000` reads its width. Before,
    the first made a new variable and the second read Empty, or both were "Variable not defined" under Option
    Explicit.
  - **Order:** the form's members come after the procedure's locals and the form's own variables and controls, and
    before a module's `Public` of the same name.
  - **Members HexIDE lacks:** one VB6's Form has and HexIDE doesn't, such as `CurrentX`, `ScaleMode` or `Cls`, is
    refused as not implemented rather than made a variable. `Me.ScaleMode` says the same, where it raised 438.
  - **Read-only members:** assigning one, such as `hWnd` or `Count`, bare or through `Me`, is VB6's own compile error.
  - **Calls with arguments:** a property called with arguments is VBA's function only for `Left("abc", 1)`, and is
    never a project procedure of the same name.
  - **The member table** is read out of VB6.OLB by a new `scripts/dump-typelib-members.ps1`, not written by hand.
- **A running form is an object, with a real `Me`** (#822, first slice). A form's code ran as a standard module, with
  `Me` and the form's name bound to its window. The form is now an instance of its module, as VB6 has it, measured on
  vb6.exe:
  - **`Me` is the form:** `TypeName(Me)` is `Form1` and `Me Is Form1` is True. `Me.Caption` and `Me.Hide` still reach the
    window, after the form's own procedures, variables and controls.
  - **A module reaches the form through its name:** `Form1.Caption`, `Form1.Score` and `Form1.Text1.Text` work from a
    standard module.
  - **A form's Public Subs aren't globals:** called unqualified from a module, one is "Sub or Function not defined", as
    in VB6, and a module's Sub of the same name is no longer "Ambiguous name detected".
  - **Declarations and events:** a form's declarations run before `Form_Initialize`, so an array sized by a Const is
    sized (#256, #760), and a module's Public Const reads its value from `Form_Load`. `Form_Initialize` is raised, and
    `Form_Load` runs before the window is shown and before `Form_Resize` (#994).
  - **`WithEvents` in a form** binds, so the form's handlers run (#972).
- **A store into a Const is refused, as VB6 refuses it** (#1003). `MK = 2` after `Const MK = 1` ran and overwrote the
  constant, so every later read gave 2. It is now VB6's compile error "Assignment to constant not permitted", raised
  as the statement runs. VB6 gives other messages for other statements, and so does HexIDE now, for a Const and an
  Enum member alike: a For counter, a For Each variable or a Mid target is "Variable required - can't assign to this
  expression", and ReDim or Erase of one is "Expected array". Measured on vb6.exe, native and p-code.
- **Select Case compares as VB6 does, and arithmetic overflows where VB6's does** (#987). VB6 treats a value as typed or
  as a Variant by where it came from, and both decide the result. Measured on vb6.exe, native and p-code:
  - **A typed selector** converts each Case value into its type, so a declared 2 is no longer `Case Is < 2.4`, and a
    declared Boolean True is `Case 3 To 6`.
  - **A Variant selector** compares as VB6 compares Variants: a Variant 5 is below `"10"` as text, and a Variant 2 is
    not `Case 1.7`. True is below False.
  - **A typed Function's result, a conversion function, a typed array element or Type field, and the typed intrinsics**
    (`Asc`, `Sgn`, `UBound`, `Len` of a typed argument and others) now overflow past their type's ceiling, as a
    declared variable does: `F() * 7000` with `F` an Integer returning 5 raises Overflow, where it gave 35000.
  - **A Variant array element, a Variant Type field, a late-bound member and the Variant intrinsics** (`Hour`, `Day`,
    `Mid`, `Left` and the rest) widen, as a Variant does: `w(0) * 7000` with `w(0)` holding 5 is 35000, where it
    raised Overflow.
  - **A Const, an Enum member, and an Enum parameter, return or Type field** are typed, as in VB6: `K * m` with
    `Const K = 5` and `m As Integer` 7000 raises Overflow, where it gave 35000. A Const declared `As Variant` is a
    Variant.
  - **A parameter or return declared `As Object`** is late-bound, as a Variant is, so its members' results widen.
- **A ByRef `As Variant` parameter writes back to its caller** (#1001). `Sub S(v As Variant): v = 5` left a caller's
  Variant, or a caller's `Integer`, as it was, because the parameter got a copy of anything but an array. It now
  shares the caller's variable, as an untyped parameter does. Along with it, an untyped or Variant parameter given a
  typed variable is a Variant in arithmetic, as in VB6: `v * 7000` with an `Integer` 5 is 35000, where it overflowed.
  And a constant passed ByRef goes by value, as VB6 passes it: a Const given to a parameter that writes it is
  unchanged, where it was overwritten, and an Enum member no longer raises "Assignment to constant not permitted".
- **An array assignment copies the array** (#970). `d = f` shared one array between the two names, so a write through
  either reached the other, with no error. Measured on vb6.exe, VB6 copies wherever it stores an array: into a
  variable, an element, a ByVal parameter and a For Each variable, and so the row-buffer idiom, `rows(i) = row`, keeps
  each row. Along with it:
  - **A ByRef `As Variant` parameter given an array shares it**, as an untyped one does, so a `ReDim` through it reaches
    the caller's array, and is VB6's 10 for a fixed one.
  - **An untyped or Variant parameter's `ReDim`** follows VB6's Variant rule even when the caller's array is declared:
    `ReDim v(-1)` raises 9 and keeps the caller's array, where it erased it.
  - **A copied array shares its objects, and each holder keeps them alive.** A local object array's objects now
    terminate at `End Sub`, as in VB6; they never terminated at all, and an object could terminate while another array
    still held it.
  - **A fixed array can't be assigned an array:** VB6's "Can't assign to array", or 13 through a parameter, where the
    fixed array was replaced.
  - **A For Each variable is Empty once the loop runs out**, as in VB6, even when there was nothing to walk; it kept
    the last element. `Exit For` still leaves it holding the element it reached.
  - **`End Sub` releases a procedure's scalar locals before its arrays**, each in declaration order, as VB6 does.
- **A Function declared to return a typed array takes only an array of its type** (#986). `F = Array(1, 2)` in a
  Function `As Integer()` returned a Variant array with no error, and `F = 5` in one `As Long()` returned an Integer.
  A mismatch now raises Type mismatch at the store, as VB6 does, or stops the run with VB6's compile error "Can't assign
  to array" where VB6 would not have compiled the program. A String stored into a Function declared `As Byte()`, which
  VB6 converts into its bytes, stops the run as not implemented (#998). Before, it was returned as a String.
- **Under On Error Resume Next, a block whose condition or header raises now runs, as VB6's does** (#839). HexIDE skipped
  the whole statement, so the program carried on down a path VB6 would not take, with no error. VB6 goes on at the
  first statement inside the block. Measured on vb6.exe, as native code, as p-code and in the IDE:
  - an If or ElseIf runs the branch whose condition raised;
  - a Case test runs its own Case;
  - Do While, Do Until and While run the body, asking again each time round, and a Loop While or Until ends the loop;
  - With runs its body with no object;
  - For and For Each run the body once, and then carry on as the IDE does.
- **For Each over something it cannot walk raises VB6's error** (#839). An unallocated array ran no passes and said nothing;
  it now raises 92. Nothing raises 424 and any other non-object value 13. Over an object, which HexIDE cannot walk yet,
  it now says so rather than raising an error On Error Resume Next stepped over. A For Each whose collection itself
  raises now stops the run unless the collection is a call of one of the project's own Functions (#969), where before
  it ran on.
- **A For converts its start, limit and step into its counter's type** (#839), as VB6 does. A Byte counter's
  `Step -1` is an Overflow, and so is any bound its type cannot hold, raised at the For statement. A fractional bound
  rounds half to even into an Integer or Long counter, so `For i = 1 To 2.5` runs to 2. Each was read as a Double, and
  a fractional one raised a trappable Type mismatch that skipped the loop under On Error Resume Next. A bound still
  fractional for its counter, a Single, Double, Currency, Date or Variant one, now stops the run as not implemented
  (#983), where it skipped the loop without a word.
- **A typed parameter keeps its type** (#839), as a variable declared with it does. `Optional i As Long = 7` holds a
  Long, a store of 2.5 into a `ByVal i As Long` gives 2, and a store of 300 into a `ByVal b As Byte` raises Overflow.
  The parameter's slot recorded no type, so each held whatever was stored into it. The parameter is now a ceiling in
  arithmetic too, as a declared variable is: with a `ByVal i As Integer` holding 5, `i * 7000` raises Overflow, where it
  widened to 35000. For the numeric types only; a String, Boolean or Date parameter is still not converted (#874).
- **A Function declared to return an array returns it** (#975). `Function F() As Long()` returned a Long 0 with no
  error, because its return was read as the element type, so `d = F()` left `d` a scalar and the next `d(1)` stopped
  the run. It now returns the array, and one that never assigns its name returns an empty one, as VB6 does. A Property
  Get declared the same way is fixed with it.
- **Calling a procedure that carries an `Attribute` line no longer stops the run.** VB6 writes these inside a
  procedure for a default member (`VB_UserMemId`), a description from Tools → Procedure Attributes
  (`VB_Description`), a property category (`VB_ProcData`) and more, and after a module-level variable for its
  description. Each stopped the run with "Attribute not implemented". They are metadata, and now run as nothing,
  as in VB6. The debugger treats them as blank lines: a step passes over them, and Set Next Statement refuses one.
  A default member they declare is not read yet, so a use of the object as a value is refused naming #174.

- **A member call inside `While...Wend` runs** (#882). `Debug.Print x`, `Err.Clear` or `rs.MoveNext` in a
  `While` body stopped the run with "Sub or Function not defined", naming the object, so the common
  `While Not rs.EOF ... rs.MoveNext ... Wend` loop could not run at all. `Do While` was unaffected.

- **A condition is read as VB6 reads it** (#847). A block `If` or `ElseIf` refused anything but a Boolean with an
  error numbered 0, so under On Error Resume Next it skipped its body with nothing in Err. A one-line `If` raised 13
  for anything but a Boolean, and the loops raised 13 for a Long, a Double, Empty or most strings. So
  `If Len(s) Then`, `If InStr(s, "x") Then`, `If flags And 4 Then` and `Do While n` with a Long `n` stopped the run.
  Every form now reads its condition as `CBool` does: a number holds when it is not 0, a string when it reads as True
  or as such a number, and Null and Empty do not. An object in a condition raises VB6's 438 and Nothing 91, where
  each was a trappable 13, or in a block `If` or `ElseIf` the error numbered 0. `If Err Then` reads Err's Number
  (#840), and a class with a default member and a control are refused until #174 and #790.

- **A subscript, a control-array index or an array bound may be any number** (#849). Only an Integer, a Byte or
  an integral string was accepted, so `a(i)` with `i` a Long, `a(UBound(a))` and `ReDim b(n)` stopped the run.
  Each is now coerced as VB6 coerces it. A subscript or a bound is coerced as `CLng` does: `a(2.5)` is `a(2)`, Null
  raises 94, `"x"` 13, and an omitted `Optional` 13. A control-array index is coerced as `CInt` does, so one outside
  an Integer raises 6 and a negative one 341, and an omitted `Optional` is 13 there too. `UBound`'s and `LBound`'s
  dimension is coerced as `CInt` does as well, and an omitted `Optional` one is 13, where it was read as dimension 1.
- **`ReDim` follows VB6 at its edges** (#849). An upper bound below its lower raises 9 and erases a variable declared
  an array, or an array parameter, keeping its type. A Variant keeps its array when any dimension's upper bound is
  one below its lower, and is emptied with 7 otherwise. `ReDim b(2147483647)` raises 7, Out of memory, where it
  raised 9. A Variant that never held an array can be `ReDim`med at all; it stopped with an internal error.
- **A typed array parameter shares its caller's array** (#849). `Sub P(b() As Long)` took `b` for a Long, so an array
  passed to it was copied rather than shared, and narrowed as if it were a scalar.
- **A fixed array can't be resized through a parameter** (#873). `ReDim b(10)` in a callee given a caller's
  `Dim buf(3)` raises VB6's 10, "This array is fixed or temporarily locked", and leaves `buf` as it was, whether `b` is
  an array parameter or an untyped one; named directly, `ReDim` of a fixed array is VB6's "Array already dimensioned".

- **A module-level `Const` is checked as VB6 checks it** (#760). Its value may read only other constants: a Const
  declared below it or the Const itself, a variable, a user function and any function call are VB6's "Constant
  expression required", where they ran from whatever HexIDE made of them. Another module's Public Const is read, bare
  (#880) or qualified, once that module's declarations have run, and refused before (#893), rather than read as
  Empty. A module naming itself is "Circular dependencies between modules".
- **A `Const` holds its value as its `As` type** (#873), at module and procedure level: `Const C As Integer = 2.5` is
  2, and `Const HALF As Long = GRID / 2` rounds as a store into a Long does. A value the type can't hold is VB6's
  compile error: `Const C As Byte = 300` is "Overflow", `Const C As Integer = "x"` "Type mismatch", and
  `Const C As Integer = Null` "Invalid use of Null". The As clause was ignored, so a module array sized from such a
  Const had the wrong bounds. A String given to a `Const … As Date` is refused, until HexIDE reads a date string as
  VB6 does (#967).
- **A module-level array sized by a `Const` loads** (#760). `Private mGrid(0 To GRID_MAX)` made the whole module
  fail to load; so did a typed literal such as `Dim d(2&)`. Its bounds are read where it is allocated, by VB6's
  rules: a Const or Enum declared below, a variable, a user function, and the built-ins VB6 refuses there are its
  "Constant expression required". A constant qualified by its library, container, module, Enum or the project is read
  (`VBA.vbCrLf`, `KeyCodeConstants.vbKeyA`, `Module2.Cols.colMax`), and `Len` sizes only from a String: of a number
  it is VB6's "Variable required". A dimension with no values, such as `Dim d(-1)` or `Dim d(5 To 2)`, is VB6's
  "Range has no values", in a module, a class or a procedure, where it built an empty array or raised 9. Another module's Public Const before that module has run
  (#893), and its Enum member read unqualified (#889) are refused, rather than sized from Empty. A form's run refuses
  such an array, naming it, because it does not run the module's declarations yet (#256). A procedure's `Const`s are declared before its `Dim`s, so
  `Dim a(P)` inside a procedure sees `P` too.

- **A method called from `Class_Initialize` no longer terminates the object** (#850). The call fired
  `Class_Terminate` in the middle of `New`, and the real `Class_Terminate` never ran afterwards.

- **An object used as a value no longer gives a wrong value.** A class with no default member raises VB6's 438
  wherever a value is wanted, where HexIDE raised 13, printed its CLR type name (`"v=" & k`, `CStr(k)`,
  `Debug.Print k`), read it as 0 (`Val(k)`, a ByVal Long argument), or matched nothing in `Select Case`. That covers
  every operator, a Let, a ByVal argument for a typed parameter, every condition, bound and Case, `Randomize`,
  `Err.Raise`, `Error`, `ChDir`, `ChDrive`, the Mid statement, and every built-in function's value arguments.
  `TypeName` and `IsObject` still take the object, and `VarType` and the Is functions answer about it. A class
  with a default member, which VB6 reads in all those places (bar a few, measured, where a Function default member
  gives 450: `Format`, `Abs` and `Not` through a Variant, and in the IDE a Case value against an Empty selector), stops
  the run with a message saying class default members are not implemented yet (#174),
  rather than an error `On Error Resume Next` would step over.
- **`Err` used as a value is its `Number`**, as in VB6: `"Error " & Err`, `x = Err`, `Err + 1`. It was refused. App
  used as a value raises VB6's 438, where it printed a .NET type name (VB6 also empties a Variant `v` in `v = App`
  first, which is #878). A control still gives its .NET type name in `"v=" & Text1`, `CStr(Text1)` and
  `Debug.Print Text1`, until #790.
- **`TypeName(Err)` is "ErrObject" and `TypeName(App)` is "App"**, as in VB6. They were HexIDE's own class names.
- **`Join` and `Filter` of something that is not an array raise 13**, as in VB6, where they raised 5. Of an array
  never allocated, `Join` gives `""` and `Filter` an unallocated String array, where both stopped the run with an error no
  `On Error` could trap.
- **`Filter` always gives a String array**, as in VB6, where it gave a Variant array for any allocated input. A Null
  match, switch or compare is 94, even for an array never allocated, and even beside something that is not an array.
  The switch reads as `CBool` does, so an Empty one no longer raises 13. The compare is 0 for binary and 1 for text;
  2 and negative values raise 5, where every value but 1 was binary; and 3 and above, which VB6's answers are
  consistent with reading as a Windows locale identifier, are refused (#881). A Nothing compare is 91, where it read as
  0, a binary compare. `Filter` converts every element first, and checks its compare only when it compares an element
  with the match: of an array with no element it gives an unallocated String array, and an empty match keeps every
  element, so `Filter(list, "", True, 2)` is the whole list where it raised 5. An object among its match, switch and
  compare is read only as its turn comes, so `Filter(d, k, "x")` is the switch's 13 where it was k's 438.
- **`Join` and `Filter` convert each element as a store into a String does**, as in VB6: a Null element is 94 and an
  array 13, and Nothing 91, where they read as `""`, the CLR type name and `""`. `Join` of an array with no element
  gives `""` without reading its delimiter; with one, a Null delimiter is 94 and a Nothing one 91, where both read as
  `""`. A Nothing match in `Filter` is 91 too.
- **The Immediate window's `? obj` no longer prints the class's name.** A class with no default member is 438, as
  `Debug.Print` of one is in VB6, and one with a default member says that reading it is not available in break mode.
- **A syntax error when a program starts, or a compile error while it runs, is reported like a run-time error.**
  Each was a message box nothing recorded, so a program that never started was reported to automation as having run
  and ended. Both now appear in the run-time error dialog and are kept for `get_last_runtime_error`, and a syntax
  error at the start makes `run_project` answer that nothing is running. That answer now starts its own paragraph
  rather than running on from the error's last word.
- **Every start that never becomes a run is reported as one.** A project with no startup form, and a Sub Main project
  with no usable `Main` or with two, were a message box, or a run that ended, and `run_project` answered success.
  Each is now a failed start, in VB6's words, recorded for `get_last_runtime_error`.
- **A syntax error names the module it is in.** One in a `.bas` or `.cls` was blamed on the startup form.
- **A declaration HexIDE cannot load is described the same way whichever startup object the project has.** The form
  path gave the raw exception after the form's name.
- **Run To Cursor from design mode no longer leaves its target behind when the start fails.** After fixing the error,
  a plain F5 paused at that line as though at a breakpoint.
- **A message box shows the buttons and icon it was asked for.** `MsgBox "Sure?", vbYesNo + vbQuestion` showed
  OK alone with no icon, because every in-box constant is a Long and the style was read only as an Integer, so a
  Yes branch could never run. The answer is a Long, as in VB6. A style VB6 does not accept, such as 6 or 12, is a
  plain OK box, as in VB6.
- **Closing a message box answers as VB6's does.** It answered OK whatever the buttons, so closing an OK/Cancel
  box took the program's OK branch. A box with a Cancel button now answers Cancel. A Yes/No or Abort/Retry/Ignore
  box can no longer be closed without an answer, which is Windows' own rule for those two.
- **`CInt`, `CLng` and the other numeric conversions no longer read an object or an array as 0.** `Nothing`
  raises 91, an array 13 and an object 438, as in VB6, and so do `RGB`, `QBColor` and a message box's style. A
  control, which VB6 reads through its default member, stops the run with a message saying default members are not
  implemented yet (#790), and so does a class with a default member (#174). An omitted `Optional` argument converts to
  448, as in VB6, where it read as 0.
- **A message box stays answerable when its form hides.** `Me.Visible = False` while a box was open made the box
  disappear and answer Cancel, or stopped the code waiting on a Yes/No box, and a box opened from a form already
  hidden ended the run as Internal error 51. The box now waits for its answer, shown on its own.
- **Restart with a message box open no longer runs the old program on.** The box closed, and the code waiting on it
  carried on beside the new run with a Cancel nobody pressed, or stopped the new run. It now ends with its run.
- **A name in a different case is the same name, as in VB6.** `abc = 5` after `Dim Abc` used to make a new
  variable and leave `Abc` as it was, and `foo = 7` inside `Function Foo` returned 0. Variables, constants,
  parameters, a Function's return value, controls, properties and `Debug.Print` now match in any case, and so do
  property names in a `.frm`, a form's own included, and the fields of its Font block.
- **A `Const` inside a procedure no longer overwrites a module variable or `Const` of the same name.** It is the
  procedure's own, as in VB6, and the module's value is unchanged after the call.
- **A Label's background colour set by a program now shows.** `Label1.BackColor = vbRed` read back red but left
  the label as it was; now it paints, for a label in a control array too. A program can make a transparent Label
  opaque with `BackStyle = 1`. A transparent Label ignores a `BackColor` set while it is transparent, as VB6 does.
- **Every control's colour reads back as VB6's number**, `255` rather than `&H000000FF&`, so `If Text1.BackColor
  = vbRed` is True and `Debug.Print` shows what VB6 shows. A Label's `ForeColor` and a TextBox's colours from the
  form read back at all, where they used to read as black; a TextBox now keeps the colours a form gives it. A
  ListBox's and ComboBox's text colour defaults to the window text colour, as in VB6.
- **A form keeps a property value HexIDE cannot read yet**, such as a palette colour, and saves it back as it was
  written. It used to be dropped from the file on the next save.
- **A running program writes and reads dates and numbers in the user's regional settings**, as VB6 does. The IDE
  ran every program in the invariant culture, so `CStr(#1/2/2003#)` showed `01/02/2003` on a UK machine, where VB6
  shows `02/01/2003`, and `CDate("1/2/2003")` was 2 January rather than 1 February. `CStr`, `Format`, the month and
  day names, `CDate`, `CDbl`, `IsDate`, `IsNumeric` and a two-digit year in `DateSerial` now follow the user's
  separators, date order and names, so text a program wrote reads back as the same value. Two things stay VB6's own
  whatever the locale: a date is Gregorian, so a Thai, Persian or Saudi machine still counts 2003 as 2003, and a minus
  sign is a plain `-`. A date the locale's order cannot make is read the other way round, as in VB6: `CDate("1/13/2003")`
  is 13 January on a UK machine. The IDE's own language changes none of this. The numbers written in the source
  itself are a separate matter, still read in the machine's culture in a made EXE (#781).
- **`s = Text1` no longer stores a .NET type name.** In VB6 a control where a value is wanted stands for
  its default property, so `s = Text1` reads its text. HexIDE does not implement that yet, and the assignment
  stored `HexIDE.Runtime.BuiltinControls.VBTextBox` in the String, or the control itself in a Variant, without a
  word. It now stops the program and says so, until default properties land (#790). `Set`, `For Each` and
  passing a control to a procedure are unaffected.
- **Numbers and dates turn into the text VB6 makes of them.** `CStr`, `&`, a String variable, `MsgBox` and
  `Format` with no mask wrote .NET's text: `CSng(1) / 3` came out as `0.33333334` and `0.1 + 0.2` as
  `0.30000000000000004`, where VB6 writes `0.3333333` and `0.3`. A date came out with a midnight time attached.
  Now a Single has seven significant digits and a Double fifteen, with E notation where VB6 uses it, and a date
  leaves out a midnight time or the zero date. A time written on its own, `#10:30:00 AM#`, is now that time on
  VB6's zero date rather than on today's date.

- **`End` ends the program.** It used to stop the program with an error saying HexIDE did not implement it, so a
  `Sub Main` that finished with `End` showed that error. Now the program stops at `End`, as in VB6: nothing after
  it runs, its forms close without their unload events, and no `Class_Terminate` runs.

- **A program that reaches something HexIDE cannot run yet now says so.** A statement, function or control
  method the interpreter does not implement used to stop the program without a word: a click handler ended
  part way while the program carried on, and a `Sub Main` project opened nothing at all. The run now ends,
  and the runtime-error dialog says HexIDE does not implement it, naming it in most cases; a few still carry no
  name (#181). An error handler does not swallow it, because carrying on past a statement that never ran would
  give a wrong answer. An error that ends `Sub Main` now appears in the same dialog as a form's.

- **`Label1.Caption = 5` sets the caption to "5".** A value of another type assigned to a control property is
  converted to the property's type, as VB6 does, instead of stopping the program. That covers a number into a
  caption or text, a string into a position, `Timer.Interval`, a scroll bar's `Value` or a list's `ListIndex`, a
  number into a check box's `Value`, a string into a colour, and a colour copied from one control to another. A
  value VB6 refuses raises VB6's own error, which an error handler can trap: Type mismatch for text that is not a
  number, Invalid use of Null, Overflow, and Invalid property value for one outside the property's range
  (`Check1.Value = 3`, `HScroll1.Value = 150`, `List1.ListIndex = 3` on a three-item list). `Tag` takes `Empty`
  as `""`, and reads `""` before anything is put in it. A date into a caption is written in the user's date
  format, as `CStr` writes it. Where HexIDE cannot give VB6's answer yet, the program stops and says so rather than
  carry on with a wrong one: a palette colour; a scroll bar whose `Min` is above its `Max`; a control on the right of any
  property, `Tag` and `Enabled` included.
- **`Command1.Enabled = Empty` disables the button**, as `CBool(Empty)` is False. It raised Type mismatch.
  `CDbl("NaN")`, `CDbl("Infinity")` and `CBool("NaN")` raise Type mismatch, and `CDbl("1E+400")` raises
  Overflow; .NET's spellings of those used to get through as numbers, so `Enabled = "NaN"` enabled the button.
  `CBool("&H10")` is True, as in VB6; it raised Type mismatch.

- **A language server that orders changes by version no longer ignores the first edit to a file.** That edit repeated the version the file was opened at, so such a server kept analysing the text
  from before it until the next edit.
- **Bookmarks and breakpoints are no longer lost on the way to their file.** A bookmark set just before
  closing the project, or HexIDE, was not saved, and nor was one set in a project of a group while another
  project changed or closed. A save that failed, because another program held the file, is now made again
  when the project closes.
- **The per-project file they live in (`<project>.user.hexproj`) keeps what it does not know.** Anything a
  newer HexIDE wrote into it is kept, even when both had the project open at once. It goes with the project
  when the project is saved somewhere else, is not created by a save with nothing to record, and is left
  untouched when it cannot be read rather than overwritten.
- **Saving a VB6 source file no longer changes text you did not edit.** A file whose accented characters
  happened to read as valid UTF-8 was decoded that way and written back changed, so one ASCII edit could alter
  an unrelated comment or string. A file now keeps the encoding it was read in, a UTF-8 byte-order mark
  included. A file an earlier HexIDE saved as UTF-8 still opens as UTF-8. A file the project only carries, such
  as a README, is not covered yet: its editor still rewrites any byte that is not valid UTF-8 (#745).
- **On Windows a source file is read in the machine's own code page, as VB6 reads it.** A file without a
  byte-order mark was shown as Latin-1, so a Western file's curly quotes and euro sign appeared as control
  characters, and a Japanese file on a Japanese machine as mojibake. A file that code page cannot read still
  opens, read byte for byte, and HexIDE now says which files it read that way. Each saves back unchanged. On
  macOS and Linux, which have no such code page, a file is read as Windows-1252, so curly quotes show there too.
- **An edit a file's code page cannot hold is refused, and nothing is written.** Such an edit used to be written
  anyway, in bytes VB6 on that machine reads differently. A character beyond Latin-1 turned the whole file into
  UTF-8, and one within it became its Latin-1 byte. Now a warning names the file, its code page, the
  character, and the line it is in, which matters when the character is in a property or a file name rather than
  the code just typed. A save of several files checks every one before it writes the first, so one such character
  leaves them all as they were. That covers Save Project, Save All, the save offered when a project closes, Make
  Project Group, and writing a new project to its folder. Make refuses before it asks where to put the program. A
  new form or module whose name the code page cannot hold is refused before anything is created.
- **Dismissing a file picker part-way through a save leaves every file as it was.** Saving a project with two
  new files asked where to put each in turn and wrote as it went, so cancelling the second left the first
  written. Every picker an operation needs is now answered before anything is written, Make Project Group's
  included.
- **A form or module that fails to load keeps its line in the project file.** The next Save Project removed the
  line from the `.vbp`, so the file dropped out of the project in VB6 too.
- **A save HexIDE refuses no longer leaves an automation tool waiting.** `set_file_content`,
  `set_control_property` and `add_control` waited for someone to close a dialog. They now reply at once with
  the refusal, and the change stays in the IDE.

## [0.2.0] — 2026-09-24

Most of this release is about not losing work. A save now gives back the file it read, and a form HexIDE
cannot give back is refused rather than quietly damaged. Running real code gets a long way further too,
and a language server that goes wrong can now be seen going wrong.

### Added

- **Frames and PictureBoxes hold controls**, in the designer and at run time, positioned relative to their
  container as in VB6. Forms that nest controls can now be opened, edited and saved.
- **The Image control.**
- **More of the language runs:** `Implements` and interface-typed variables; code without
  `Option Explicit`, with variables created on first use; the `App` object; `IsMissing`, with VB6's rules
  for omitted `Optional` arguments; line numbers as `GoTo` targets; and some 700 built-in constants
  (`vbRed`, `vbKeyReturn` and the rest) with their correct values and types, qualified names included.
- **`Sub Main` as the startup object**, so a code-only project with no forms runs. Project Properties
  offers it.
- **Files a project carries but does not compile** (a README, a data file) are kept byte for byte on save,
  appear in the Project Explorer and open in the code editor. **Project → Add File** now works, and
  double-clicking the project node opens a live overview of its type, startup object and members.
- **Attach any language server by editing `lsp-servers.json`**, with no rebuild, over stdio, WebSocket or a
  named pipe. A document goes to every server that claims its language, including files the project only
  carries. [How to configure one](docs/language-servers.md).
- **A Protocol Inspector** (Tools menu) showing the whole conversation with every language server on one
  timeline, with failures marked and every message viewable, and a connection list saying which servers are
  attached and how far a failed one got. A conversation can be exported with paths and names
  pseudonymised, so it is safe to attach to a bug report.
- **More from language servers:** Go to Declaration (Ctrl+F12), workspace-wide symbol search in the Object
  Browser, code actions, folding for carried files, and servers that only report errors when asked. A
  diagnostic now shows its rule code, its documentation link and which server raised it, and a
  server's messages about itself reach the status bar and the log.
- **Command-line options:** `--help` (also `/?` and `-h`); `--user-data-dir`, which keeps a session's
  settings, layout and server configuration apart from your own; and `--capture-lsp`, which records
  server conversations from the first handshake. [All of them](docs/command-line.md).

### Changed

- **Forms are written exactly the way VB6 writes them.** Of the 22 form and control files VB6 ships in its
  own `Template` folder, 21 now survive open-then-save byte for byte. Project files, standard modules and
  class modules round-trip too.
- **A form HexIDE cannot reproduce opens read-only**, with a banner saying so, instead of being saved
  lossily. That covers a form opened without its `.frx` and binary content HexIDE does not model. The one
  VB6 template still refused, `Web Browser.frm`, keeps its pictures on an ActiveX ImageList. Save As is
  refused before the file picker opens, and a form's `.frm` and `.frx` are written together or not at all.
- **Calling a built-in function HexIDE does not implement yet raises an error**, instead of silently
  returning nothing.
- **The Properties window names enum values the way VB6 does** (`0 - None`), and Enter commits a value.
- **Add-ins can ship their own versions of shared libraries**, instead of getting whichever version the IDE
  happened to load first.

### Fixed

#### Losing work

- **Undo in a freshly opened code window could empty it**, and the next save wrote the module with no
  code. Undo now stops at the code as it was loaded.
- **A UserControl's code could go missing** when it was opened from both the designer and the Project
  Explorer.
- **Saving a form rewrote its size and position**, so it reopened in VB6 with the wrong frame and scale.
- **Make EXE and saving a project to a folder left modules, classes and UserControls behind**, with the
  project file pointing outside the package.
- **Unsaved projects shared one scratch folder**, so a new project could overwrite, or inherit, files from
  an earlier session.
- **A save dropped** menus' submenu structure, controls' membership of their Frame, fonts, `Tag`,
  ListBox and ComboBox `List` data and other `.frx`-backed properties.
- **An untouched project reported itself modified**, because its `.vbp` did not round-trip.

#### Running code

- **In a single-line `If`, statements after a colon ran even when the condition was false.**
- **Starting from a form did not load the project's modules and classes**, so a call into a `.bas`, or
  `New` on a class, failed under F5.
- **Variant arithmetic raised Overflow** where VB6 widens to `Long` or `Double`. Declared `Integer` and
  `Long` still overflow where VB6's do, and arithmetic no longer produces Infinity or NaN.
- **Runaway recursion crashed the IDE.** It now raises error 28, *Out of stack space*.
- **A skipped argument (`Foo 1, , 3`) shifted the ones after it.**
- **A form's menu bar was missing at run time**, `MsgBox` ignored its title, and `OptionButton.Value`
  raised error 461 when set from code.
- Member chains such as `a.b.c.d` stopped after the first dot, doubled quotes in string literals stayed
  doubled, `Select Case` needed identical types, and a variable declared `As` a type did not coerce what it
  was given.
- Colon-separated statements, `Rem` in every form VB6 allows, a label on the same line as a statement, and
  line continuations aligned with tabs were misparsed, some badly enough that the module would not load.

#### Debugging

- **F5 silently did nothing when the startup form could not be built**, and the IDE believed a program was
  running. It now says why.
- End on the runtime-error dialog ends the run, and the dialog offers only the buttons that work. An
  intermittent crash when stepping from a breakpoint is gone.
- Breakpoints and bookmarks follow a document through a rename, a first save and Save As, and two
  projects in a group can each have a `Module1` without their marks colliding.

#### Language servers

- **A new, unsaved project had no language server**, so there were no error underlines, go-to-definition or
  rename until it was saved.
- **A third-party server could silently switch off every language feature**, by describing its features
  with option objects rather than true/false, or by never answering at startup. One failed request no
  longer kills a connection.
- Diagnostics from servers that normalise paths were discarded or shown twice, underlines vanished when a
  code window moved dock or after Make EXE with the real VB6 compiler, and the first module opened never
  folded.
- Servers are shut down as the protocol specifies, so they exit cleanly instead of reporting a crash.
- A pipe name too long for the platform, typically 43 characters on macOS, is refused when the configuration
  is read, with the reason, instead of failing later with an unrelated socket error.

#### Everything else

- Screen readers announce real names for designer controls, New Project templates, Properties rows,
  toolbar buttons, tool windows and Project Explorer nodes, instead of internal type names, and read
  captions without the access-key underscore. Nineteen Standard toolbar buttons gained their tooltips.
- A crash writes its stack trace to the IDE log on every platform, and the log rolls over at its size
  limit instead of stopping.
- Renaming a control to a name VB6 does not accept is refused, instead of producing a `.frm` VB6 cannot
  load.
- On Linux, settings, add-in consent and revocations are stored in your home config folder, not wherever
  HexIDE was launched from.
- Locking the controls on a never-saved form no longer opens Save As.

## [0.1.0] — 2026-08-16

The first versioned build. Before this, nothing in the tree carried a version at all, so a shipped
binary could not be identified.

### Added

- **A cross-platform Visual Basic 6 workbench** — form designer, toolbox, project explorer, properties
  window, menu editor and code editor, running natively on Windows, macOS and Linux.
- **Native VB6 file format support.** Reads and writes `.vbp`, `.vbg`, `.frm`, `.cls`, `.bas`, `.ctl`,
  `.pag` and their binary companions. Content HexIDE does not recognise is carried through verbatim, so
  a project opened here still opens in VB6. Saves are atomic, so a crash cannot truncate your work.
- **A VB6 interpreter** for running code in the IDE — classes, `Property Get`/`Let`/`Set`, `WithEvents`,
  user-defined types, `Enum`, object arrays, error handling, and around eighty intrinsics including the
  full `Format` mask language. Its behaviour is checked against the real 1998 compiler rather than
  against assumptions about it. It is a demonstrator with a deliberate ceiling — see the Scope page.
- **Language tooling** over an out-of-process LSP server: syntax diagnostics, document symbols, and
  keyword and declared-name completion.
- **Thirty languages**, four of them right to left, covering the whole interface.
- **A signed add-in system** with offline verification, per-add-in load contexts and a first-load
  consent gate, plus a bundled AI Chat add-in (bring your own key).
- **A bridge to the real VB6 compiler on Windows**, so Run and Make produce genuine native executables
  when VB6 is installed.

### Fixed

- **Every VB6 option button crashed the IDE on render.** `Classic.Avalonia.Theme 11.3.0.3` is compiled
  against Avalonia 11 and called a `StreamGeometryContext.ArcTo` overload that Avalonia 12 replaced, so
  drawing a radio bevel threw on the render thread and took the process down — in the designer and at
  runtime alike, with nothing in the log.
- **Every VB6 check box rendered ticked**, whatever its value, because the tick's visibility rules
  targeted a part name the template does not use.
- **Dark themes made the code editor unreadable.** Syntax colours were hardcoded for a light background,
  leaving keywords at roughly 1.1:1 contrast on both shipped dark packs.
- **Release builds contained the wrong platforms.** Publishing without a runtime identifier bundled
  native payloads for every platform SkiaSharp ships, so the Windows archive carried Linux binaries and
  the download was six times larger than it needed to be.

[Unreleased]: https://github.com/hexide-io/HexIDE/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/hexide-io/HexIDE/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/hexide-io/HexIDE/releases/tag/v0.1.0
