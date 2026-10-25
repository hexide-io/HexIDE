## The reset prompt: which edits raise it, and which windows it holds (2026-10-22, #1329)

**The probe.** It ran in the VB6 IDE itself, in the oracle guest's console session, driven as "Reaching the VB6 *IDE*,
not just the compiler" describes. Each step was recorded with Win32 calls and a screenshot. This is IDE behaviour, so
there is no native or p-code build to compare.
- **The project:** a Standard EXE whose Form1 holds a CommandButton and a 250 ms Timer, each appending a line to a log.
- **The run:** F5, then Ctrl+Break, then an edit typed into Form1's code window.
- **What each step recorded:**
  - the owner (`GetWindow(…, GW_OWNER)`), text and buttons of every top-level window of the VB6 process;
  - `IsWindowEnabled` on the IDE's frame, the code window and Form1;
  - the IDE's mode, from its title;
  - whether the Timer was still ticking.
- **The IDE's options:** as installed. Nothing is stored beyond window layout.

**Which edits ask, in break mode.** VB6 checks an edit as the caret leaves the line:

| Edit | Result |
|---|---|
| A new module-level `Dim zz` under `Option Explicit` | **Applied live, no prompt.** Continue resumed the program, and the Timer ticked on |
| Deleting a body line of `Command1_Click`, which isn't on the stack | Applied live, no prompt, and Continue resumed |
| Retyping that variable, `Dim zz` to `Dim zz As Long` | **"This action will reset your project, proceed anyway?"**, with OK, Cancel and Help |
| Adding a parameter to `Log`, which three procedures call | The same prompt, with OK, Cancel and Help |
| A second `Dim zz` under the first | **"This edit requires a Reset"**, with OK and Help only |

- **The declinable prompt.**
  - Cancel put the line back as it was, `Dim zz`, and left the project in break mode with every window enabled.
  - OK kept the edit, ended the run (the IDE's title read `[design]`, and Form1 was destroyed) and re-enabled every
    window. The next F5 ran the edited code.
- **"This edit requires a Reset"** offers no way to decline. OK reset the project in the same way.
- **The caption of both** is "Microsoft Visual Basic", with the warning icon.

**Which windows the prompt holds.** In every case its owner was **the running form, Form1** (`ThunderFormDC`), not
the IDE, although the IDE's frame was the foreground window when the edit was made. While it waited:

| Window | Enabled |
|---|---|
| The IDE's frame (`wndclass_desked_gsk`) and its owner (`IDEOwner`) | False |
| Form1 (`ThunderFormDC`) | False |
| The program's hidden application window (`ThunderMain`) | False |
| The code window, an MDI child of the frame | True, but its frame is disabled, so it takes no input |

- **So the prompt holds the IDE and the running program together.** Every top-level window of the process is disabled,
  as a task-modal message box disables them.
- **Where it showed:** Form1 was brought to the front, and the prompt was centred on the screen, not over Form1.
- **Timers:** not observable here. The program is paused in break mode, so its Timer doesn't tick with or without the
  prompt, and run mode offers no prompt (below).

**Run mode offers no prompt.** While the program runs, its code window takes no typing:
- Ctrl+Home, End and the arrow keys moved the caret;
- Enter and the typed text were ignored;
- the project stayed `[run]`, with no prompt and no break.

**There is no "Notify Before State Loss" option.** VB6's Tools > Options holds six pages: Editor, Editor Format,
General, Docking, Environment and Advanced. None of them has it. That option belongs to the VBA editor in Office, not
to VB6.

**What this overturns.**
- **D2 in `docs/debugger-vb6-divergences.md`** quoted VB6's prompt as "This action will reset your project. Do you want
  to continue?", Yes and No. It is "…proceed anyway?", with OK and Cancel, beside a second prompt that can't be
  declined.
- **#1298's reason for the owner,** "as VB6's prompt holds the IDE", was half right. It holds the IDE, but its owner is
  the running form, and it holds that form too.
- **#1329's own suggested probe,** "a new module-level `Dim` while paused", is an edit VB6 applies live.

**In HexIDE:**
- **One prompt, for every edit** while the project runs or is paused (D2). It reads "This action will reset your
  project. Do you want to continue?", with Yes and No.
- **Its owner** is the code editor's window when that is visible and not minimized, and otherwise the top window
  (#1298).
- **Measured live, Windows, a Debug build of main 099da9af.** The probe project ran with a Timer counting on Command1's
  caption and a click setting Form1's caption. The key was sent with `type_text` in keyboard mode, with Form1 active.
  The native state was read with the same Win32 calls as above.

| Case | Owner | Disabled while it waits | The running program |
|---|---|---|---|
| Code window docked | The IDE's main window | The IDE's main window only | Runs on: the Timer went from 8 to 37, and a real click on Command1 ran its Click |
| IDE window minimized | Form1, the top window, through the fallback | Form1 only. The minimized IDE stays enabled | The Timer runs on (42 to 65), and Form1 takes no input |
| Code window floated | Not reached. Float hides the code window instead of floating it (#1361) | | |

- **Measured live, Linux, the openSUSE VM** (a Debug build of main 01d990c under Xvfb, with no window manager). There is
  no Win32 state to read there, so these come from the tools alone:
  - **Docked:** the prompt was modal over the IDE's main window, and the running form took a click under it (its
    `Form_MouseDown` ran).
  - **Minimized:** not reachable. With no window manager the window never minimized, so the case repeated the docked
    one.
  - **Floated:** the code window vanished from every tool, as on Windows (#1361).
  - That run also found that a form's own `Form_Click` never runs here (#1364).
- **Yes ended the run and kept the edit. No kept the run and put the code back.** Each left every window enabled, on
  both platforms.
- **So HexIDE's prompt holds less than VB6's.** VB6's holds the IDE and the program together. HexIDE's holds whichever
  one window owns it, and the program keeps running under it.
- **Two differences in when it asks.** HexIDE asks in run mode as well as in break mode, where VB6's code window takes
  no typing while the program runs. And it asks for every edit, where VB6 applies most edits live (D2).

