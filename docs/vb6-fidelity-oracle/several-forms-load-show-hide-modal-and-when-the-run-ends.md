## Several forms: load, show, hide, modal, and when the run ends (2026-10-14, #822)

The seven questions of #822's design note for slice 2a. There are 21 projects, two forms each. Every form logs
Initialize, Load, Resize, Activate, Deactivate, QueryUnload, Unload and Terminate, with the seconds since the start, and
every project ends itself.

**How they were run:**
- every project native and p-code, which agree in every probe;
- in vb6.exe's IDE (`/runexit`) for the modal, hide and run's-end probes, where the host could matter. The IDE matches
  p-code in every one.
- Most runs were in the guest's session 0, which has no interactive desktop. Two things read differently there, so the
  modal probes were run on the console session too. The console answers stand.

**1. Which use loads a form.** Form1 touches Form2 once, then logs `Forms.Count`, which counts loaded forms without
touching Form2.

| first use | raises | loaded after it |
|---|---|---|
| `x = Form2.Caption` | Initialize, Load | yes |
| `x = Form2.Text1.Text` | Initialize, Load | yes |
| `x = Form2.pub`, a Public variable | Initialize | **no** |
| `Form2.pub = 3` | Initialize | **no** |
| `Form2.Foo`, a Public Sub | Initialize, then Foo runs | **no** |
| `Set o = Form2` | Initialize (`TypeName(o)` is `Form2`) | **no** |

**Any use of the name makes the instance and raises Initialize. Only a control or a built-in member loads it.** None
of them shows it.

**2. `Load Form2`, then `Form2.Show`.**
- **Load:** raises Initialize and Load, and returns with the form loaded and not visible.
- **Show:** raises Resize and returns. Form1's Deactivate and Form2's Activate come after the statement has returned.
- **Re-run on the console (2026-11-18, #4768): the same.** The events, their order and the activation after the
  statement were the same in the IDE (`/runexit`), p-code and native, with the probe logging session id 1. One reading
  differed: after `Form2.Show`, `Form2.Visible` and `IsWindowVisible` read True on the console, where session 0 read
  False for both.

**3. Hide.**
- **`Form2.Hide`:** the form stays loaded, and `Forms.Count` is still 2.
- **Hiding both forms:** the run keeps going. Form1's Timer ticked four more times, until the forms were unloaded.
- **Activation:** no Deactivate was logged for a hidden form. That was in session 0, and activation there is not
  trustworthy, so it is unmeasured.

**4. Unload, then use again.**
- **After `Unload Form2`:**
  - `Form2.pub` still reads **5**, the value set before the unload, and does not reload the form;
  - `Form2.Caption` reloads it, raising **Load without Initialize**.
- **Terminate** is not raised by Unload. It came at `Set Form2 = Nothing`, before the next statement.
- **After that,** `Form2.pub` makes a **new instance** (Initialize, with pub 0), not loaded.

**5. `New Form2`.**
- **`Set f = New Form2`:** raises Initialize at once, and the form is not loaded.
- **`f.Caption = "second"`:** loads it.
- **`f.Show`:** shows it.
- **`Form2.Show` after that:** makes the **default instance**, a third Initialize and Load, so three forms are
  loaded. `f Is Form2` is False, and the captions are "second" and "Form2".
- **`Set f = Nothing`, after `Unload f`:** raises f's Terminate.
- **Re-run on the console (2026-11-18, #4768): the same,** in the IDE, p-code and native: three forms loaded,
  `f Is Form2` False, and, once the handler had returned, Form1's Deactivate, f's Activate, f's Deactivate and the
  default instance's Activate. The re-run's Initialize read `Me.Name`, which loads the form, so it doesn't test again
  that `New` alone leaves the form unloaded. That row doesn't depend on a display.

**6. `Show vbModal`.** Form1's Timer1 ticks every 200 ms throughout. Its Timer2 shows Form2 modally at 0.6 s, and
Form2's own timer reports on Form1, then unloads (6a) or hides (6b) Form2 at its fifth tick.
- **Form1's Timer keeps ticking while Form2 is modal,** in native code, in p-code and in the IDE. This is the
  measurement the design waited on. The obvious guess, that the IDE holds other forms' Timers behind a modal form, is
  wrong here. **It is right for a `MsgBox`**, measured straight after:
  - **in the IDE**, no tick came in the 5 s a MsgBox was up, from a Timer2 on the same form;
  - **compiled**, Form1's Timer ticked under it for the whole 29 s.

  Both runs were in session 0. So a modal form lets other events run in either build, and a MsgBox holds them, but
  only in the IDE.
  **Re-run on the console (2026-11-18, #4768): the same.** A 100 ms Timer on the form that called `MsgBox` didn't
  tick while the box was up in the IDE (`/runexit`). Compiled, it had ticked 47 times (p-code) and 46 (native) by the
  time a box closed after about 5 s returned. The probe logged its session id, 1.
- **Form1 is disabled while Form2 is modal.** On the console, `Form1.Enabled` reads False and so does
  `IsWindowEnabled(Form1.hWnd)`, and both read True again once the modal form has gone. In session 0 both read True
  throughout, the one place that session misled.
- **A click posted to Form1's button still ran its Click** (6c), on the console as in session 0. That shows only that
  posted messages bypass the disabled window. A person's click cannot reach a disabled window, so it is not evidence
  that input runs.
- **The statement after `Show vbModal`** runs once Form2 has unloaded (6a) or hidden itself (6b), and after the handler
  that did so has returned. After a hide, Form2 is still loaded.
- **Activation:** Form1 is deactivated as Form2 activates, and activated again when the modal returns.

**7. When the run ends.**
- **Sub Main showing a form and returning:** Main ends straight after the form's Resize, before its Activate (as E3
  had it). The run then lasts until the form unloads. Re-run on the console (2026-11-18, #4768): the same, in the IDE,
  p-code and native.
- **Sub Main loading a form hidden and returning:** the run goes on; its Timer ticked until it unloaded itself.
- **Sub Main showing a form modally:** Main waits. Once the form has unloaded, Main goes on with no form loaded, and
  returns.
- **The last visible form unloading while another form is loaded:** the run goes on with the other form, hidden or
  shown, and ends only when it unloads.
- **So the run ends when no form is loaded, shown or hidden, and Sub Main has returned.**
- **The Terminates of default instances never released** come at the run's end, all together, in the order the forms
  were first created.
  **Overturned 2026-10-28 (#2496, #2497):** they come form by form in the order the project lists the forms, and within
  one form the instance made last comes first. These probes made their two forms in the project's order, so the two
  rules gave the same answer. The measurement is in [The Forms collection, "The Terminates at the run's
  end"](the-forms-collection.md#the-terminates-at-the-runs-end).

Unrelated to the questions but recorded: in session 0 a shown form reads `Visible` False, as #973's probe saw.

**8. An error in another form's Initialize or Load, under the caller's `On Error`.** Form1's Timer calls a Sub that sets
`On Error GoTo h`, then uses Form2 once. Form2 divides an Integer by an Integer variable holding 0 in its Initialize or
its Load. Compiled only, native and p-code agreeing.

| use | Form2 errs in | the caller's handler |
|---|---|---|
| `Load Form2` | Load | **not reached**: the run stops at the error |
| `L Form2.Text1.Text` | Load | **not reached** |
| `Form2.Show` | Load | **not reached** |
| `Form2.pub = 1` | Initialize | **traps it**: `caught 11 Division by zero` |

- **Load is an event, and its error is not the caller's.** The log stops after Form2's Load line and the run hangs on
  the unhandled-error box (session 0 can't dismiss it). This matches what b1 to b3 found for other events.
- **Initialize is not a boundary.** Its error comes back through the statement that used the form's name, as a class's
  Initialize error comes back through its `New`. After the trap Form1's Timer ticked on, and the run ended at its End.
- **What it overturned.** HexIDE's first cut reported an error in either the same way, at the boundary, on the
  reasoning that both are event procedures. Initialize isn't one in this sense.
- **The IDE is not measured here.** Under the IDE's default Break in Class Module, a form counts as a class module,
  which is #1011's c1. HexIDE follows the compiled program there: the caller's handler catches it (decided
  2026-10-15).

**9. Unload of a form that isn't loaded.**
- **`Unload Form2`, with Form2 never used:** raises Form2's Initialize, from the use of its name, then nothing. There
  is no QueryUnload, Unload or error, and `Forms.Count` stays 1.
- **`Form2.pub = 1`, then `Unload Form2`:** the same. Afterwards `Form2.pub` still reads 1, so the instance was kept.
- **`Unload Me` inside the form's own Initialize** does nothing, for the startup form and for another form:
  - the line after it runs;
  - the form then loads, and Show goes on as usual.

So an Unload of a form that isn't loaded does nothing, wherever it comes from. HexIDE had refused it as not
implemented.

**10. A use whose Load unloads the form.** Form2's Load unloads it with `Unload Me`. Form1 then uses Form2 once under
`On Error Resume Next`, logging `Err` and `Forms.Count`. Native and p-code agree.

| use | log |
|---|---|
| `x = Form2.Text1.Text` | Initialize, Load, QueryUnload 1, Unload, the rest of Load, then **364 "Object was unloaded"**. `x` is left Empty and `Forms.Count` is 1 |
| `x = Form2.Caption` | the same, and the same **364** |
| `Form2.Hide` | the same, and the same **364** |

- **Same answer for every use.** The `Show`, `Load` and `Show vbModal` rows under *Unload Me inside Form_Load* gave this
  answer too, and so does each use that loads the form.
- **No second load.** VB6 doesn't load the form again to finish the use. HexIDE's first cut did, retrying the use after
  the load, and so loaded it again on every retry.

**11. A loaded form's name set to Nothing.**
- **In Form1's own code, `Set Form1 = Nothing`, then `Form1.Caption`:** the name makes a **second instance**, with
  Initialize and Load. `Forms.Count` becomes 2, and `Form1 Is Me` is False. The first instance is untouched, and its
  Timer runs on, so the second Load's Timer started a second run of ticks. Nothing else reaches the first instance by
  name again.
- **Form2's name cleared while `f` holds it:** the sequence was `Set f = Form2`, `Form2.Show`, `Set Form2 = Nothing`,
  then `Unload f`.
  - The Unload raises QueryUnload and Unload, and no Terminate.
  - Terminate comes at `Set f = Nothing`, before the next statement.

  So a loaded form is held apart from its name, and its Terminate waits for the last reference, the name included, as
  measurement 4's `Set Form2 = Nothing` after its Unload showed from the other side.

**12. `Show vbModal`'s edges (2026-10-14, slice 2a.2).** Fourteen more projects, built the same way as the 21, native
and p-code, which agree in every one. Compiled only, in session 0: the IDE is not measured here. Form1's Timer1 ticks
every 200 ms, its Timer2 shows Form2 modally at 0.6 s, and each other form's Timer1 ticks every 200 ms from its Load.
Every row ran under `On Error Resume Next`, logging `Err.Number` and `Err.Description` after the statement.

**Re-run on the console (2026-11-18, #4744).** Session 0 displays no form: `Visible` and `IsWindowVisible` read False after `Show`, so every row below whose setup says a form is shown measured a hidden one. The rows were run again on the guest's console, in the IDE, p-code and native, which agree in every row. The last column has the result, and *A form displayed, and a form behind a modal one, re-measured on the console* below has the probe.

| statement | where | result in session 0 | on the console |
|---|---|---|---|
| `Form2.Show vbModal` | Form2 already shown, modelessly | **no error**: Form2 turns modal, and the statement returns when it has gone | **400 "Form already displayed; can't show modally"** |
| `Me.Show vbModal` | Form1 shown, no modal form displayed | **no error**: Form1 turns modal over itself, and the statement returns when its other Timer hides it | **400** |
| `Me.Show vbModal` | inside Form2, which is modal | **405 "Unable to show modal form within this context"** | **400**, not 405 |
| `Form1.Show vbModal` | Form2 modal, Form1 beneath it | **no error**: Form1 turns modal over Form2, and Form2's statement returns once Form1 hides itself | **400** |
| `Form2.Show vbModal` | Form2 shown, then hidden | no error, modal as usual | the same |
| `Form3.Show` (loaded, hidden) | Form2 modal | **401 "Can't show non-modal form when modal form is displayed"** | the same |
| `Form1.Show` (already shown) | Form2 modal | **401**, the same | the same |
| `Me.Show` | inside Form2, which is modal | **401**, the same | the same |
| `Load Form3` | Form2 modal | no error. Form3 loads hidden, and `Forms.Count` is 3 | the same |
| `Form1.Hide` | Form2 modal, Form1 not modal | **402 "Must close or hide topmost modal form first"** | 402, and **Form1 stays shown** |
| `Form2.Hide` | Form3 modal over Form2 | **402**, the same | 402, and **Form2 stays shown** |
| `Form2.Hide`, from Form1's Timer | Form2 modal and topmost | no error, and the modal returns | the same |
| `Me.Visible = False` | inside Form2, which is modal | the modal returns, as on Hide. Form2 stays loaded | the same |
| `Unload Form2`, from Form1's Timer | Form2 modal | the rest of Form1's Timer handler runs first, then the statement after `Show vbModal` | not re-run |
| `End` | inside Form2, which is modal | the run ends. The statement after `Show vbModal` never runs | not re-run |

**Show's style argument.** Form2 hid itself every fifth tick, so a modal Show returned about a second later.

| `Form2.Show` | result |
|---|---|
| `0`, `0.4` | modeless |
| `1`, `"1"`, `0.6` | modal |
| `2`, `-1`, `True`, `1.5` | **5 "Invalid procedure call or argument"**, raised after Form2's Initialize and Load: Form2 is loaded, and not visible |

So the style converts as `CInt` does, rounding 0.6 to 1 and 1.5 to 2, and only 0 and 1 are accepted.

**A modal form showing another.** Form2, modal, shows Form3 modally from its Timer's handler.
- Form1's Timer ticks throughout.
- Form2's Timer doesn't tick until Form3's modal has returned, because the waiting handler is that Timer's own, and a
  Timer never runs inside itself (#766).
- Each modal returns once its own form has gone, innermost first.

**The startup form's Load showing a form modally.** Form1 isn't shown while the modal is up, and reads `Visible` False.
Load goes on once Form2 has unloaded, and Form1 is then shown as any startup form is, with its Resize after Load.
The console agrees (2026-11-18, #4744): Form1 reads `Visible` False, and `IsWindowVisible` 0, while the modal is up.

**What was overturned.**
- **Err 400, "Form already displayed; can't show modally",** is documented for a modal Show of a form already shown.
  It never came: the shown form turned modal instead, in every case measured. The IDE wasn't measured, and an
  owner-form argument wasn't tried.
  - **Overturned in its turn (2026-11-18, #4744).** On the console 400 comes, in all four rows where the form is
    displayed, in the IDE, p-code and native. The same probe run in session 0 gave this table's results again: no
    error, and 405 for `Me.Show vbModal` inside the modal form. Reasoned, not checked: 400 never came here because
    session 0 displays no form.
- **Err 402's text** is "Must close or hide topmost modal form first". HexIDE's table had "Code must close topmost modal
  form first".
- **Err 5's text** is "Invalid procedure call or argument". HexIDE's table had "Invalid procedure call", and every Err 5
  it raised said so.

**13. A method call statement with no space before its parenthesised argument.** A form's Timer ran `Form2.Show(0)`
and `Text1.Move(100)`, written into the `.frm` that way. Both builds compile, and both statements run: Form2 loads and
shows, and `Text1.Left` reads 100 after the Move. VB6's editor would have rewritten each with a space, so this is what
a file written elsewhere gets. HexIDE's grammar refused both; #1064 now accepts them in the interpreter and language server. Re-run on the console
(2026-11-18, #4768): `Form2.Show(0)` the same in the IDE, p-code and native, with Form2 displayed (`Visible` True,
where session 0 read False). `Text1.Move(100)` wasn't run again, since it doesn't depend on a display.

**14. A 402 still hides the form, and a modal form hidden from above keeps waiting (2026-11-16, #4032, #3936).** Section
12's table records `Form1.Hide` and `Form2.Hide` behind a modal form as 402, and nothing more. HexIDE read that as a
refusal: it raised 402 and left the form shown. So this asks what the 402 leaves behind, for a modeless form and for a
modal form beneath the topmost one, through `Hide` and through `Visible = False`.

**The probe.** Run does `Form1.Show` and then `Form2.Show vbModal`. Form2's Activate does `Form3.Show vbModal`. Form3's
Activate runs the statements below under `On Error Resume Next`, logging `Err.Number` and the form's `Visible` after
each, and ends with `Me.Hide`. Form2's Activate then starts its own Timer, which unloads Form2. vb6.exe SP6, compiled
(`/make`), native and p-code agreeing in every row. The IDE wasn't measured.

| Statement, in Form3 (modal over Form2, modal over Form1) | `Err.Number` | `Visible` afterwards | on the console |
|---|---|---|---|
| `Form2.Hide`: a modal form beneath the topmost | **402** | **False** | 402, `Visible` **True** |
| `Form2.Visible = False`, in its place | **402** | **False** | 402, `Visible` **True** |
| `Form1.Hide`: a modeless form beneath both | 402 | **False** | 402, `Visible` **True** |
| `Me.Hide`: the topmost modal form | 0 | False, and its `Show vbModal` returns | the same |

**After Form3 returned,** Form2 was still hidden. **Its modal loop hadn't ended.** Form2's Timer fired, and Run's
`Form2.Show vbModal` returned only once that Timer had unloaded Form2. So hiding a modal form from above takes it off the
screen and leaves its `Show vbModal` waiting. Hiding the topmost modal form, as section 12 records, ends its modal loop.

**The rule these rows support.** Behind a modal form, `Hide` raises 402 and hides the form all the same, whether the
target is modal or not. `Visible = False` does the same to a modal form beneath. On #3936 a *modeless* form's
`Visible = False` behind a modal form gave no error (oracle PR #4034), so whether `Visible = False` raises 402 depends
on whether its target is itself modal. `Hide` raises 402 for both.

**What was overturned.** The obvious reading of section 12's 402 rows was a refusal that changes nothing, as 401 is
for `Show`: on #3936, `Form2.Show` behind a modal form gave 401 and left Form2 hidden. HexIDE built exactly that reading, and
raised 402 without hiding. vb6.exe raises 402 and hides the form anyway, so a program running under
`On Error Resume Next` sees the form go. These rows can't say which of the two happens first, only that both do. Measured because #3936's review left "does VB6 hide a modal form beneath?" unmeasured.

**Not measured:** the IDE (F5); a modal form beneath that is hidden and then shown again while the topmost stays up;
`Unload` of a modal form beneath.

**Re-run on the console (2026-11-18, #4744): the 402 leaves the form shown.** This section's probe, rebuilt from its
description, ran on the guest's console in the IDE (`/runexit`), p-code and native, which agree. Each 402 left its form
shown, `Visible` True and `IsWindowVisible` 1, and Form2 was still shown when Form3's modal returned. The same probe in
session 0 gave this section's table again, because there every form read `Visible` False before the statement as well
as after it. So the 402 is a refusal that changes nothing, as 401 is for `Show`: the obvious reading that this section
overturned was right. *The rule these rows support* and the paragraph after the table describe session 0, not VB6.
`Form1.Hide` and `Form2.Hide` from a Timer, section 12's rows, leave their forms shown too.

### A form displayed, and a form behind a modal one, re-measured on the console (2026-11-18, #4744, SP6)

**Why.** Sections 12 and 14 ran their programs in session 0, which displays no form. A form there reads `Visible` False
after `Show`, so every row that needed a displayed form measured a hidden one. These probes ran the same rows on the
guest's console, and again in session 0 to check that they reproduce the earlier tables.

**The probes.** vb6.exe SP6. Each project was built with `/make` as p-code and as native code, and each row also ran in the
IDE with `/runexit`. All three ran on the guest's console session. A row is chosen by `Command$`, and every statement runs
under `On Error Resume Next`, logging `Err.Number`, `Err.Description`, and each loaded form's `Visible`,
`IsWindowVisible` and `IsWindowEnabled`.
- **Section 12's rows:** Form1's Timer1 shows Form2 modally at 0.6 s, and Form2's Timer1 runs the row's statement 0.6 s
  after Form2 loads. The rows that need Form2 shown first show it in Form1's Load. A Timer on Form1 logs every form
  and ends the run at 5 s.
- **Section 14's rows:** as section 14 describes. `Sub Main` shows Form1 and then Form2 modally. Form2's Activate shows
  Form3 modally, and Form3's Activate runs the row's statement, then `Me.Hide`. Form2's Timer then unloads Form2.

**Section 12's rows.** The IDE, p-code and native agree in every row.

| Row | Statement | Where | Console | Session 0, this probe |
|---|---|---|---|---|
| m1 | `Form2.Show vbModal` | Form2 shown modelessly | **400** | no error logged: the statement still waiting at 5 s |
| m2 | `Me.Show vbModal` | Form1 shown, no modal form | **400** | the same as m1 |
| m3 | `Me.Show vbModal` | inside Form2, which is modal | **400** | 405 |
| m4 | `Form1.Show vbModal` | Form2 modal, Form1 beneath it | **400** | the same as m1 |
| m5 | `Form2.Show vbModal` | Form2 shown, then hidden | Form2 turns modal: Form1 disabled, the statement still waiting at 5 s | the same as m1 |
| m6 | `Form3.Show` (loaded, hidden) | Form2 modal | 401 | 401 |
| m7 | `Form1.Show` (shown) | Form2 modal | 401 | 401 |
| m8 | `Me.Show` | inside Form2, which is modal | 401 | 401 |
| m9 | `Load Form3` | Form2 modal | no error, `Forms.Count` 3 | the same |
| m10 | `Form1.Hide` | Form2 modal | 402, **Form1 still shown** | 402, Form1 `Visible` False |
| m11 | `Form2.Hide`, from Form3's Timer | Form3 modal over Form2 | 402, **Form2 still shown** | 402 |
| m12 | `Form2.Hide`, from Form1's Timer | Form2 modal and topmost | no error, and the modal returns | the same |
| m13 | `Me.Visible = False` | inside Form2, which is modal | no error, the modal returns, and Form2 stays loaded | the same |
| m16 | `Form2.Show vbModal` | in the startup Form1's Load | no error; Form1 reads `Visible` False meanwhile | the same |

The same `Form1.Show vbModal` under Form2's modal form also gave 400 from a Click on Form2, from Form2's Timer and from
Form1's own Timer, in the IDE run by a driver, and in p-code (all three) and native (the Click and Form1's Timer).

**Section 14's rows.** The IDE, p-code and native agree in every row.

| Row | Statement, in Form3's Activate | Console | Session 0, this probe |
|---|---|---|---|
| h1 | `Form2.Hide`: a modal form beneath the topmost | 402; Form2 `Visible` **True** | 402; `Visible` False, as before it |
| h2 | `Form2.Visible = False` | 402; Form2 `Visible` **True** | 402; `Visible` False, as before it |
| h3 | `Form1.Hide`: a modeless form beneath both | 402; Form1 `Visible` **True** | 402; `Visible` False, as before it |

After `Me.Hide`, Form2 was shown on the console, and Form2's Activate went on to start its Timer, which unloaded it.

**The rule these rows support.**
- **`Show vbModal` of a displayed form raises 400.** That includes a modal form showing itself again, which section 12
  recorded as 405. A form that was shown and then hidden turns modal as usual (m5).
- **Behind a modal form, `Hide` and a modal form's `Visible = False` raise 402 and change nothing.** The form stays shown.

**What was overturned.** Section 12's "400 never came" and section 14's "402 still hides the form", both recorded in
session 0. Each held there and in no other run. The session-0 runs of these probes reproduced both, so the earlier
probes measured what they measured: the cause was where they ran. Not overturned: the 401 and 402 numbers, `Load`
under a modal form, and the rows where the modal returns.

**Not measured:** section 12's `Unload Form2` from Form1's Timer and `End` inside the modal form, on the console; a
console path to 405; a minimised form as the target of `Show vbModal`.

### A form's name as a type: New, As New, an Object and an interface (2026-11-18, #4598, SP6)

**The probes.**
- **The forms:** Form1 ran each step from a Timer and logged to a file. Form2 numbered its instances in its
  Initialize, and logged each event with that number. No handler read a property, so the log loaded nothing.
- **The interface:** Form2 `Implements IThing`, a class with `Hello` and a `WhoAmI` property.
- **The builds:** p-code and native code logged the same lines, in each of three runs.

**In a form's code.** Every step ran inside `Tick1`, a Sub of Form1 called from the Timer, so `f`, `g`, `h`, `k`, `o`
and `t` are Tick1's locals.

| Step, in order | Events | Forms.Count after |
|---|---|---|
| at the start | | 1 |
| `Set f = New Form2`, with f As Form2 | Initialize #1 | 1: not loaded |
| `f.Id`, a Public Function of Form2 | none | 1 |
| `f.Tag = "one"` | Load #1 | 2 |
| `Dim g As New Form2`, before g is used | none | 2 |
| `g.Id` | Initialize #2 | 2 |
| `g.Caption = "g"` | Load #2 | 3 |
| `Set h = New Form2`, then `Load h` | Initialize #3, Load #3. h.Visible is False | 4 |
| `Set k = New Form2`, then `k.Show` | Initialize #4, Load #4. Activate #4 came only after the Timer's handler returned | 5 |
| `Set o = New Form2`, with o As Object | Initialize #5. `TypeName(o)` is "Form2", and `o.Id` is 5 | 5 |
| `Set t = New Form2`, with t As IThing, then `t.Hello` | Initialize #6, IThing_Hello #6 | 5: a call through the interface doesn't load it |
| `UseIt New Form2`, with `Sub UseIt(ByVal w As IThing)` reading `w.WhoAmI` | Initialize #7, then UseIt's own line, then Terminate #7 as the call returned | 5 |
| `TypeName(New Form2)` | Initialize #8, then Terminate #8. It reads "Form2" | 5 |
| `Unload f` | QueryUnload #1 with UnloadMode 1, Unload #1 | 4 |
| then `f.Id` | none. It reads 1 | 4 |
| then `f.Tag` | **Load #1, with no Initialize.** It reads "" | 5 |
| `Unload f`, then `Set f = Nothing` | QueryUnload #1, Unload #1, then Terminate #1 at the Set | 4 |
| `Set k = Nothing`, with k showing | none: k stays loaded and shown | 4 |
| Tick1 returns | Terminate #5 and #6, for o and t, before the Timer's handler ran its next line. Not #2, #3 or #4, which are loaded | |

**In a standard module and a class**, from a third run with a Form2 holding a Label:

| Step | Events and values |
|---|---|
| `Sub ModLocal` in Module1: `Dim f As Form2`, `Set f = New Form2`, then read `f.Id` | Initialize #1. `TypeName(f)` is "Form2". Terminate #1 as ModLocal returned |
| `Sub ModGlobal` in Module1: `Set gf = New Form2`, gf a module-level `Public gf As Form2` | Initialize #2. From Form1, `TypeName(gf)` is "Form2" and `gf Is Form2` is False |
| `Set x = Make()`, with `Function Make() As Form2` in Module1 doing `Set Make = New Form2` | Initialize #4, x.Id 4. Not loaded |
| `Function Blank() As Form2` that sets nothing | `TypeName(Blank())` is "Nothing", and `Blank() Is Nothing` is True |
| The same As Class1, and As Form | "Nothing", both |
| `Take New Form2`, with `Sub Take(ByVal w As Form2)` in Module1 | Initialize #5, Take's own line reading "Form2", then Terminate #5 as the call returned |
| `Set c = New Class1`, whose Class_Initialize does `Set SUT = New Form2` into a `Private SUT As Form2` | Initialize #6 inside Class_Initialize |
| `Set c = Nothing` | Class1's Terminate, then Terminate #6 |
| `Set x = New Form2` over x's earlier instance | Initialize #7, then Terminate #4 for the instance x held |
| then `x.Label1.Caption` | **Load #7**: a control of a New instance loads it |

**From the first run**, whose handlers read `Me.Tag`:
- **Types:** `TypeName(f)` read "Nothing" before the Set. After it, `TypeOf f Is Form2`, `TypeOf f Is Form` and
  `TypeOf f Is IThing` were all True. `Set x = f`, with x As Form, worked, and `TypeName(x)` read "Form2".
- **The default instance:** `f Is Form2` was False, and naming Form2 made the default instance.
- **A property read in Initialize loads the form there and then.** Reading `Me.Tag` inside Form_Initialize ran
  Form_Load before Initialize's own line finished. So a log taken in Initialize can make Load look as if it came first.

**What it shows**, extending section 5 above:
- **A form's name is a type,** and `New` makes a new instance of it, from a form, a standard module or a class. That
  holds in a variable of the form's own type, As Form, As Object, or As an interface the form implements, and in a
  Function's result.
- **A New instance loads as the default instance does:** on its first control or built-in member. The form's own
  Public members don't load it, and nor does a call through an interface it implements.
- **`As New` makes the instance when the variable is first used.**
- **A temporary instance terminates as soon as the statement or call that used it ends.**
- **An instance that isn't loaded terminates when its last reference goes,** as a class's does: at the Set, or when
  the procedure whose local held it returns.
- **A loaded form outlives its variables.** `Set k = Nothing`, and a local going out of scope, neither unloads nor
  terminates it. The Forms collection keeps it.

**Not measured:** `Unload Me` inside a New instance.

### Who owns a modal form, and what F5 brings back (2026-11-18, #4618, SP6, IDE)

**The probes.** vb6.exe SP6's IDE, run with F5 (p-code), on the guest's console session. A driver in that session clicked
the buttons and pressed the keys. After each step it read the foreground window and every top-level window of VB6.EXE in
z-order, each with `GetWindow(GW_OWNER)`, `IsWindowEnabled`, `IsIconic` and its extended style. Form2's Activate also
logged `GetWindow(Me.hWnd, GW_OWNER)` itself.
- **Two forms:** Form1, with a Timer retitling it and two buttons, and Form2. Rows a and b, run once.
- **Three forms:** Form1, Form2 and Form3. Form1's Load shows Form3 modelessly. Rows c, d and e. Rows c and d ran twice
  and row e once, with the same owners each time.
- **Three forms and a standard module (2026-11-18, #4744).** Module1's `OpenModal` runs `Form2.Show vbModal`. Rows f
  and g ran once in the IDE, driven as above. Rows h to k ran in the self-driving project of *A form displayed,
  re-measured on the console*, in the IDE (`/runexit`), p-code and native, which agree.

**1. The owner.**

| Row | Statement, and whose code runs it | Active form when it runs | Form2's owner |
|---|---|---|---|
| a | `Form2.Show vbModal`, in Form1's Click | Form1 | Form1 |
| b | `Form2.Show vbModal, Form1`, in Form1's Click | Form1 | Form1 |
| c | `Form2.Show vbModal, Form3`, in Form1's Click | Form1 | **Form3** |
| d | `Form2.Show vbModal`, in Form1's Timer | **Form3**, by `Screen.ActiveForm` and the foreground | **Form1** |
| e | `Form2.Show vbModal`, in Form3's Timer | **Form1**, by both | **Form3** |
| f | `OpenModal`, called from Form1's Click | Form1 | Form1 |
| g | `OpenModal`, called from Form1's Timer | **Form3** | **Form1** |
| h | `OpenModal`, called from Form3's Timer | **Form1** | **Form3** |
| i | Form1's Timer calls Form3's `CallOpen`, which calls `OpenModal` | Form1 | **Form1** |
| j | Form1's Timer calls Form3's `ShowDirect`, which runs `Form2.Show vbModal` itself | Form1 | **Form1**, not Form3 |
| k | `OpenModal`, called from `Sub Main` after `Form1.Show` and `Form3.Show` | Form3 | **Form3** |
| l | `Form2.Show vbModal`, in the startup Form1's Load | not logged | the hidden main window |

**The rule these rows support.** The modal form's owner is the owner-form argument when there is one. Otherwise it's the
form whose event is running, however deep the `Show` is: in a standard module (rows f to i) or in another form's
method (row j). It isn't the active form (rows d, e, g and h). Two cases fall outside that:
- **The startup form's Load,** before the form is displayed, gives the hidden main window (row l).
- **With no form's event running,** as in `Sub Main`, the owner was the active form (row k). That row can't tell the
  active form from the form shown last.

Rows a to e were written up on 2026-11-18 as "the form whose code ran `Show`". Every `Show` in them was in the event
handler itself, so that reading and this one agreed there. Row j separates them.

**2. The same in every row.**
- **Modeless forms are owned too, by the hidden main window.** Form1 and Form3 are each owned by an invisible
  `ThunderMain` window titled with the project's name, whose extended style is &H80 (a tool window). Each form carries
  &H40000, `WS_EX_APPWINDOW`, which is what gives it a taskbar button.
- **The modal form has no taskbar button.** Form2's extended style is &H100 alone, without `WS_EX_APPWINDOW`, although
  its `.frm` sets no `ShowInTaskbar`. During row a, Alt+Tab listed two entries, Form1 and the IDE, and none for Form2.
- **Every other form is disabled.** Form1 and Form3 alike read `IsWindowEnabled` 0, whichever owns the modal form, and 1
  again once Form2 has closed. The IDE's windows stay enabled.
- **Closing Form2 brings back the form that was active before it, not its owner.** The foreground went to Form1 after
  rows c and e, and to Form3 after row d.

**3. Minimise and restore,** in rows a and b alike. The driver posted `WM_SYSCOMMAND` to each window, as its title-bar
button would send.

| Step | Foreground afterwards | VB6.EXE's windows afterwards, top first |
|---|---|---|
| `SC_MINIMIZE` to Form2 | the IDE | the IDE, Form2 minimised, Form1. **Form1 wasn't minimised** |
| `SC_RESTORE` to Form2 | Form2 | Form2, Form1, the IDE |
| `SC_MINIMIZE` to Form1, disabled under Form2 | Form2 | as before: **Form1 wasn't minimised** |

A click on Form1's taskbar button wasn't tried. The last row is a posted message only.

**4. Ctrl+Break, then F5.** The hidden main window, always directly below the forms, is left out.

| Row | After Ctrl+Break: foreground; windows, top first | After F5 |
|---|---|---|
| a, b (owner Form1) | the IDE; the IDE, Form2, Form1 | Form2; Form2, Form1, the IDE |
| c (owner Form3) | the IDE; the IDE, Form2, Form3, Form1 | Form2; Form2, Form3, **Form1**, the IDE |
| d (owner Form1) | the IDE; the IDE, Form2, Form1, Form3 | Form2; Form2, Form1, **Form3**, the IDE |

**F5 brings back every form of the program, not only the modal form and its owner.** In row c, Form1 owns nothing and
isn't Form2's owner, and it came back above the IDE too, in the place it held before. Reasoned, not checked: every form
is in one ownership tree under the hidden main window, and Windows raises an owned window's whole tree with it.

**A break while a modal form waits, with no code running (2026-11-18, #4708).** Rows c and d broke with no code
running: three-form Form1 has no ticking Timer, and row d's Timer disables itself before its `Show`. VB6 broke at once,
opened the calling form's code window, and made the waiting `Show` statement the current one, with the yellow arrow and
highlight:
- **row c:** `Form2.Show vbModal, Form3` in `Command1_Click`;
- **row d:** `Form2.Show vbModal` in `Timer1_Timer`.

These come from the screens the driver took at each Ctrl+Break, in the second three-form run. F5 then resumed the wait,
with Form2 still modal and in front (section 4's table). Rows a and b had Form1's Timer ticking throughout, so they don't
show this case. Not measured: what F8 does from that stop.

**What was overturned.** The obvious guess, and #4618's own wording, was "the form beneath the modal one", which reads
as the active form. Rows d and e pull the active form and the owner apart, and the owner follows the code that ran
`Show`. And bringing back the modal form's owner alone is not what F5 does: row c brings back a form that owns nothing.

**Not measured:**
- a modal `Show` from `Sub Main` with no form loaded;
- row k with the active form different from the form shown last;
- an owner form that is hidden or not loaded;
- a modeless `Show` with an owner form, `Form2.Show , Form1`;
- the compiled program, for rows a to g;
- whether Form1 or Form3 gets Activate or Deactivate as the modal form closes: the probes logged no events of theirs.
