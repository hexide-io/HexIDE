## A form's Resize, Activate and Deactivate (2026-10-23, #994)

**The question.** When a running form raises Resize, Activate and Deactivate:
- on its first show, and when it's loaded without being shown;
- when it's shown again after Hide, and loaded again after Unload;
- when the program's active form changes, by Show, SetFocus, Hide, Unload or a click;
- when the program loses and regains activation to another application.

The lifecycle rows above give the order on show, `Form_Initialize`, `Form_Load`, `Form_Resize`, `Form_Activate`, for
one form. These rows give the rest.

**The probe.** Three projects, each built native and p-code with `/make` in session 0. Each EXE then ran in the
guest's interactive console through the console agent, since Activate and focus need a desktop. Every form logs every
event to a file with `Timer`'s time:
- Initialize, Load, Resize with its `ScaleWidth`, `ScaleHeight`, `WindowState` and `Visible`;
- Activate, Deactivate and Paint;
- GotFocus and LostFocus, for the form and its TextBox;
- QueryUnload, Unload and Terminate.

Form1, the startup form, steps through a script on a 600 ms Timer and logs each step before and after. The driver gives
the probe the foreground as it starts. When the probe logs "app switch window", the driver activates a Notepad it
started, then comes back:
- **Run 1:** by `AppActivate` of the probe's process;
- **Runs 3a and 3b:** by a real mouse click on Form1, then on Form2, with Notepad moved clear of both forms.

Native and p-code agreed line for line in every run.

**Run 1, the first show and the size.** Form3's Load sets its own Width and Height. Form4 is fixed-border with no
controls. Form5 is shown modal and unloads itself on its own Timer.

| Step | Log |
|---|---|
| start | `Form1 initialize`, `Form1 load 3600x2400 ws0 visFalse`, `Form1 resize 3600x2400 ws0 visTrue`, `Form1 activate`, `Form1 Text1 gotfocus`, `Form1 paint` |
| `Load Form2` | `Form2 initialize`, `Form2 load`, then the step's after: no Resize |
| `Form2.Show` | `Form2 resize ... visTrue`, the step's after, then `Form1 Text1 lostfocus`, `Form1 deactivate`, `Form2 activate`, `Form2 Text1 gotfocus`, `Form2 paint` |
| `Me.SetFocus`, from Form1's Timer | the step's after, then `Form2 Text1 lostfocus`, `Form2 deactivate`, `Form1 activate`, `Form1 Text1 gotfocus` |
| `Form2.Hide`, Form2 not active | nothing |
| `Form2.Show`, after the Hide | the step's after, then Form1's LostFocus and Deactivate, Form2's Activate, GotFocus and Paint: **no Resize** |
| `Unload Form2`, Form2 active | `Form2 queryunload 1`, `Form2 unload`, the step's after, then `Form1 activate`, `Form1 Text1 gotfocus`: **no Deactivate for Form2** |
| `Form2.Show`, after the Unload | `Form2 load` (no Initialize), `Form2 resize`, the step's after, then the activation |
| `Form3.Show`, whose Load sets `Width = 6000` and `Height = 4000` | `Form3 initialize`, `Form3 load 3600x2400`, **`Form3 resize 5760x2400 visFalse`**, the line after the Width, **`Form3 resize 5760x3420 visFalse`**, the line after the Height, `load end`, **`Form3 resize 5760x3420 visTrue`**, the step's after, then the activation |
| `Form4.Show`, fixed-border, no controls | `Form4 resize 3600x2400`, the step's after, then Form1's LostFocus and Deactivate, `Form4 activate`, **`Form4 form gotfocus`**, `Form4 paint` |
| `Me.WindowState = vbMinimized` | `Form1 resize 0x0 ws1 visTrue`, then the step's after |
| `Me.WindowState = vbNormal` | `Form1 resize 3600x2400 ws0`, the step's after, `Form1 paint` |
| `Me.Width = Me.Width + 300` | `Form1 resize 3900x2400 ws0`, the step's after, `Form1 paint` |
| `Me.Move Me.Left, Me.Top, Me.Width, Me.Height` | nothing |
| `Form5.Show vbModal` | `Form5 initialize`, `load`, `resize`, then Form1's LostFocus and Deactivate, `Form5 activate`, GotFocus, Paint; at Form5's Timer `queryunload 1`, `unload`, the step's after, then `Form1 activate`, `Form1 Text1 gotfocus` |
| Notepad activated, then the probe by `AppActivate` | nothing |
| `Unload Me`, at the end | Form1's QueryUnload, Unload and Terminate, then every other form's Terminate, in the order they were made |

**Run 2, hiding.**

| Step | Log |
|---|---|
| `Form2.Hide`, Form2 active | the step's after, then `Form2 Text1 lostfocus`, `Form2 deactivate`, `Form1 activate`, `Form1 Text1 gotfocus` |
| `Me.Hide`, Form1 not active | nothing |
| `Me.Show`, after that | the step's after, then Form2's LostFocus and Deactivate, Form1's Activate, GotFocus and Paint: no Resize |
| `Me.Hide`, Form1 active | the step's after, then Form1's LostFocus and Deactivate, Form2's Activate and GotFocus |
| `Form2.Visible = False`, then `Form2.Visible = True` | as `Hide` and `Show`: nothing for the inactive form hidden, and the activation when it shows |
| Notepad, then `AppActivate` of the probe, then `Unload Form2`, Form2 active | nothing at the switch; QueryUnload and Unload at the Unload, then nothing |

**Runs 3a and 3b, coming back by a click.** Form2 is shown, and so active, before the switch to Notepad.

| Back by | Log |
|---|---|
| a click on Form1 (p-code) | `Form2 Text1 lostfocus`, `Form2 deactivate`, `Form1 activate`, `Form1 Text1 gotfocus` |
| a click on Form1 (native) | nothing. The driver's own `AppActivate`, as the probe started, had already brought Form1 forward after Form2's Show: the log has Form2's Deactivate and Form1's Activate there. So Form1 was the active form when the program was left |
| a click on Form2, the active form | nothing |

What these show:
- **Resize comes once as a form is first shown after Load**, inside `Show`, before it returns, with `Visible` already
  True. `Load` alone raises none. A form shown again after `Hide` raises none, and one loaded again after `Unload`
  raises it again.
- **While the form is hidden, Resize is raised for each change of its size**, inside the statement: Form3's Load raised
  two, then the show raised a third at the same size.
- **After the show, Resize is raised for each change of size or state**, inside the statement that made it, and not for
  a `Move` to the size the form has. Minimized, the form reads 0 by 0 with WindowState 1.
- **Activate and Deactivate come after the statement that activated the form has returned**, as messages Windows
  posts. The order is the old form's control's LostFocus, the old form's Deactivate, the new form's Activate, then the
  new form's control's GotFocus. A form with no control to take the focus is given its own GotFocus after its Activate.
- **A form hidden while active is given Deactivate. One unloaded while active is not.** The form Windows activates next
  is given Activate.
- **Leaving for another application raises nothing, and nor does coming back to the form that was active.** Coming
  back to another of the program's forms raises the pair, as a switch inside the program does. So VB6 tracks which of
  its forms was last activated, and raises the pair only when that changes.
- **What looked like an exception, and was not.** In run 2, `Unload Form2` after the switch raised no Activate for
  Form1, where run 1's did. Windows activated Notepad, which lay under Form2, so no form of the program was activated.

### In HexIDE (#994)

**Resize.** A form's context raises it when its window first becomes visible since the form loaded, and after that
only when a size setter or the window reports a size or state that differs from the one Resize was last raised for.
Since #1570, Width and Height setters report the requested extent immediately, even during Load while hidden;
the subsequent layout report finds the same extent and raises nothing. WindowState changes on a shown form
use the same comparison. Before #1570, hidden assignments raised nothing, and the headless probe
`Debug.Print "wider": Me.Width = Me.Width + 450: Debug.Print "after"` printed `wider`, `after`, then Resize.
The window system reports a size more often than it changes one: at 150% scaling a desktop startup form raised three Resizes, and
headless two. The size is compared in whole device pixels: a form 4000 twips wide is 266.67 units, which the window
laid out again at 267 on a later show. Until the window system has sized a window, its size is the `Width` and `Height`
it is being shown at. Before that, `ClientSize` is still the platform's default, 1024 by 768 headless.

**Activate and Deactivate.** The program records the form Activate was last raised for. When one of its windows is
activated, it raises Deactivate on that form, if it's still loaded, and Activate on the new one, both posted. A
window's deactivation is never reported, so a switch to another application and back raises nothing. A form that
unloads stops being the active one, so it's given no Deactivate. A window a modal form holds isn't reported, as Windows
doesn't activate a disabled one. Posting matters on the desktop only: Win32 activates the window inside the Show, and
with the posting removed the pair ran there, before the statement after the Show. Headless, the activation comes after
the handler either way, so no headless test can tell.

**Live, on the desktop at 150% scaling.** A two-form program logged, in order:
- `f1 initialize`, `f1 load`, `f1 resize 5400x4200 ws0`, `f1 activate`;
- Show2: `f2 initialize`, `f2 load`, `f2 resize 3000x1500 ws0`, the line after the Show, `f1 deactivate`, `f2 activate`;
- Hide2, with Form2 active: `f2 deactivate`, `f1 activate`, Windows having activated Form1;
- Form2's own Unload button: `f2 unload`, `f1 activate`;
- Wider: one `f1 resize 5700x4200 ws0`;
- the same width set again: nothing.

**A form's `Move`** stopped the program, refused as not implemented. It runs since #1569, which measured it: [A form's
Move](a-forms-move.md#a-forms-move-2026-10-26-1569).

**Recorded here and left to their own issues:** GotFocus and LostFocus around Activate and Deactivate (#1101), and
Paint (#991).

**Not measured:** a form whose `.frm` starts it maximized or minimized. HexIDE raises the show's Resize at the size
the form was designed at, and another when the window takes its maximized size.

