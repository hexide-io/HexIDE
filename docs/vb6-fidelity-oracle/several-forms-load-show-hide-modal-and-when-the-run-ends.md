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

**6. `Show vbModal`.** Form1's Timer1 ticks every 200 ms throughout. Its Timer2 shows Form2 modally at 0.6 s, and
Form2's own timer reports on Form1, then unloads (6a) or hides (6b) Form2 at its fifth tick.
- **Form1's Timer keeps ticking while Form2 is modal,** in native code, in p-code and in the IDE. This is the
  measurement the design waited on. The obvious guess, that the IDE holds other forms' Timers behind a modal form, is
  wrong here. **It is right for a `MsgBox`**, measured straight after:
  - **in the IDE**, no tick came in the 5 s a MsgBox was up, from a Timer2 on the same form;
  - **compiled**, Form1's Timer ticked under it for the whole 29 s.

  Both runs were in session 0. So a modal form lets other events run in either build, and a MsgBox holds them, but
  only in the IDE.
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
  had it). The run then lasts until the form unloads.
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

| statement | where | result |
|---|---|---|
| `Form2.Show vbModal` | Form2 already shown, modelessly | **no error**: Form2 turns modal, and the statement returns when it has gone |
| `Me.Show vbModal` | Form1 shown, no modal form displayed | **no error**: Form1 turns modal over itself, and the statement returns when its other Timer hides it |
| `Me.Show vbModal` | inside Form2, which is modal | **405 "Unable to show modal form within this context"** |
| `Form1.Show vbModal` | Form2 modal, Form1 beneath it | **no error**: Form1 turns modal over Form2, and Form2's statement returns once Form1 hides itself |
| `Form2.Show vbModal` | Form2 shown, then hidden | no error, modal as usual |
| `Form3.Show` (loaded, hidden) | Form2 modal | **401 "Can't show non-modal form when modal form is displayed"** |
| `Form1.Show` (already shown) | Form2 modal | **401**, the same |
| `Me.Show` | inside Form2, which is modal | **401**, the same |
| `Load Form3` | Form2 modal | no error. Form3 loads hidden, and `Forms.Count` is 3 |
| `Form1.Hide` | Form2 modal, Form1 not modal | **402 "Must close or hide topmost modal form first"** |
| `Form2.Hide` | Form3 modal over Form2 | **402**, the same |
| `Form2.Hide`, from Form1's Timer | Form2 modal and topmost | no error, and the modal returns |
| `Me.Visible = False` | inside Form2, which is modal | the modal returns, as on Hide. Form2 stays loaded |
| `Unload Form2`, from Form1's Timer | Form2 modal | the rest of Form1's Timer handler runs first, then the statement after `Show vbModal` |
| `End` | inside Form2, which is modal | the run ends. The statement after `Show vbModal` never runs |

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

**What was overturned.**
- **Err 400, "Form already displayed; can't show modally",** is documented for a modal Show of a form already shown.
  It never came: the shown form turned modal instead, in every case measured. The IDE wasn't measured, and an
  owner-form argument wasn't tried.
- **Err 402's text** is "Must close or hide topmost modal form first". HexIDE's table had "Code must close topmost modal
  form first".
- **Err 5's text** is "Invalid procedure call or argument". HexIDE's table had "Invalid procedure call", and every Err 5
  it raised said so.

**13. A method call statement with no space before its parenthesised argument.** A form's Timer ran `Form2.Show(0)`
and `Text1.Move(100)`, written into the `.frm` that way. Both builds compile, and both statements run: Form2 loads and
shows, and `Text1.Left` reads 100 after the Move. VB6's editor would have rewritten each with a space, so this is what
a file written elsewhere gets. HexIDE's grammar refused both; #1064 now accepts them in the interpreter and language server.

**14. A 402 still hides the form, and a modal form hidden from above keeps waiting (2026-11-16, #4032, #3936).** Section
12's table records `Form1.Hide` and `Form2.Hide` behind a modal form as 402, and nothing more. HexIDE read that as a
refusal: it raised 402 and left the form shown. So this asks what the 402 leaves behind, for a modeless form and for a
modal form beneath the topmost one, through `Hide` and through `Visible = False`.

**The probe.** Run does `Form1.Show` and then `Form2.Show vbModal`. Form2's Activate does `Form3.Show vbModal`. Form3's
Activate runs the statements below under `On Error Resume Next`, logging `Err.Number` and the form's `Visible` after
each, and ends with `Me.Hide`. Form2's Activate then starts its own Timer, which unloads Form2. vb6.exe SP6, compiled
(`/make`), native and p-code agreeing in every row. The IDE wasn't measured.

| Statement, in Form3 (modal over Form2, modal over Form1) | `Err.Number` | `Visible` afterwards |
|---|---|---|
| `Form2.Hide`: a modal form beneath the topmost | **402** | **False** |
| `Form2.Visible = False`, in its place | **402** | **False** |
| `Form1.Hide`: a modeless form beneath both | 402 | **False** |
| `Me.Hide`: the topmost modal form | 0 | False, and its `Show vbModal` returns |

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
