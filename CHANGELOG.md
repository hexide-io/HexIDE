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

### Fixed

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
  The parameter's slot recorded no type, so each held whatever was stored into it. For the numeric types only; a String,
  Boolean or Date parameter is still not converted (#874).
- **`Case Is` and `Case x To y` convert their values into a typed selector's type** (#839), as an equality Case does.
  Against a declared 5, `Case Is > "abc"` and `Case "a" To "z"` raise Type mismatch, where they matched nothing; against
  a declared 2, `Case Is < 2.4` does not match, because 2.4 becomes 2.
- **A Variant selector compares as VB6 compares Variants** (#839), in every kind of Case, the equality one included.
  It converts nothing: a Variant 2 does not match `Case 1.7`, and a Variant 5 is `Case Is < 5.4` and is simply not above
  `"a"`, with no error. Against a String it is compared as text, against a number as a number, a string that is not one
  being above it, and against a Variant any number is below any string. A Variant 2 matched `Case 1.7`, and a Variant
  string against a number matched nothing.
- **A Boolean `Case` orders True below False** (#839), as -1 and 0, so a True selector is `Case Is < False`.
- **A typed Function's result, a conversion function's, and a typed array element or Type field are typed, and a
  Variant one is not** (#839), as VB6 types them. `F() * 7000`, with `F` declared `As Integer` returning 5, and
  `CInt(v) * 7000` now raise Overflow, where they widened; a Variant array element or Type field holding 5 now widens,
  where it overflowed. A String, Boolean or Date parameter given a value of its type is typed too.

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
  A `v As Variant` parameter is not refused yet, and its ReDim of a caller's dynamic array still leaves the caller's
  array as it was, where VB6 resizes it (#970).

- **A module-level `Const` is checked as VB6 checks it** (#760). Its value may read only other constants: a Const
  declared below it or the Const itself, a variable, a user function and any function call are VB6's "Constant
  expression required", where they ran from whatever HexIDE made of them. Another module's Public Const is read
  qualified once that module's declarations have run, and refused unqualified (#880) or before (#893), rather than
  read as Empty. A module naming itself is "Circular dependencies between modules".
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
  "Range has no values", in a module, a class or a procedure, where it built an empty array or raised 9. Another module's Public Const unqualified (#880), or qualified before that module has run
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
