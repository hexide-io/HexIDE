## The Mid, Mid$, MidB and MidB$ statements (2026-10-28, #2623)

**The question.** HexIDE runs the `Mid` statement but refuses `Mid$` and `MidB$`, and raises 13 for `MidB`. What do the four
do, and what do they raise?

**The probe.** A Standard EXE `Sub Main` under `On Error Resume Next`. Each line resets `s` to `"abcdef"`, runs the
statement and logs `s`. The `MidB` lines log `Len` and each character's `AscW`, since a split character isn't printable.
`v` is a Variant.

| Statement, `s` starting as `"abcdef"` | vb6.exe |
|---|---|
| `Mid(s, 2, 2) = "XY"` | aXYdef |
| `Mid$(s, 2, 2) = "XY"` | aXYdef |
| `Mid$(s, 2) = "XYZ"` | aXYZef |
| `Mid$(s, 5) = "WXYZ"` | abcdWX |
| `Mid$(s, 2, 5) = "X"` | aXcdef |
| `Mid$(s, 7) = "X"` | abcdef err 5 |
| `Mid$(s, 0) = "X"` | abcdef err 5 |
| `MidB(s, 3, 2) = "X"` | 6: 97 88 99 100 101 102 |
| `MidB$(s, 3) = "XY"` | 6: 97 88 89 100 101 102 |
| `MidB(s, 2, 2) = "X"` | 6: 22625 0 99 100 101 102 |
| `MidB(s, 3, 1) = "XY"` | 6: 97 88 99 100 101 102 |
| `v = "abcdef": Mid$(v, 2, 1) = "Z"` | aZcdef err 0 |
| `v = 12345: Mid$(v, 2, 1) = "9"` | [19345] String/8 err 0 |
| `v = Null: Mid$(v, 1, 1) = "9"` | Null Null/1 err 94 |
| `Mid$(s, 2, 1) = Null` | abcdef err 94 |

So `Mid$` is `Mid`. `MidB` and `MidB$` replace bytes of the string's UTF-16 form, never more than the replacement has
and never past the end, and an odd offset splits a character: `MidB(s, 2, 2) = "X"` puts `X`'s bytes over the high byte
of `a` and the low byte of `b`. A start past the end, or below 1, is 5. Null, as the target's value or as the
replacement, is 94. A Variant holding a number becomes a String.
