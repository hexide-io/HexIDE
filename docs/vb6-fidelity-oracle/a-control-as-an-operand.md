## A control as an operand (2026-11-23, #5504, SP6)

**The question.** "A control where a value is wanted is its default member" measured conversions, `Print`, `Write #`
and `&`. What does VB6 do with a control on either side of an arithmetic, comparison or logical operator? #5504 saw
HexIDE raise 13 for `Check1 + 0`, where VB6 wrote 0.

**The probe.** One form with 13 intrinsic controls, set in `Form_Load`:

| Control | Default member | Its value |
|---|---|---|
| `Text1` | Text | `"12"` |
| `Text2` | Text | `"T1"` |
| `Label1` | Caption | `"3"` |
| `Frame1` | Caption | `"4"` |
| `Command1` | Value | False |
| `Check1` | Value | 1 |
| `Option1` | Value | True |
| `List1` | Text | `"5"` (items `"5"`, `"6"`, ListIndex 0) |
| `Combo1` | Text | `"7"` |
| `HScroll1` | Value | -5 (Min -10, Max 10) |
| `VScroll1` | Value | 8 |
| `Shape1` | Shape | 3 |
| `Picture1` | Picture | empty |

For each control `C`, it ran:
- `C op 2` and `2 op C` for `+`, `-`, `*`, `/`, `\`, `Mod`, `^`, `=`, `<>`, `<`, `>`, `<=`, `>=`, `And` and `Or`;
- `Not C`, `-C` and `C + "1"`;
- the same through `Dim o As Object`: `o + 2`, `2 * o`, `Not o` and `o = 2`;
- three pairs: `Text1 + Label1`, `Check1 + HScroll1` and `Text1 + Check1`.

Each result was assigned to a Variant under `On Error Resume Next`, and its `TypeName`, value and `Err.Number` were
logged: 497 rows.

**A second program, the discriminating one,** ran the same 497 expressions with the default member written out:
`Text1.Text + 2`, `Check1.Value * 2`, `o.Caption = 2` and so on.

**Setup:** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle guest, Windows 10 build 19044.1288, with MSVBVM60 6.00.9848.
Both programs were built with `/make` as native code and as p-code, and each exe ran in session 0.

**The result: a control as an operand is its default member, read as the member's own type.**
- **All 497 rows match the second program's,** in native code and in p-code, early-bound and through `As Object`
  alike. Every operator, both sides, every control.
- **No control raised an error that its member doesn't.**
  - **Text2's `"T1"`** raised 13 with every operator, `Not` and `-` included, because the String isn't a number. But
    `Text2 + "1"` is `"T11"`.
  - **`2 / C`, `2 \ C` and `2 Mod C`** raised 11 for Command1 (False) and Picture1 (0).
- **The type follows the member's,** as Variant arithmetic on that type would give it. Some examples:

| Expression | Result |
|---|---|
| `Text1 + 2` | Double 14 |
| `Text1 + "1"` | String `"121"` |
| `Text1 + Label1` | String `"123"` |
| `Text1 + Check1` | Double 13 |
| `Command1 + 2` | Integer 2 |
| `Not Command1` | Boolean True |
| `Check1 + 2` | Integer 3 |
| `Not Check1` | Integer -2 |
| `Option1 + 2` | Integer 1 |
| `HScroll1 + 2` | Integer -3 |
| `Check1 + HScroll1` | Integer -4 |
| `Shape1 + 2` | Integer 5 |
| `Picture1 + 2` | Long 2 |
| `Not Picture1` | Long -1 |
| `Text1 \ 2`, `Text1 And 2` | Long 6, Long 0 |
| `Not Text1` | Long -13 |

**One row differs between early-bound and late-bound, and the member written out differs the same way.**

| Expression | Early-bound (`Text2 = 2`, `Text2.Text = 2`) | Through `As Object` (`o = 2`, `o.Text = 2`) |
|---|---|---|
| `"T1"` compared with 2 | raises **13** | **False**, no error |

The obvious guess was that the two paths agree, since both read the same Text. They don't:
- **early-bound,** the default is a typed String, and comparing it with a number converts it to a number;
- **late-bound,** it arrives as a Variant holding a String, and a Variant String compared with a number is unequal,
  with no conversion.

No other control's four late-bound rows differed from its early-bound ones.

### Native code's `Integer Mod 2` of a negative (2026-11-23, #5504, SP6)

**Found by the probe above:** native code and p-code disagreed on exactly two of the 497 rows.

| Expression | Native code | p-code |
|---|---|---|
| `Option1 Mod 2` (True) | Integer 1 | Integer -1 |
| `HScroll1 Mod 2` (-5) | Integer 1 | Integer -1 |

The second program, with the members written out, gave the same split. It added five rows of its own:

| Expression | Native code | p-code |
|---|---|---|
| `i Mod 2`, `Dim i As Integer`, `i = -5` | Integer **1** | Integer -1 |
| `b Mod 2`, `Dim b As Boolean`, `b = True` | Integer **1** | Integer -1 |
| `lg Mod 2`, `Dim lg As Long`, `lg = -5` | Long -1 | Long -1 |
| `-5 Mod 2`, a literal | Integer -1 | Integer -1 |
| `CVar(i) Mod 2` | Integer -1 | Integer -1 |

- **So it's native code's, and the control has nothing to do with it.** An Integer or Boolean variable `Mod 2` with a
  negative left operand comes out positive in native code. p-code keeps the dividend's sign, as `Mod` does
  everywhere else ("Operator precedence" records `-7 Mod 2` as -1).
- **HexIDE matches p-code** where the two differ (CLAUDE.md, "Fidelity Principle"), so -1 is the answer to build to.
- **Not measured:**
  - a divisor other than 2;
  - Byte;
  - a negative divisor;
  - which native-code optimisation switches change it. The project used `/make`'s defaults.

**In HexIDE (#5504), as lane C's run for #5385 saw it, on main `cf602a678`:** `Check1 + 0` raises 13 at
`VbNumeric.Arith`. The other operators weren't run there. HexIDE's `Integer Mod 2` of a negative wasn't checked.

**Not measured:**
- `Like`, `Is`, `Imp`, `Eqv` and `Xor`;
- a control on both sides of a comparison;
- a ListBox with no selection;
- a TextBox holding text that is only partly a number;
- controls other than these 13.
