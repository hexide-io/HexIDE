# Changelog

All notable changes to HexIDE are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). The leading `0.` is the stability promise:
there isn't one yet. Anything may change between 0.x releases.

## [Unreleased]

### Added

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
  to a number whose file is free, and automation's `add_file` refuses a name whose file is taken.

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
