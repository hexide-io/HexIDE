## Width # (2026-10-28, #2554)

Measured on vb6.exe SP6, every probe built native and as p-code, which logged the same lines. Each probe is a Standard EXE `Sub Main` under `On Error Resume Next`, logging each statement with `Err.Number`; `Show(v)` logs a value as `[value] TypeName/VarType`.

**What the rows say:**
- The width is converted as CInt converts: 3.6 is 4, 3.4 is 3, **4.5 is 4 and 5.5 is 6**. A numeric String is taken. 0 means no limit. 0 to 255 are taken; **256 and -1 are 5.** A file number not open is 52. A Binary or Input file takes it without error (its effect there is unmeasured).
- **`Print #` breaks the line before an item that would pass the width**, so at 5, `"ab"; "cd"; "ef"` writes `abcd` and then `ef`.
- **An item longer than the width is written whole**, on a line of its own if anything is before it: `"abcdefghijkl"` stays one line, and `"abcdefg"; "h"` is `abcdefg`, then `h`.
- A number's leading and trailing spaces count: `1; 2; 3` at 5 writes ` 1 `, ` 2 ` and ` 3 ` on three lines.
- A comma's move to the next print zone, past the width, breaks the line: `"abc", "de"` is `abc`, then `de`. `"a"; Tab(4); "b"` stays within it.
- A trailing `;` carries the column into the next `Print #`: `"abc";` then `"de"` is `abcde`.
- **`Write #` is not wrapped:** `Write #1, "abcdefghij", 123` is one line at width 5.

In the file dumps, `<CRLF>` stands for the line break.

```
Width #1, 5 err 0
Width #1, 0 err 0
Width #1, 255 err 0
Width #1, 256 err 5
Width #1, -1 err 5
Width #1, 3.6 err 0
Width #1, a String 4 err 0
Width #9, 5, not open err 52
file err 0 [abcdefghijkl<CRLF>ab<CRLF>cdefg<CRLF> 12345678 <CRLF>abc<CRLF>de<CRLF>"abcdefghij",123<CRLF>abcdefghijkl<CRLF>abcdefgh<CRLF>abcdefgh<CRLF>]
Width #2, 5 on a Binary file err 0
Width #3, 5 on an Input file err 0
width: done
```

```
file err 0 [abcd<CRLF>ef<CRLF> 1 <CRLF> 2 <CRLF> 3 <CRLF>abcde<CRLF>abcde<CRLF>f<CRLF>a  b<CRLF>abcdefg<CRLF>h<CRLF>abcd<CRLF>abc<CRLF>ab<CRLF>cde<CRLF>abcdef<CRLF>]
width2: done
```

The second probe, whose file is the dump above:

```vb
Sub Main()
    Dim v As Variant, b As Byte, s As String, f As String, f2 As String
    On Error Resume Next
    f = App.Path & "\w.txt"
    Open f For Output As #1
    Width #1, 5
    Print #1, "ab"; "cd"; "ef"
    Print #1, 1; 2; 3
    Print #1, "abc";
    Print #1, "de"
    Print #1, "abcde"; "f"
    Print #1, "a"; Tab(4); "b"
    Print #1, "abcdefg"; "h"
    Width #1, 3.6
    Print #1, "ab"; "cd"
    Width #1, 3.4
    Print #1, "ab"; "c"
    Width #1, 4.5
    Print #1, "ab"; "cde"
    Width #1, 5.5
    Print #1, "abc"; "def"
    Close #1
    Err.Clear: s = Dump(f): L "file err " & Err.Number & " [" & s & "]"
    L "width2: done"
End Sub
```
