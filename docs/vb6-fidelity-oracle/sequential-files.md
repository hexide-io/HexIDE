## Sequential files (2026-10-17, #1145)

**The probes.** Found by the Notepad demo (`demo/notepad-forms`), which reads and writes its documents with `Open`,
`Line Input #`, `Print #`, `Close`, `FreeFile` and `EOF`. Two probes, each a form whose `Form_Load` logs to a file on
number 100 under `On Error Resume Next`, then `End`s. Each probe was built native and p-code, and **all four runs
agreed**. The input files were written byte for byte with `Put` on a Binary file, and what `Print #` wrote was read
back the same way and logged as hex. The guest is en-GB, code page 1252.

**Where `Line Input #` ends a line:**

| The file's bytes | Lines read before `EOF` |
|---|---|
| `a CR LF b CR LF` | `a`, `b`: a final line break makes no empty last line |
| `a CR LF b` | `a`, `b` |
| `a CR LF CR LF` | `a`, `""` |
| `a CR b CR c` | `a`, `b`, `c`: a lone CR ends a line |
| `a LF b LF c` | one line, `a LF b LF c`: a lone LF does **not** end one |
| `a LF CR b` | `a LF`, `b` |
| `a CR LF b ^Z c CR LF d` | `a`, `b`: **Ctrl+Z (26) ends the file** |
| no bytes | none: `EOF` is True at once |
| `caf E9 CR LF` | `café`: the ANSI code page |
| one line of 70,000 characters | all 70,000 |

After the last line `EOF` is True, and one more `Line Input #` is **62**, "Input past end of file", with the variable
keeping the value it had. A Variant read into holds a String.

**What `Print #` writes:**

| Statement | Bytes |
|---|---|
| `Print #f, "a"` | `a CR LF` |
| `Print #f, "a";` then `Print #f, "b"` | `ab CR LF` |
| `Print #f, "a", "b"` | `a`, 13 spaces, `b CR LF` |
| `Print #f, 5` / `-5` / `1.5` | ` 5 ` / `-5 ` / ` 1.5 `, each then `CR LF` |
| `Print #f, "x"; 5; "y"` | `x 5 y CR LF` |
| `Print #f, True` | `True CR LF`: no spaces |
| `Print #f,` | `CR LF` |
| `Print #f, "a",` then `Print #f, "c"` | `a`, 13 spaces, `c CR LF` |
| `Print #f, "a";` then `Print #f, ; "z"` | `az CR LF` |
| `Print #f, Null` (in a Variant) | `Null CR LF` |
| `Print #f, "e"; Empty; "e"` | `ee CR LF` |
| `Print #f, #1/2/2003#` | `02/01/2003 CR LF`: the date as it reads, then a space |
| `Print #f, ChrW$(&H416)` | `? CR LF`: code page 1252 has no byte for it, and no best fit either (Ā is `A`: see "Print # and the ANSI best fit", #1511) |
| `Print #f, CCur(2.5), CSng(0.1), CDbl(1E+20)` | ` 2.5 `, ` 0.1 ` and `1E+20 `, each starting a zone |
| `"12345678901234567890", "x"` | 8 spaces before `x`: column 20 moves to 28 |
| `"12345678901234", "x"` | 14 spaces: a zone boundary already reached still moves on, to 28 |
| `"1234567890123", "x"` | 1 space |
| `"ab" & vbCrLf & "c", "x"` | 13 spaces after `c`: the column restarts at a line break inside an item |
| `"ab";` then `"cd", "x"` | 10 spaces after `abcd`: the column carries across statements |

So a number has a space before it where its sign would go and one after, and a date has one after. A string, a
Boolean and Null have none. A comma moves to the next 14-column zone, and a statement ending in `;` or `,` writes no
line break.

**`Debug.Print` is the same Print** (2026-10-22, #1380). A compiled program drops `Debug.Print`, so it was measured in the
IDE. In the guest's console session VB6 opened a project whose Sub Main prints 25 cases, each after a marker line, and
ran it with F5. Ctrl+G, Ctrl+Home, Ctrl+Shift+End and Ctrl+C then copied the Immediate window to the clipboard. The
first attempt copied nothing; with screenshots between the keys, the second copied all of it. The same 25 cases went
through `Print #` to a file in a compiled probe, native and p-code. **All three agree line for line**, apart from the
Immediate window's own two empty lines at its end.

| Statement | Line, between brackets |
|---|---|
| `1; "a"; 2` | `[ 1 a 2 ]` |
| `1, 2` | `[ 1             2 ]` |
| `"x";` then `"y"` | `[xy]` |
| `"p",` then `"q"` | `[p             q]` |
| `-1; -2.5; 3` | `[-1 -2.5  3 ]` |
| `True; False` | `[TrueFalse]` |
| `#1/2/2003#; "d"` | `[02/01/2003 d]` |
| `Null; Empty; "e"` | `[Nulle]` |
| `"a", "b", "c"` | `[a             b             c]` |
| `"123456789012345", "z"` | `[123456789012345             z]`: column 16 moves to 29 |
| `CCur(1.5); CSng(0.1); CDbl(1E+20)` | `[ 1.5  0.1  1E+20 ]` |
| `CByte(5); CLng(-7); CInt(0)` | `[ 5 -7  0 ]` |
| `1.5E-10` | **`[ 0.00000000015 ]`** |
| nothing | `[]` |
| `, "lead"` | `[              lead]`: a leading comma moves to column 15 |
| `"a",` then `,` then `"b"` | `[a                           b]`: two zones, to column 29 |
| `1;` then `2` | `[ 1  2 ]` |
| `Spc(3); "x"; Tab(10); "y"` | `[   x     y]`: `x` at column 4, `y` at 10 |
| `Tab(5); "x"` | `[    x]` |
| `"a"; Tab; "b"` | `[a             b]`: `Tab` alone is a comma |
| `"a"; Spc(0); "b"` | `[ab]` |
| `"abc"; Tab(2); "d"` | `[abc]`, then `[ d]`: a column already passed **starts a new line** |
| `"x"; Tab(0); "y"` | `[x]`, then `[y]`: `Tab(0)` is column 1, passed too |

What these add to the table above:
- **`Debug.Print` formats as `Print #` does,** zones, spacing and an open line included.
- **`Tab(n)` moves to column n**, and to n on the next line when the line is already past it. `Spc(n)` writes n spaces.
- **A line a run leaves open stays open for the next run, column and all.** A second probe ran one Sub Main twice
  with F5: `Debug.Print "abc"; Tab(10); "x";`. The Immediate window then held `abc      xabc` and `         x`. The
  second run's `abc` continued the first run's line, and its `Tab(10)` found the column at 13, past 10, so it went to
  the next line. A column restarted for each run would have given `abc      xabc      x`. The copy read
  `         xg`: the copy step's own Ctrl+G typed a `g` at the caret, which a screenshot taken before it does not show.
  That probe's first attempt also copied nothing. It ran again with 15 seconds for VB6 to open, and the Immediate
  window opened from the View menu before F5.

**Separators with no item** (2026-10-22, #1409). A third IDE probe ran with F5 and was copied the same way. Each case
below is one or more `Debug.Print` statements, then the line they print:

| Statements | Line |
|---|---|
| `"a";`, then `;`, then `"b"` | `ab`: `;` alone writes nothing, and the line stays open |
| `;`, then `"x"` | `x`: nor does it end a line, even at the start of one |
| `, ,`, then `"y"` | `y` at column 29: one zone for each comma |
| `"a";`, then `;;`, then `"b"` | `ab` |
| `,;`, then `"z"` / `;,`, then `"w"` | `z` / `w` at column 15 |
| `"abc";`, then `,`, then `"v"` | `v` at column 15 |
| `;"u"` | `u` |
| `; "t";`, then `"--10"` | `t--10`: the line stays open |

So a statement of separators alone is an ordinary output list, as `Print #` reads one. A procedure's call is another
matter. In a `/make` probe with `Sub Foo(Optional a, Optional b, Optional c)`, `Foo ,` is the compile error "Syntax
error", native and p-code, and so are `Foo , ,` and `Foo ;`, built as p-code. `Foo , 2` builds and runs, with `a` and
`c` missing. A list of
separators alone belongs to Print only.
- **`1.5E-10`, a Double, prints as `0.00000000015`,** most likely just its text. *Turning a number or a date into
  text* keeps a Double fixed while its digits fit, as `0.000012345` is, and goes to E notation for `0.00001 / 3`.
  HexIDE's `CStr` gives `0.00000000015` too, but `CStr` of this value was not measured on vb6.exe.
- **`CDbl(1E+20)` has its sign space,** ` 1E+20 `. The row above gives it as `1E+20 `, starting its zone, but its own
  hex dump, pinned in `SequentialFileTests`, has ten spaces after ` 0.1 `: nine to reach column 29, then the sign. So the
  two are one result, read two ways.

**Opening, numbering and closing:**

| Probe | Result |
|---|---|
| `FreeFile`, `FreeFile(0)`, `FreeFile(1)`, `FreeFile(2)` with nothing open | 1, 1, 256, 256, with no error |
| `FreeFile` read twice before an `Open` | the same number both times |
| `FreeFile` with 1 and 2 open | 3 |
| `Open` For Output on an existing file | truncated: `LOF` 0 |
| `Open` For Append | adds at the end, and creates a missing file |
| a missing file For Input | 53, "File not found", and the number stays free |
| a missing directory, For Input or Output | 76, "Path not found" |
| a directory as the path | 75, "Path/File access error" |
| `""` as the path | 75 |
| a number already open | 55, "File already open" |
| a second Output, Append or Input of a file open for Output | 55 |
| a second Output or Append of a file open for Input | 55 |
| a second Input of a file open for Input | taken |
| a second Input of one opened `Lock Write` or `Lock Read` | 55 |
| `Open ... As #0` | 52, "Bad file name or number" |
| `Open ... As #512` (a literal) | compile error, "Syntax error" |
| `Open ... As #g` with `g = 512` | taken |
| `As 3` and `Close 3`, with no `#` | taken, and `Print #3` writes to it |
| `Access Read` with Input; `Shared` | taken |
| `For Input Access Write` | compile error, "Syntax error" |
| `Print #` on an Input file; `Line Input #` on an Output file | 54, "Bad file mode" |
| `EOF` of an Output file | True, with no error |
| `Line Input #`, `Print #` or `EOF` on a number not open | 52 |
| `Close` on a number not open, twice, or `#0` | no error |
| `Close #g` with `g = -1` | 52 |
| `Close` with no number | closes every open file, the log's included |
| a relative path, after `ChDir App.Path` | resolved against the current directory |

**Overturned while measuring.** Our first reading was that `As 3` without the `#` was refused, with 55. It was not:
`a1.txt` was still open on number 512 from the line before, so the 55 was the second Output of one file. The second
probe used a fresh file, and `As 3` was taken.

**In HexIDE (#1145):**
- **All of the above runs**, apart from the two compile errors, which are not raised.
- **Not implemented:**
  - Random and Binary mode, `Get`, `Put`, `Input #`, `Write #`, `Input$`, `LOF`, `Loc` and `Seek`. `Input #` and
    `Write #` run since #1521, and `Input$`, `LOF`, `Loc` and `Seek` since #1645 (see "LOF, Loc, Seek and the Input
    function"), and Random and Binary mode, `Get` and `Put` since #1646 (see "Random and Binary files, Get, Put and
    Len");
  - `Tab` and `Spc` in `Print #`;
  - `Line Input #` into anything but a plain variable.
- **The divergences:**
  - an Input file is read whole when it opens, so a change another program makes while it is open is not seen;
  - a `Lock` keeps the file only from this program's own second open, not from another process.
- **Chosen, not measured: where a relative path goes.** Against the project's folder, `App.Path`, until the program
  runs `ChDir`, and against that directory after it. VB6 resolves one against `CurDir`, which the row above measured,
  and `CurDir` is the project's folder when the project was opened from Explorer, the usual case. The IDE's own working
  directory is never used: it is wherever HexIDE was started, and a file written there is one the developer never
  chose. An unsaved project has no folder, so a relative path in one is refused as not implemented. What `CurDir` is
  in the VB6 IDE when the project was opened some other way is not measured.
- **Still a parse failure:** a line holding two `#` file numbers. That is the date-literal token, and it has an issue
  of its own.

### `Print #` items with nothing between them, and a sign after `Spc` or `Tab` (2026-11-06, #3248)

**The probes.** The forms are rdcore's. Each project held one statement under test, after `On Error Resume Next`, with:
- `ff` a file open For Output;
- `F` a `Function F(n)` returning `n * 10`;
- `x` holding `"x"`.

Each was built with `/make` as native code and as p-code, and **the two agreed on every form**. Every exe that built ran
with `Err.Number` 0, and its file was read back as hex. Every line it wrote ended `CR LF`. The `Debug.Print` rows were
checked for compile acceptance only, since a built exe drops `Debug.Print`.

**A sign or an operator straight after a `Spc` or `Tab` clause:**

| Statement | vb6.exe |
|---|---|
| `Print #ff, Spc(2) - 1`, and `Spc(2)-1` | `[  -1 ]` |
| `Print #ff, Tab - 1`, and `Tab-1` | 14 spaces, then `-1 `: `-1` starts at column 15 |
| `Print #ff, Tab(2) + 1` | `[  1 ]` |
| `Print #ff, Spc(2) & "x"`, and `Spc(2)&"x"` | **Syntax error** |
| `Print #ff, Tab(2) & "x"`, and `Tab & "x"` | **Syntax error** |
| `Print #ff, Tab(2) = 5`, `Spc(2) = 5` and `Tab = 5` | **Syntax error** |
| `Print #ff, x; Tab(2) = 5` | **Syntax error** |
| `Debug.Print Tab(2) & "x"` | **Syntax error** |
| control: `Print #ff, Spc(2); "x"` | `[  x]` |
| control: `Print #ff, Tab(2); "x"` | `[ x]` |

**Items with nothing between them:**

| Statement | vb6.exe |
|---|---|
| `Print #ff, 1(2)`, and `1 (2)` | `[ 1  2 ]` |
| `Print #ff, 1(2)(3)` | `[ 1  2  3 ]` |
| `Print #ff, "a"x` | `[ax]` |
| `Print #ff, Spc(1)Tab(4)"b"` | `[   b]`: `b` at column 4 |
| `Write #ff, 1 2` | `[1,2]` |
| `Print #ff, F (2)`, and `F(2)` | `[ 20 ]` |
| `Debug.Print 1(2)` | builds |

What these add:
- **An item needs no separator before it.** With nothing between two items, `Print #` prints them as `;` would, and
  `Write #` writes its comma between them, as it does for `;`.
- **A name followed by a parenthesised expression is still a call,** with or without the space. A number followed by
  one is two items.
- **A `Spc` or `Tab` clause ends at its closing parenthesis, or at the word itself for a bare `Tab`.** Whatever
  follows starts the next item.
  - So the `-` in `Spc(2) - 1` is a sign, not a subtraction.
  - `&` and `=` can't start an item, so either one there is a Syntax error. That holds with no spaces, and after an
    earlier item and `;`.
- **A bare `Tab` before an item moves to the next zone,** as `"a"; Tab; "b"` showed above. Here it went from column 1
  to column 15.

**Overturned.** The obvious readings are that `Spc(2) - 1` subtracts, and that two items with nothing between them
are a syntax error. HexIDE's grammars make both readings, and vb6.exe makes neither.

**Not measured:**
- how the VB6 IDE relists these lines, and whether `/run` agrees: only `/make` was run;
- other binary operators straight after a clause, such as `*`, `Mod` or `Like`;
- a form's or a PictureBox's `Print` method.

**In HexIDE,** at main ed0ad0387. #3248 has the table, form by form.
- **Items with nothing between them:** the interpreter refuses each program at load. The language server reports a
  syntax error.
- **A sign after a clause:** the interpreter stops when the line runs, with "Sub or Function not defined" for
  `Spc(n)` and `Tab(n)`, and a not-implemented exception for a bare `Tab`. The server reports nothing, which is right
  for these.
- **`&` or `=` after a clause:** the server reports nothing where vb6.exe gives a Syntax error. The interpreter fails
  only when the line runs, with the same errors as for a sign.
