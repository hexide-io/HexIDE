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

### Fixed

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
