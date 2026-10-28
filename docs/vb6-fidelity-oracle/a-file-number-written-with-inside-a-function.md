## A file number written with # inside a function (2026-10-28)

**The question.** HexIDE gives "Syntax error" for `EOF(#1)` and its neighbours. Is that vb6.exe's answer too?

**The probe.** One Standard EXE per function, each opening a file of `abc` for Input as file 1 and logging the call.

| Expression, with file 1 open For Input | vb6.exe |
|---|---|
| `EOF(#1)` | compile error "Syntax error" |
| `LOF(#1)` | compile error "Syntax error" |
| `Loc(#1)` | compile error "Syntax error" |
| `Seek(#1)` | compile error "Syntax error" |
| `FileAttr(#1, 1)` | compile error "Syntax error" |
| `Input(2, #1)` | [ab] String/8 |

So only `Input` takes a `#` before its file number; `EOF`, `LOF`, `Loc`, `Seek` and `FileAttr` take the number bare, and
a `#` there is vb6.exe's compile error "Syntax error", as HexIDE already gives it.
