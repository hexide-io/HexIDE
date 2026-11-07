## A Const declared with a type character (2026-10-23, #1614)

**The question.** #1611 found that `Const KS! = 2.5D2` is the Single 250 in vb6.exe, where HexIDE stopped the program as
the module loaded, with "Const type hints not supported". What does a type character on a Const's name do: is it the
`As` clause it stands for, with that clause's conversions and errors? And how does it meet a name read with a character,
another declaration of the name, and an `As` clause beside it?

**The probe.** One program, built native and p-code with vb6.exe 6.00.8176 (the release build) and run on the MSVBVM60
6.00.9848 (SP6) runtime, en-GB. It declares eleven Consts at module level, one of them `Public`, and ten of the same in
a procedure, then logs `TypeName` and `CStr` of each through its bare name. Then fifteen one-statement projects, native,
for what might not compile. Native and p-code agreed on every row of the program.

**Result: the character is the As clause.** Each Const converts as `As` that type converts it (#873's rows), at module
level and in a procedure alike:

| Const | vb6.exe |
|---|---|
| `KI% = 2.5`, `KL& = 3.5` | Integer 2, Long 4 (banker's rounding) |
| `KS! = 2.5E2`, `KD# = 1`, `KC@ = 1.23456` | Single 250, Double 1, Currency 1.2346 |
| `KT$ = 5`, `KU$ = "x"`, and in a procedure `K$ = 1.5` | String "5", "x" and "1.5" |
| `KB% = True`, `KN% = "7"`, `KH% = &HFFFF` | Integer -1, 7 and -1 |
| `KE& = KI + 0.5` | Long 2 |
| `Public Const KP% = 7` | Integer 7 |
| `KS!` and `KT$` read with their own characters | Single 250, String "5" |

**Refused, each a compile error:**

| Declaration | vb6.exe |
|---|---|
| `Const K% = 40000`, `Const K@ = 1E20`, `Const K! = 1E39`, and `Const K% = 32768` in a procedure | Overflow |
| `Const K% = "x"` | Type mismatch |
| `Const K% = Null` | Invalid use of Null |
| `Const K% As Integer = 1` | Expected: = |
| `Const K% = 1`, then `K = 2` | Assignment to constant not permitted |
| `Const K% = 1`, then reading `K&` | Type-declaration character does not match declared data type |
| at module level, `Const K = 1` and `Const K% = 2`, `Const K% = 1` and `Const K& = 2`, or `Dim K` and `Const K% = 1` | Ambiguous name detected: K |
| in a procedure, `Const K = 1` and `Const K% = 2` | Duplicate declaration in current scope |

A character does not make another name. `K` and `K%` are one name, so a second declaration of it is a duplicate with or
without a character, and the same with a plain `Const K = 1` twice.

### In HexIDE (#1614)

- **`BaseTypeMapper.Map(TypeHintContext)`** gives the type a character declares, and `StatementExecutor.AsDeclared`
  takes it as the `As` clause, with that clause's conversions and compile errors. The three refusals, in `PrePass` and
  in the module-level and procedure-level Const paths, are gone.
- **A character and an `As` clause together** is "Expected: =".
- **`ConstTypeCharacterTests`** runs the program and must print vb6.exe's lines, and refuses eight of the one-statement
  projects with vb6.exe's message.
- **Divergences kept, each filed:**
  - a name read with a type character, `KS!`, is refused as not implemented (#742), so the program's two such rows are
    left out of the test, and so is reading `K&` for a `K%`;
  - a second declaration of a name at module level ran, where vb6.exe refuses it (#1622), and in a procedure (#1499).
    Both are refused since #1622; `DeclaredTwiceTests` pins these probes' duplicate rows.

