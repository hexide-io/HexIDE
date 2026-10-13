# Changelog

All notable changes to HexIDE are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). The leading `0.` is the stability promise:
there isn't one yet. Anything may change between 0.x releases.

## [Unreleased]

### Added

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
- **No breakpoint or bookmark can be set on a file's header or a procedure's `Attribute` lines**, by F9,
  Ctrl+F2, a click in either margin or an automation client; the status bar says why. Those lines never run.
  One already there can still be removed.
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
  add to. A reply about one document now names it in `project` and `document`, spelled as the IDE spells them,
  and so does each `get_diagnostics` item. Where a reply also carries a `uri`, the tool's description now says
  what it is: the name the language server knows the document by. `get_debug_state` and `get_call_stack` name
  the running project.
- **For add-in authors: `AddinDocument.Content` is now the document's whole file**, as the code window shows it - a
  form's designer block, a class's header and the leading `Attribute` lines first, then the code - where it used to
  be the code alone. Every position on the add-in surface counts from the top of that file - selections, edits,
  navigation and diagnostics - where in 0.2.0 it counted from the first line of code, so line N of `Content` is
  line N everywhere. An add-in that counted lines itself is out by the header's length until it counts them in
  `Content`. `SetContent` takes `Content` back unchanged, or the code alone, and keeps the header either way; a header
  that differs is refused. **New `TrySetContent`, `TryApplyEdits` and `TryNavigateTo` say why a call was refused**:
  an unknown or ambiguous name, a header the call would change, or a line the document does not have. The existing
  members keep their signatures and answers, so a packaged add-in keeps running; and the new ones have default
  implementations, so a hand-written `IEditorAccess` keeps compiling. A line outside the document passed to
  `NavigateTo` now moves nothing, where it closed the IDE.
- **The AI Chat panel is told, and tells you, when the IDE refuses an edit.** It is shown the whole file, header
  included, and told the header is read-only. An edit it asks for, or a block you apply, that would change the
  header is refused with the reason in the transcript, where before the edit was reported as done whether it was
  or not. Its privacy notice now says the active file's contents are sent, layout and captions included, and is
  shown once more to anyone who saw the old one, which said "code".

### Fixed

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
