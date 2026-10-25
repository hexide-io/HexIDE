## The library's `$` forms (2026-10-15, #742, #1071)

**The question.** "A type character on a name the program also declares" settled how `Left$` is looked up, and
which library names refuse or ignore a `$`. Two questions remained: which library functions *have* a `$` form, and
what the `$` changes on them. A `Variant` that receives the result reads `String` either way, so the type can't tell
the two forms apart. A Null can.

**The probe.** [The scripted harness](README.md#the-scripted-harness--scriptsvb6-oracleps1), VB6.EXE 6.00.8176 (the original release) in the
oracle guest, one `/make` of 69 expressions under `Option Explicit`. Each function was called three times:
- **the `$` form** with an ordinary argument;
- **the plain form** with a Null;
- **the `$` form** with a Null.

It built first time, so every one of the 23 has a `$` form.

| Function | `F$(ordinary)` | `F(Null)` | `F$(Null)` |
|---|---|---|---|
| `Chr`, `ChrB`, `ChrW` | `Chr$(65)`: `A` | ERR 94 | ERR 94 |
| `Format` | `Format$(1.5)`: `1.5` | `""`, String | `""`, String |
| `Hex`, `Oct` | `Hex$(255)`: `FF`, `Oct$(8)`: `10` | Null | ERR 94 |
| `LCase`, `UCase` | `LCase$("A")`: `a` | Null | ERR 94 |
| `Left`, `LeftB`, `Right`, `RightB` | `Left$("abc", 1)`: `a`, `LeftB$("abc", 2)`: `a` | Null | ERR 94 |
| `Mid`, `MidB` | `Mid$("abc", 2)`: `bc`, `MidB$("abc", 3)`: `bc` | Null | ERR 94 |
| `LTrim`, `RTrim`, `Trim` | `Trim$(" a ")`: `a` | Null | ERR 94 |
| `Str` | `Str$(5)`: ` 5` | Null | ERR 94 |
| `String` | `String$(3, "x")`: `xxx` | `String(3, Null)`: Null | ERR 94 |
| `Space` | `Space$(2)`: two spaces | ERR 94 | ERR 94 |
| `Environ` | `Environ$("OS")`: `Windows_NT` | ERR 94 | ERR 94 |
| `Error` | `Error$(11)`: `Division by zero` | ERR 94 | ERR 94 |
| `CurDir` | `CurDir$("C")`: the current folder | ERR 13 | ERR 13 |

`ChrB$(65)` came back as one byte the harness's text file couldn't render, which is what a single-byte string
there should do.

**The rule, with no exception in these rows.** `F$(...)` answers as `F(...)` does, with the result made a String:
- **Where the plain form passes a Null through,** the `$` form raises 94. That is CStr's error on a Null, and it is
  the only difference the rows show.
- **Where the plain form already raises on a Null** (`Chr`, `Space`, `Environ`, `Error`, `CurDir`), the `$` form
  raises the same error.
- **Where the plain form turns a Null into a String** (`Format`), the `$` form does the same.

Not probed: `Command$`, `Date$`, `Time$`, `Dir$`, `Input$` and `InputB$`. HexIDE refuses those, along with any other
`$` on a library name these rows don't cover, naming #742.

**In HexIDE (#1071).** A call written with a `$` is looked up without it, as the earlier entry measured:
- **a user Function** answers it if it is declared `As String`, and any other type is the compile error (TU_01,
  TV_01);
- **on the library,** the 23 here answer as the rule says, the six of the earlier entry are its compile error, and the
  nine it ignores answer as their plain form;
- **any other `$`** stops the run naming #742: on an unmeasured library name, an array, a control array or a member of
  the form.

