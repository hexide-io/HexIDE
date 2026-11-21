## Break When Value Is True, for each kind of value (2026-11-21, #869, IDE, SP6)

**The question.** A watch set to *Break When Value Is True* decides "true" in HexIDE by its own rule: a Boolean, or a
number that isn't 0. So a String watch never breaks (#869). The issue proposed the rule a condition uses, as `CBool`
reads it, with a value that can't be converted treated as not holding. Which values does VB6's own watch break on? Does
it break once when the value becomes true, or at every statement while it stays true?

**The probe:**
- **The IDE:** VB6.EXE 6.00.9782 (SP6), in the console session of a clone of the oracle guest. It was driven through
  the console agent, as "Reaching the VB6 *IDE*, not just the compiler" in the README describes.
- **The program:** a Standard EXE whose startup is `Sub Main`. `v` is a module-level `Variant` and `n` an `Integer`.
  Each value was its own run, in a fresh IDE, with this module:

  ```vb
  Sub Main()
      v = False
      n = 1
      v = <the value>
      n = 2
      n = 3
      v = False
      n = 4
      n = 5
  End Sub
  ```
- **The watch:** F8 to break at `Sub Main()`, then Debug > Add Watch. Its expression was `v`, with *Break When Value Is
  True*, in the dialog's default context, which the Watches window showed as `Module1.Main`. Then F5, and F5 again at
  each break, until the IDE was back in design mode.
- **What was recorded** at each break:
  - the IDE's title and a screenshot;
  - the Watches window's row, copied with Ctrl+C. A watch break leaves the focus there, and the row gives the value and
    its type;
  - the next statement: Debug > Show Next Statement, then the caret's line.
- **The IDE's options:** as installed, with Error Trapping at its default (no value in the registry). Six rows (`" 5 "`,
  `"&H10"`, `"0"`, `Empty`, the Date and the Integer `0`) first ran while another probe had left Error Trapping on
  *Break on Unhandled Errors*. They were run again at the default, with the same results, and those are the rows shown.
- **Builds:** this is IDE behaviour, so there is no native or p-code build to compare.

**The first break came at the statement after the assignment that made the watch true,** before that statement ran.
The "next statement" column says where each break was.

| Value assigned to `v` | Breaks | Next statement at each break | The Watches window's value and type |
|---|---|---|---|
| `"True"` | 1 | `n = 2` | `"True"`, `Variant/String` |
| `"1"` | 1 | `n = 2` | `"1"`, `Variant/String` |
| `" 5 "` | 1 | `n = 2` | `" 5 "`, `Variant/String` |
| `"&H10"` | 1 | `n = 2` | `"&H10"`, `Variant/String` |
| `"abc"` | 1 | `n = 2` | **`<Type mismatch>`, `Variant/Empty`** |
| `""` | 1 | `n = 2` | **`<Type mismatch>`, `Variant/Empty`** |
| `"0"` | **3** | `n = 2`, `n = 3`, `v = False` | `"0"`, `Variant/String`, at each |
| `"False"` | 0 | | |
| `0`, an Integer | 0 | | |
| `Null` | 0 | | |
| `Empty` | 0 | | |
| `7` | 1 | `n = 2` | `7`, `Variant/Integer` |
| `True` | 1 | `n = 2` | `True`, `Variant/Boolean` |
| `#1/2/2000#` | 1 | `n = 2` | `02/01/2000`, `Variant/Date` |

`"0"` was run four times, and every run broke three times. In one of them only F5 was pressed between the breaks,
with nothing read in between, so the readout's keys don't cause the repeat.

**Two more runs** put a call to a Sub in the same module, `Helper`, straight after the assignment:

| Value | Breaks |
|---|---|
| `"0"` | At the call, `Helper`. Then at `Sub Helper()`, with the Watches window showing **`<Variable uses an Automation type not supported in Visual Basic>`, `Variant/Empty`**. Then none, although `v` still held `"0"` for `n = 2` and `n = 3` back in `Sub Main` |
| `"abc"` | Once, at the call. None after |

**A first, sequential run** tested the values in one `Sub Main`, with `v = False` between them and a call to a logging
Sub, `L`, after each. It didn't include `"False"`, `""` or the Integer `0`. It agreed on which of the others break, and
how often, except for `"0"`:
- `"0"` first broke at `L`'s declaration, after the call had begun, rather than at the call;
- it then broke at each statement of `L`, with the Watches window showing `"0"`;
- once `L` returned to `Sub Main`, it didn't break again.

**Readings:**
- **The watch breaks once, when the value becomes true.** It didn't break again at `n = 3` while the value stayed true.
  The exception is `"0"`.
- **Strings that `CBool` reads as True break:** `"True"`, `"1"`, `" 5 "`, `"&H10"`. `"False"` doesn't.
- **Strings that `CBool` can't convert break too:** `"abc"` and `""`. The Watches window shows `<Type mismatch>`. So a
  failed conversion counts as a break, not as "doesn't hold". **This overturns the rule #869 proposed.**
- **`"0"` breaks, and goes on breaking after every statement while it holds.** That's measured four times. Yet
  `CBool("0")` is False, and the Integer `0` doesn't break.
  - **No rule I would defend.** A guess is that VB6 tests "is true" one way and remembers "was true" another, so a
    `"0"` never counts as already true. That is a guess, not a measurement.
  - **Across a call,** the `"0"` breaks stopped after the watch was read out of context, as the second table shows.
- **`Null` and `Empty` don't break.**

### In HexIDE

Per #869, HexIDE's watch reads "true" with `DebugInspector.IsTruthy`: a Boolean, or a number that isn't 0. So no String
watch breaks. That wasn't checked again for this page. Against these rows, HexIDE agrees on `True`, `7`, `0`, `Null`,
`Empty` and `"False"`, and differs on every String that broke here.

### Not measured

- *Break When Value Changes*;
- a watch on an expression rather than a variable, such as `v = 1`;
- a watch whose context is *(All Procedures)* or *(All Modules)*;
- `"0"` with leading or trailing spaces, `"0.0"`, `"-0"` and `"00"`, which might tell the `"0"` rows apart;
- a Date of 0, `#12:00:00 AM#`.
