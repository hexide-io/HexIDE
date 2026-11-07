## File-number hashes and date literals (2026-10-17, #1146)

**Method.** Independent statement snippets compiled with `scripts/vb6-legality.ps1` in the oracle guest, first
native (the harness default), then with `CompilationType=-1` added to its generated project. Both agreed on every
result below. These are compile results, not a claim that every accepted date spelling already executes in HexIDE.

**File statements accepted:**

```vb
Dim s As String: Line Input #1, s: Print #2, s
Print #1, "a";: Close #1
Close #1, #2, #3
Print #1, #1/2/2003#: Close #1
Print #1, 2, #1/2/2003#
Dim f As Integer: f = 1: Print #f, 2#: Close #f
Open "x" For Output As #1: Print #1, "a": Close #1
Debug.Print Input$(1, #1): Close #1
Print #1, _
2#: Close #1
```

Each is a separate probe (the final two physical lines are one probe). No file need exist to ask the compiler.

**The ambiguity that must remain.** `Debug.Print #1, 2#` compiles. `Print #1, 2#` in a standard module is refused
with "Method not valid without suitable object": VB6 sees the date, so this is the form's Print method, not file
1 followed by a Double. `Dim May As Integer: Print #May, 2#` is refused the same way; `Dim f As Integer: Print #f, 2#`
compiles. A lexer fix that always treats a hash after Print as a file number would therefore change VB6's meaning.

**Date spellings accepted after `Debug.Print`:**

- `#1/2/2003#`, `#1-2-2003#`, `# 1/2/2003 #`, `#2/29#`, `#10.30#`, `#1, 2#`.
- `#January 2, 2003#`, `#2 Sept 2003#`, `#Jan. 2, 2003#`, `#2 Jan. 2003#`, `#2 Jan.#`.
- `#Tue, Jan 2, 2001#`, `#Tues, Jan 2, 2001#`, `#Thurs, Jan 4, 2001#`.
- `#10:30:00 AM#`, `#10:30AM#`, `#10 PM#`, `#10:30 A#`, `#10:30 P#`, `#1/2/2003 10:30 PM#`.

A follow-up, again native and p-code, accepted `#Thur, Jan 4, 2001#`, `#Weds, Jan 3, 2001#`, and
`#2 Janu 2003#` / `#2 Februar 2003#`. Month-name probes in the form `Debug.Print #2 <name> 2003#` accepted
`Jan`, `Janu`, `Janua`, `Januar`, `Febr`, `Febru`, `Februa`, `Februar`, `Mar`, `Marc`, `Apri`, `Augu`, `Augus`,
`Septem`, `Octob`, `Novem`, `Decem`, `January` and `September`. They refused the shorter `J`, `Ja`, `F`, `Fe`,
`M`, `Ma`, `Ap`, `Au`, `S`, `Se`, `O`, `Oc`, `N`, `No`, `D`, `De`. The token rule therefore retains name
prefixes from three letters onward, not just the usual three-letter abbreviations and full spellings.

**Refused with "Syntax error":** `#1#`, `#1, #`, `#1, 2, #`, `#January#`, `#1Jan2000#`, `#10:30 a.m.#`,
and `#10:30 p.m.#`, each following `Debug.Print`. In particular a comma before the closing hash is not a date:
`Print #1, 2, #1/2/2003#` must retain its file number and its real date, not start with a spurious `#1, 2, #` token.

**Execution, native and p-code.** A `CopyFiles() As String` helper supplied to `scripts/vb6-oracle.ps1` used file
numbers 10 and 11, leaving the harness's output file alone. Its body, inside `On Error GoTo Failed`, was:

```vb
Dim s As String, result As String
Open App.Path & "\source.txt" For Output As #10
Print #10, "alpha": Print #10, "beta": Close #10
Open App.Path & "\source.txt" For Input As #10: Open App.Path & "\copy.txt" For Output As #11
Do While Not EOF(10)
    Line Input #10, s: Print #11, s
Loop
Print #11, "tail";: Close #10, #11
Open App.Path & "\copy.txt" For Input As #10
result = Input$(LOF(10), #10): Close #10
CopyFiles = Replace(Replace(result, vbCr, "<CR>"), vbLf, "<LF>")
```

The error label returned `"ERR" & Err.Number`; neither build reached it. Both returned
`alpha<CR><LF>beta<CR><LF>tail`, TypeName `String`. `SequentialFileTests` checks those bytes after the same copy loop.

**In HexIDE:** both grammars now delimit a date using date/time components, separators and an optional meridiem.
This fixes the file-number collision without adding statement-context rules or claiming complete calendar validation.
`FileNumberGrammarTests` in each test project guards the file tokens and preserved date tokens.

