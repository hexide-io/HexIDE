## An Error value in Select Case, against every other kind (2026-10-28, #2520)

**The question.** "CVErr and the Error subtype" measured `Select Case e` against `Case 5`, `Case CVErr(5)` and
`Case Is > CVErr(4)`. What about a Case value of every other kind, and the other way round, a selector of another kind
against an Error Case value? Found reviewing #2604.

**The probe.** Two Standard EXEs. The first runs `Select Case e` against a String, Null, Empty, an `Is` and a `To` Case,
then compares `e` with a String. The second runs selectors of seven kinds against `Case e`, `Case Is < e` and
`Case e To e`. Each Case logs whether it ran and `Err.Number`.

| Probe line, `e = CVErr(5)`, under `On Error Resume Next` | vb6.exe |
|---|---|
| case string 5 | took the Case, err 13 |
| case string abc | took the Case, err 13 |
| case Null | took the Case, err 13 |
| case Empty | took the Case, err 13 |
| case Is > 4 | took the Case, err 13 |
| case 1 To 9 | took the Case, err 13 |
| e = "5" | [unset] String/8 err 13 |
| e = "abc" | [unset] String/8 err 13 |
| e < "5" | [unset] String/8 err 13 |
| selector a Variant holding 5, Case e | took the Case, err 13 |
| selector a Variant holding 5, Case Is < e | took the Case, err 13 |
| selector a Variant holding 5, Case e To e | took the Case, err 13 |
| selector a Long 5, Case e | took the Case, err 13 |
| selector a Long 5, Case Is < e | took the Case, err 13 |
| selector a Long 5, Case e To e | took the Case, err 13 |
| selector a String "x", Case e | took the Case, err 13 |
| selector a String "x", Case Is < e | took the Case, err 13 |
| selector a String "x", Case e To e | took the Case, err 13 |
| selector a Variant holding "5", Case e | took the Case, err 13 |
| selector a Variant holding "5", Case Is < e | took the Case, err 13 |
| selector a Variant holding "5", Case e To e | took the Case, err 13 |
| selector Null, Case e | took the Case, err 13 |
| selector Null, Case Is < e | took the Case, err 13 |
| selector Null, Case e To e | took the Case, err 13 |
| selector Empty, Case e | took the Case, err 13 |
| selector Empty, Case Is < e | took the Case, err 13 |
| selector Empty, Case e To e | took the Case, err 13 |
| selector a Variant holding CVErr(5), Case e | took the Case, err 0 |
| selector a Variant holding CVErr(5), Case Is < e | took Case Else, err 0 |
| selector a Variant holding CVErr(5), Case e To e | took the Case, err 0 |

So an Error value against a value of any other type is 13, on either side of the Case, and under `Resume Next` the
Case's body runs, as it does for `If`. Two Error values compare by their number.
