# MCP dev-server gaps — closed

Entries retired from [`../mcp-server-gaps.md`](../mcp-server-gaps.md). They are kept, rather than
deleted, because each records a *measurement*: what the symptom looked like, what was tried, and which
reading of it turned out to be wrong. That is the part which stops a fixed gap being rediscovered as a
new one, and it is not recoverable from the commit that closed it.

The live document holds only what is still open, so its length means something again.

---

## No tool said which mouse pointer a control shows — **CLOSED** (#2453, 2026-10-28)

**Symptom (2026-10-28, hexide-int, Windows, #2448's live check).** `inspect_element {"target":"Window","window":"Form1"}`
on a running form with `Screen.MousePointer` at 11 had no cursor field, `take_snapshot` draws no pointer, and
`dump_visual_tree` carries nothing about it. So `Screen.MousePointer`, a control's own MousePointer and a TextBox's
I-beam could not be checked in the running IDE. Avalonia's `Cursor` does not say what it shows.

**Fixed (#2453).** The runtime records the MousePointer each `Cursor` it makes came from (`VBCursorTypeExtensions.PointerOf`),
without caching them, since callers compare cursors by reference. `inspect_element` gives `pointer`: that MousePointer
by number and VB6 name, the standard pointer drawn, where it is set, and `setBy`, read from the property's value
priority: `Own` for a control's own value, `Override` for one set above it as `Screen.MousePointer` sets it, `Style`
for a style's. A pointer the runtime did not make is reported without a kind, saying it is not recorded. Guarded by
`InspectPointerTests`.

**Live (2026-10-28, Windows, headless `claude -p` through the hexide tools).** On main: a button designed with
MousePointer 11 read `{"mousePointer":11,"name":"Hourglass","shows":"Wait","setBy":"Own","setOn":"self"}`, and a
TextBox given 2 in `Form_Load` read `{"mousePointer":2,"name":"Cross",…}`. A label left at its default read
MousePointer 1, Arrow, which is #1970 (the design-time default is 1 where VB6's is 0), now visible. On #2448's branch,
with this change applied: `Screen.MousePointer = 11` in `Form_Load` over a button whose own MousePointer is 2 read
`{"mousePointer":11,"name":"Hourglass","shows":"Wait","setBy":"Override","setOn":"self"}`, and after a button set
`Screen.MousePointer = 0` it read `{"mousePointer":2,"name":"Cross","shows":"Cross","setBy":"Own","setOn":"self"}`.

---

## A dropdown's unrealized items couldn't be read: the dump saw ten of Options' 31 languages — **CLOSED** (#2450, 2026-10-28)

**Symptom (2026-10-28, hexide-mcp, a first-contact check of #2444).** The Language dropdown on Options' Language page
holds 31 items (`inspect_element` gave `itemCount` 31), but only the first ten had rows. `dump_visual_tree` with
`interactiveOnly: false`, after an `expand`, listed Arabic through Finnish and stopped, and `match: "fr"` found
nothing. `select` with `value: "French"` worked, by scrolling the list until French had a row, but nothing before
that told the caller French was there or how it was spelled. The headless session reported: "I couldn't see that
French exists beforehand."

**Fixed (#2450).** `inspect_element` on a list, an open dropdown or a tab control gives `items`: each item's text as
`select` matches it. A row that doesn't exist is brought into view to be read, as `select` does, and the list is put
back where it was before anything is drawn. Items that don't show are left out, as `select` passes over them, and at
most 200 are listed; `itemsNote` says what was left out. A closed dropdown has no rows to bring into view, and opening
it is a person's action, not a read, so it gives no `items` and `itemsNote` says to expand it. Guarded by
`InspectItemTextsTests`.

**Live (2026-10-28, Windows, a headless `claude -p` told to work from the descriptions alone).** Asked to find
every language the dropdown offers without selecting one, it inspected the closed dropdown and read `itemCount` 31
and the `itemsNote` telling it to expand. It expanded, inspected again, and listed all 31, from "System default"
to "Vietnamese", then collapsed it and cancelled the dialog. What it still tripped on was the two dropdowns having
no accessible name, which is #2449.

## `take_snapshot` of a minimized window returned its last frame as if current — **CLOSED** (#2416, 2026-10-28)

**Symptom (2026-10-27, Windows, #1925's minimized check).** After `set_window_state {"state":"Minimized","window":"Form1"}`
had run the form's Resize, `take_snapshot {"window":"Form1"}` returned a 450×300 image, with the usual client
`captureArea`, whose label read `resize 1, activate 1, state 0`: the form before it minimized. Restored, it read
`resize 3`. A minimized window draws no new frames, and the compositor capture (#2306) returns the last one.

**Workaround.** Restore the window with `set_window_state` before capturing it, and treat a capture of a minimized
window as stale.

**Suggested fix.** Refuse a minimized window in the default capture, as the frame capture does, naming
`set_window_state`; or render it fresh and say so. Never present the last frame as current.

**Fixed (#2416).** The default capture refuses a minimized window, as the frame capture does, and says how to
restore it. A person can't see a minimized window's contents either, so nothing is rendered in its place. Guarded by
`FrameCoverTests`, which pins the wording; headless can't minimize a window, so the refusal itself was checked live.

**Live (2026-10-28, Windows at 150%, headless `claude -p` through the hexide tools).** A form whose Resize writes
`resize N, state S` into a label:

```
set_window_state {"state":"Minimized","window":"Form1"}      → label: resize 2, state 1
take_snapshot {"window":"Form1"}
→ 'Form1' is minimized, so nothing of it is on the screen, and the last frame it drew before it was minimized would
  read as current; nothing was captured. Restore it with set_window_state {"state":"Normal","window":"Form1"},
  then capture it.
set_window_state {"state":"Normal","window":"Form1"}         → label: resize 3, state 0
take_snapshot {"window":"Form1"}                               → a 450×300 image reading "resize 3, state 0"
```

---

## `type_text` refused a running form's RichTextBox as having no text surface — **CLOSED** (#2460, 2026-10-28)

**Symptom (2026-10-28, hexide-frm, live-checking #2456's RichTextBox).** `type_text(target: "Window/None[RichTextBox1]",
text: "x", mode: "keyboard", window: "Form1")` answered `'VBRichTextBox' has no text surface
(TextEditor/TextArea/TextBox) to type into`. The RichTextBox is a `Control` HexIDE draws itself, and it takes typing in
a `TextInput` handler of its own. The refusal judged the target by its class, not by whether it takes text, so
typing, KeyPress and Change order, MaxLength and protected text could not be driven at all. Every in-box control
drawn the same way would have been unreachable in the same way.

**Fixed (#2460).** In keyboard mode a control that takes focus, with no text box under it, is sent the keys: KeyDown,
TextInput and KeyUp, as a person's keyboard sends them. What it did with them is read from what came back. A control
that handled the TextInput of none of the keys that type a character is reported as taking no typing, and the reply
says their KeyDown and KeyUp were raised. A control that cannot take focus is still refused, now saying why. Document
mode still needs a text box, and its refusal names keyboard mode. **A dropdown that is not editable behaves differently
in keyboard mode:** it takes focus, so it is now sent the keys, and its own text search selects by them, as a person's
typing does. A caller that learned "expand, then select" from the old refusal now sees the dropdown take the keys
instead; document mode still refuses it that way. Refusing typing a person can do was the defect.
Guarded by `TypeIntoOwnInputTests`, which type into a stand-in control that handles its own input.

**Live (2026-10-28, Windows, headless `claude -p` through the hexide tools)**, on #2456's branch with this change
applied. The probe form held a RichTextBox with `MaxLength = 5`, whose KeyPress and Change appended to a label:

```
type_text {"target":"Window/None[RichTextBox1]","text":"abcdefg","mode":"keyboard","window":"Form1"}
→ {"success":true,"mechanism":"keyboard","detail":"typed 7 key(s) into VBRichTextBox as a person types them"}
inspect_element {"target":"Window/Text[Label1]","window":"Form1"}
→ name: "P97 C[a] P98 C[ab] P99 C[abc] P100 C[abcd] P101 C[abcde] P102 P103 "
type_text {"target":"Window/None[RichTextBox1]","text":"x","window":"Form1"}
→ 'VBRichTextBox' has no text surface (TextEditor/TextArea/TextBox) to type into; mode "keyboard" sends it keys as a
  person types, for a control that takes typing itself
```

Every key ran KeyPress and every insert ran Change, until MaxLength stopped the inserts after the fifth; whether that
matches VB6's RichTextBox is #2456's to say, not this tool's.

## `get_window_state` mixed screen and device-independent pixels, and didn't say which edge it measured — **CLOSED** (#2454, 2026-10-28)

**Symptom (2026-10-28, hexide-int, Windows at 150%, #2448's live check).** The IDE, maximized, read
`{"x":-11,"y":-11,"width":1280,"height":649}`. A running form placed at Left 7200, Top 3600 twips read
`{"x":721,"y":325,"width":304,"height":200}`. `x` and `y` were the platform's position in screen pixels, which on
Windows includes the invisible resize border (the -11). `width` and `height` were the client area in device-independent
pixels. Nothing in the reply said either, so a caller could not tell whether a form's `y` of 325 against the 360 its
Top implies was a placement defect or only what the number measured.

**Fixed (#2454).** The four numbers are kept, as `set_window_state` takes them, and every reply now also carries:
- `scale`;
- `outer`, the window's whole rectangle, its invisible resize border included, which is where `x` and `y` are;
- `frame`, the window as drawn;
- `client`, the area inside;
- `units`, which says all of this.

The three rectangles are on the screen in device-independent pixels, twips divided by 15. Positions stay in screen
pixels for `x` and `y` because they are what the platform and `set_window_state` use, and the only coordinates that
hold across monitors of different scale. A hidden or minimized window has no rectangles. Guarded by
`WindowStateUnitsTests`; Windows' own rectangles are read from the HWND, which a headless test has none of, so those
were checked live.

**Live (2026-10-28, Windows at 150%, headless `claude -p` through the hexide tools).** A form whose `Form_Load` runs
`Me.Move 7200, 3600, 4800, 3600`:

```
get_window_state {"window":"Form1"}
→ {"state":"Normal","x":4,"y":23,"width":304,"height":200,"name":"Form1","scale":1.5,
   "outer":{"x":2.7,"y":15.3,"width":318.7,"height":238},"frame":{"x":8.7,"y":15.3,"width":306.7,"height":232},
   "client":{"x":10,"y":45.3,"width":304,"height":200.7},"units":"…"}
set_window_state {"state":"Normal","window":"Form1","x":720,"y":360}
→ "outer":{"x":480,"y":240,…},"frame":{"x":486,"y":240,…},"client":{"x":487.3,"y":270,…}
```

The first reply shows the form at Windows' default place, not at 480, 240, while the program's own `Me.Left` and
`Me.Top` read back 7200 and 3600. That is #852 (a form's Left and Top don't move the window at run time), whose
oracle rows say a form's Left and Top are the outer window's position, so `outer` is the rectangle to compare with
them. The reply now shows that defect instead of hiding it. The maximized IDE reads `outer` -7.3, -7.3 (the -11 in
screen pixels) and `frame` 0, 0.

---

## `press_key` with a character key typed nothing — **CLOSED** (#2428, 2026-10-27)

**Symptom (2026-10-27, Windows, #2422's live check).** `press_key {"target":"Window/Edit[Text1]","key":"K",
"modifiers":"Shift","window":"Form1"}` on a running TextBox logged `D16/1 D75/1 U75/1 U16/0`: no KeyPress, and no
text. The oracle puts KeyPress 75 between the KeyDown and KeyUp, and a person's press types K. The reply, `pressed
Shift+K`, does not say nothing was typed. The raw key events carry no text, which a platform sends separately.

**Workaround, until the fix.** `type_text` with `mode: "keyboard"`, which logged `D16/1 D75/1 P75 U75/1 U16/0`.

**Suggested fix.** Type the key's character between its KeyDown and KeyUp when it types one, as `type_text` does; or
say in the reply that nothing was typed and name `type_text`. [#2428](https://github.com/hexide-io/HexIDE/issues/2428).

**Fixed (#2428).** `press_key` sends the key's character after its KeyDown, as text of its own, as a platform sends
a person's press, through the window input and the routed fallback alike. None goes with Ctrl, Alt or Meta held,
whose chords are commands, and none for a control character such as Backspace's, which a running program's KeyPress
takes from the key itself. In a code window the key is judged as typing is, so a protected line refuses it. The
reply ends `it typed "K", as the key does`. Guarded by `PressKeyTypesTests` and `TypeKeysTests`.

**Live (2026-10-27, Windows, a Debug build of the fix on main at batch 86, which has #2418, headless `claude -p` through the
hexide tools).**

| `press_key` | Logged | Text1 | The oracle |
|---|---|---|---|
| K, `modifiers` Shift | `D16/1 D75/1 P75 U75/1 U16/0` | `K` | KeyCode 75, KeyAscii 75 |
| K | `D75/0 P107 U75/0` | `k` | KeyCode 75, KeyAscii 107 |
| A, `modifiers` Ctrl | `D17/2 D65/2 P1 U65/2 U17/0` | | KeyAscii 1, once |
| X, `modifiers` Alt | `D18/4 D88/4 U88/4 U18/0` | | no KeyPress |
| Back | `D8/0 P8 U8/0` | | 8 in all three |

## `open_file` couldn't put the caret in a carried file — **CLOSED** (#2403, 2026-10-27)

**Symptom (2026-10-27, Windows, #1334's live check).** `open_file {"name":"twice.c","line":3,"column":27}`, on a
project carrying `twice.c`, answered `{"success":false,"error":"'twice.c' is a carried file, which opens in the
plain-text editor, where this does not put the caret; nothing was opened"}`. The refusal is deliberate: #700 placed the
caret in the code window only.

**Workaround, until the fix.** `open_file` with no `line`, then walk the caret there with `press_key`: Ctrl+Home, Down per line, End or
Right. That is a call per keystroke, and a wrapped line or a refused key misplaces it.

**Suggested fix.** Place the caret in the carried-file editor, an AvaloniaEdit `TextEditor` like the code window's, with
`CaretPlacement.Place` and the same rules, and read it back in the note. If a carried file must be treated differently,
open it and say so, rather than refuse and open nothing. [#2403](https://github.com/hexide-io/HexIDE/issues/2403).

**Fixed (#2403).** `open_file` places the caret in a carried file as in a code window. It checks the place against the
text `get_file_content` returns, the editor's buffer or else the file on disk, and refuses one off the text before
anything opens. It then opens the file, places the caret with `CaretPlacement.Place`, which now takes any
`ISearchableDocument`, and reads it back in the note. The carried-file view gained what #700's review gave the code
window's: a fold the caret lands at the end of is opened, which AvaloniaEdit does not do for itself, and the editor
scrolls to it. It also takes a caret placed before it was laid out, as open_file places one in a file it has just
opened; the editor's own caret, at 0 when its document arrived, used to overwrite it. Guarded by
`CaretPlacementTests` and `OpenFileCaretTests`.

**Live (2026-10-27, Windows at 150%, a Debug build of the fix, headless `claude -p` through the hexide tools).** A
project carried `notes.md`, 80 lines, `line NN of the notes` from line 2.

- `open_file {"name":"notes.md","line":99}`: `Nothing was opened: in notes.md, line 99 is not one of its lines,
  numbered 1 to 81`, and `get_document_tabs` then listed no tab.
- `open_file {"name":"notes.md","line":70,"column":6}`: `… The caret is at line 70, column 6: offset 1510 in the text
  get_file_content returns.` The snapshot, read by eye, shows lines 59 to 81 with the caret just before `70` on line 70:
  the editor scrolled there.
- `type_text` `XY` in keyboard mode on the editor's TextArea, then `get_file_content`: line 70 read `line XY70 of the
  notes`.
- `open_file` at line 2, column 1 on the file already open: `The caret is at line 2, column 1: offset 9 …`.

---

## No tool could see a running form's frame — **CLOSED** (#1595, #2182, 2026-10-27)

**Symptom (2026-10-23, #1575, Windows, Debug).** A change to a running form's window, its border, caption bar and
buttons, could not be checked through the tools. `take_snapshot(window: "B3x1")` on a running Fixed Dialog returned
a 252 × 150 image at scale 1.5: the form's 168 × 100 client area, with no caption bar, border or buttons.
`inspect_element(target: "Window", window: "B3x1")` returned `boundingRect [0,0,168,100]`, `providers []` and
`dataContextMembers []`, and nothing about whether the window can be resized, its decorations, its buttons or whether
it is a tool window.

**Workaround.** From outside the IDE: `GetWindowLong` with `GWL_STYLE` and `GWL_EXSTYLE` through a PowerShell
P/Invoke on the windows HexIDE's process owns, and a screen capture of their rectangles. Both bypass the surface a
real caller uses.

**Implemented; Windows live verification pending (#1595).** `inspect_element` on a Window reports the
Avalonia frame and now reads `nativeStyle` / `nativeExStyle` as unsigned 32-bit Win32 bit masks from its HWND.
`nativeStyleNote` explains when those reads are unavailable, including on headless and non-Windows hosts.
`take_snapshot(window: "B3x1", includeFrame: true)` captures the window with its frame, cropped to the DWM frame
rectangle, with client-origin offsets for mapping inspected bounds. Since #2182 Windows renders it (`PrintWindow` with
`PW_RENDERFULLCONTENT`) rather than the screen being copied, so a window covering it is not in the image, and open
popups are drawn over it as the default capture draws them. Should Windows render nothing, or only black, the frame
rectangle is copied from the screen, and the note says how much of it other windows cover. It refuses a window with
no HWND, or hidden or minimized, rather than silently returning a client-only image; a copy from the screen is also
refused for a window partly off the desktop. The default client/popup render is unchanged.

**Required live check.** On Windows at 150% scaling, inspect a running Fixed Dialog and tool window, compare
both style masks with `GetWindowLong`, and capture each with `includeFrame: true` (also with `inline: true`).
Confirm caption, border and buttons, client-origin coordinate mapping, an overlapping window, and the explicit
refusal for a minimized or partly off-screen window. Keep this entry open until those checks pass.
[#1595](https://github.com/hexide-io/HexIDE/issues/1595).

**Measured (2026-10-26, Windows 11 at 150%, a Debug build of this branch merged with main at c208b2d7, headless
`claude -p` through the hexide tools).** A running form with BorderStyle 3 (Fixed Dialog) and the caption "Fixed":

- `inspect_element {"target":"Window","window":"Fixed"}` gave `"frame":{"canResize":false,"windowDecorations":"Full",
  "canMinimize":false,"canMaximize":false,"showInTaskbar":true,"windowState":"Normal","nativeStyle":382206080,
  "nativeExStyle":2359553}`. Both masks equal `GetWindowLongPtr`'s `GWL_STYLE` and `GWL_EXSTYLE`, read on the same
  window: caption, system menu, no sizing frame and no minimize or maximize box, with a modal dialog frame.
- `take_snapshot {"window":"Fixed","includeFrame":true}` gave `"width":454,"height":347,"scale":1.5,"windowOffsetX":2,
  "windowOffsetY":45` and the note. With the form in front, the image is the form: a caption bar reading "Fixed",
  the border, a close button only, and the client area at (2, 45), so `boundingRect` [0,0,300,200] maps to
  (2, 45)-(452, 345) as the description says. With `"inline":true`, the inline copy is the same, at 454×347 and
  `inlineScale` 1.5. Without `includeFrame` the image is the 450×300 client area, with offsets 0.
- **With another window over the form, the image is that window.** The IDE was started in the background, so the
  running form opened behind another application's window, and the same call returned that window's pixels. The reply
  was identical to the unobscured one, note included, so nothing in it says the capture missed. No tool brings a
  running form to the front, so the note's advice can't be followed through the tools. Filed as #2182.
- **Not checked:** a tool window, and the refusal for a minimized or partly off-screen window. `set_window_state`
  acts on the IDE's main window only, and nothing else minimizes or moves a running form.

**Measured again with #2182's fix (2026-10-27, Windows at 150%, a Debug build of this branch merged with main at
27e5bd06, headless `claude -p` through the hexide tools).** The running form was a Fixed Dialog captioned "Fixed" with
a Toolbar. A test harness outside the IDE covered it with a topmost magenta window; `WindowFromPoint` at the form's
centre returned the harness window.

- `inspect_element {"target":"Window","window":"Form1"}` gave the same `nativeStyle` 382206080 and `nativeExStyle`
  2359553 as before, equal to `GetWindowLong`'s `GWL_STYLE` and `GWL_EXSTYLE` read by the harness.
- `take_snapshot {"window":"Form1","includeFrame":true}` with the form covered gave `"width":604,"height":497,
  "scale":1.5,"windowOffsetX":2,"windowOffsetY":45` and the frame `captureArea`. The image is the form: its caption bar
  reading "Fixed", the close button, the Toolbar and the client area, with no magenta pixel in it.
- With the Toolbar's dropdown open, the same call drew its menu, item First, over the form, still covered.
- Without `includeFrame` the image is the 600×450 client area, offsets 0, also with no magenta.
- With the harness then moving the form to x 1800 on a desktop 1920 wide, so all but 120 pixels of it were off the
  right edge, the call returned the whole 604×497 window: no part black or missing.
- **One divergence from the screen.** The 1-pixel border down the sides and along the bottom comes out black, about
  3,000 pixels of the 604×497 image, where the screen draws it in the border colour. Windows 11 draws that border
  outside the window's own pixels.

**The last two checks (2026-10-27, Windows at 150%, a Debug build of this branch merged with main at 8048301e,
headless `claude -p` through the hexide tools).**

- **Tool windows.** Two running forms, Form1 a Fixed ToolWindow (BorderStyle 4) and Form2 a Sizable ToolWindow (5).
  - `inspect_element {"target":"Window","window":"Form1"}` gave `"nativeStyle":382205952,"nativeExStyle":2097536`, and
    Form2's gave `382664704` and `2097536`. A harness's `GetWindowLongPtr` read the same four values, with
    `WS_EX_TOOLWINDOW` set on both.
  - `take_snapshot {"window":"Form1","includeFrame":true}` gave a 304×197 image with `"windowOffsetX":2,"windowOffsetY":45`.
    It shows the caption reading "Tool fixed", a close button only, the border, and the client area. Form2's is the same
    with its own caption. With `"inline":true` the inline copies are the same size, at `inlineScale` 1.5.
  - Windows' own metrics agree, read without the screen: the client origin sits at (2, 45) inside the DWM frame, and at
    144 dpi `SM_CYCAPTION` and `SM_CYSMCAPTION` are both 34, so a tool window's 45-pixel caption band is
    34 + `SM_CYFRAME` 5 + `SM_CXPADDEDBORDER` 6. The session was locked, so no screen copy could be compared; that the
    frame captures still rendered shows #2182's route needs no unlocked screen.
- **A minimized window.** `set_window_state {"state":"Minimized","window":"Form1"}`, which reaches a running form since
  #2357, then `take_snapshot {"window":"Form1","includeFrame":true}`, was refused: `The window is hidden or minimized.
  Show or restore it, then retry includeFrame true.` That reply also carried the client `captureArea`, describing an
  image it never took. Both are fixed here: a refusal carries no `captureArea`, and the refusal names the way back: `The
  window is minimized, so Windows draws no frame for it. Restore it with set_window_state, state Normal and its name as
  'window', then retry includeFrame true.` A hidden window gets a refusal of its own. `FrameCoverTests` pins both.
- **Partly off the desktop, through the tools.** `set_window_state {"state":"Normal","window":"Form1","x":1800,"y":200}`
  on a 1920-wide desktop, then the frame capture, returned the whole 454×347 window, nothing black or missing, with no
  note. That is the harness check above, now made through the surface.

**Found on the way.** The default capture of a minimized window returned the last frame it drew before minimizing,
with the reply of a fresh capture: #2416, in the live document.

## An array argument from a client with a stale schema was refused as "not a long string", with no hint why — **CLOSED** (#2446, 2026-10-28)

**Symptom (2026-10-27, hexide-frm, driving the CommonDialog's two-file Open after #2395 and #2426).** The session had
loaded `answer_next_file_dialog` before #2426 added `paths`, and a client keeps a tool's schema once loaded. With no
`paths` in its copy, it sent the array the caller wrote as a JSON string, and the server refused it: `'paths' must be
array, not a long string. Its input schema names each parameter and its type.` That pointed the caller at the schema,
which was the stale thing, and told it nothing of what had arrived. A model on first contact would retry the same call,
or decide the tool was broken.

**Fixed (#2446).** A string argument that holds, as JSON, the array or object its parameter declares is refused
naming the cause, and the refusal's close says that a retry from the same session sends the same text and a new
session loads the current schema. The value is not taken: that would guess at what the client meant. A long string in
any refusal is shown by its length and start, not as "a long string". Guarded by `McpRestartTests`, which call a real
server over its transport.

**Live, reproducing the cause (2026-10-28, Windows, headless `claude -p` through the hexide tools).** The session
loaded `answer_next_file_dialog` from a batch-85 build, whose schema lists `path` alone, and used it. The IDE was then
swapped for a build of this fix, in the same session, and the call made with `paths` written as an array:

```
'answer_next_file_dialog' refused its arguments, and nothing was done: 'paths' must be array, and arrived as a string
holding one, a string of 43 characters beginning "["C:\\nowhere\\a.txt","C"…: what a client sends while it holds an
older schema of this tool, from before this parameter took an array. Retrying in this session sends the same text: its
client keeps the schema it loaded, which this server has since changed. A new session loads the current schema and
sends the value as it was written.
```

The middle of that reply has since been tidied to `arrived as a string of 43 characters holding one, beginning …`; the
test pins the new wording.

---

## `interact`'s `click` pressed without moving the pointer there, so a program's MouseMove never ran from one — **CLOSED** (#2407, 2026-10-27)

**Symptom (2026-10-27, hexide-frm's TreeView recheck, on #2353).** `click` raised a press and a release at the point,
and no move first, so a running form's MouseMove never fired from a click. A person's click always arrives with the
pointer having moved onto the control. A live check of a control's event order, MouseMove, MouseDown, MouseUp,
Click, couldn't be made with `click` alone, and the recheck had to `hover` at the same points to get MouseMove at all.

**Fixed (#2407).** `click` raises a `PointerMoved` on the element it presses, at the point, before the press, on its own
pointer. That is the route a running form's MouseMove is raised from. A click where the last click left the pointer in
that window moves nothing first, as a still pointer sends no move; a hover's end takes the pointer out of the window,
so the next click arrives again. A double click's second press already moved nothing. The reply ends `the pointer moved
there first, as a person's does` or `the pointer was already there from the last click, so it did not move first`.
Guarded by `InteractClickTests` and `MouseEventTests`. The oracle's mouse probes logged no MouseMove, so whether VB6
raises others around a click is unmeasured: #2412.

**Live, against a real pointer (2026-10-27, a Debug build of the fix, headless `claude -p` through the hexide tools).**
A twips form logged each mouse event into a label: `cMM`, `cMD`, `cMU` and `cC` for Command1's MouseMove, MouseDown,
MouseUp and Click, and `fMM`, `fMD`, `fMU` and `fC` for the form's.

| Input | Windows, through `click` | Linux under WSLg, through `click` | Linux, real X input from `xdotool` |
|---|---|---|---|
| click on Command1 | `cMM cMD cC cMU` | `cMM cMD cC cMU` | `cMM cMD cC cMU` |
| click on the form at (333, 200) | `fMM fMD fMU fC` | `fMM fMD fMU fC` | `fMM fMD fMU fC` |
| the same click again | `fMD fMU fC` | `fMD fMU fC` | `fMD fMU fC` (and `cMD cC cMU` on Command1) |

- **The real pointer.** `xdotool mousemove` then `click 1`, so the X server's own input reached Avalonia's X11
  backend. With the form not yet active, the first real click on Command1 gave `cMM` alone, as the window manager took
  the press to activate the form; with it active, it gave the row above.
- **Windows.** The real-pointer column couldn't be measured there: the session was locked, and posted mouse messages
  don't reliably reach Avalonia's input on this host, as an earlier entry found. Three posted clicks left one
  `fMM fMD fMU fC`, which says nothing either way.

## `answer_next_file_dialog` armed one path, so a multi-select Open could not be answered with several files — **CLOSED** (#2408, 2026-10-27)

**Symptom (2026-10-27, hexide-frm's live check of #2395, the CommonDialog).** A ShowOpen with AllowMultiselect and
Explorer (`Flags = &H80200`) had to return two files, as in the oracle's row where `"a.txt" "b.txt"` is typed and
FileName comes back as the folder, Chr(0), `a.txt`, Chr(0), `b.txt`. No call could give that answer.
`answer_next_file_dialog(path: "<folder>\a.txt")` armed one path, which the Open picker returned as a one-element
list. Calling it twice armed two answers, each spent by its own dialog, so the second went to the next picker rather
than to the same pick. Only a person or the headless tests could reach a program's multi-file result. The IDE's own
Add File, which allows several, had the same limit.

**Fixed (#2408).** `answer_next_file_dialog` takes `paths`, every file of one answer in order, beside `path`; the reply
lists them in `paths`. Only an Open that allows several takes them all. A save, or an Open that takes one file, answers
the answer as cancelled, not with its first file, which no person could have chosen there; the answer is spent all the
same, so it cannot redirect a later dialog. The save's own reply fails and says so. Where nothing replies for the
dialog, as with a single-select Open from a menu or a running program, the next `answer_next_file_dialog` or
`clear_file_dialog_answers` reply does, once. Both `path` and `paths`, an empty `paths` or a blank entry is refused, with
nothing armed. Guarded by `ScriptedFileDialogsTests` and `AnswerNextFileDialogPathsTests`. A program's multi-file
FileName is #2395's to format; this gives it the files.

**Live (2026-10-27, a Debug build of the fix, headless `claude -p` through the hexide tools, a new Standard EXE).**

- **Add File with two files.** `answer_next_file_dialog {"paths":["…\\Alpha.bas","…\\Beta.bas"]}`, then
  `invoke_menu_item {"path":"Project/Add File"}`: `get_project_info` listed `"modules":["Alpha","Beta"]`, and
  `clear_file_dialog_answers` then said `Nothing was armed.`
- **A save.** `add_file` a module Gamma, arm two paths, then `set_file_content` Gamma: `{"success":false,"error":"The
  new code is in the IDE. Module 'Gamma' still has no file: the save picker answered as cancelled, because the armed
  answer named several files and it takes one (answer_next_file_dialog's paths is for an Open that allows several), so
  nothing was written. …"}`, re-checked against the final build. The `clear_file_dialog_answers` after it said
  `Nothing was armed.`: the save took the answer, and its reply had already reported the refusal.
- **An Open that takes one file, from a menu.** Arm two paths, then `invoke_menu_item {"path":"Run/Start External
  Debug Session"}`, which replied only `Invoked`. The next `answer_next_file_dialog {}` said `The next file dialog will
  answer as cancelled. Earlier, an Open picker that takes one file took an answer of 2 paths, which it cannot take, so
  it answered as cancelled.`
- **Refused at arming.** `path` and `paths` together: `Pass path for one file or paths for several, not both.`; `paths`
  empty: `paths must name at least one file, and none of them blank. …`; both with `Nothing was armed.`

## `press_key` and `type_text` pressed a modified key without the modifier's own KeyDown and KeyUp — **CLOSED** (#2422, 2026-10-27)

**Symptom (2026-10-27, hexide-frm's live check of #2418, the TextBox key events).** `press_key` with `modifiers`, and
`type_text` in keyboard mode, sent the key with its modifiers as flags and never the modifier's own key. A person's
Shift+K is Shift down, K down, K up, Shift up. The oracle's TextBox key table measures that pair, Shift 16, Ctrl 17
and Alt 18 with mask 1, 2 or 4 while held and mask 0 on their own release, and none of it could be driven:
`press_key(key: "A", modifiers: "Ctrl")`, `press_key(key: "X", modifiers: "Alt")` and
`type_text(text: "K", mode: "keyboard")` each raised the main key's events alone. The keyboard twin of #2407.

**Fixed (#2422).** Each modifier's own key goes down before the key, in the order Ctrl, Shift, Alt, Meta, holding
what is down so far, and up after it in reverse, holding what is still down. The window input, the routed fallback
and `type_text`'s keyboard mode all send the same chord. A modifier pressed alone reports itself held going down and
not coming up, as Windows reports it. The replies name the keys: `pressed Shift+K; LeftShift went down before K and
up after it, as a person holds it`, and `type_text`'s `LeftShift went down before the capital and up after it`.
Guarded by `ModifierKeyChordTests`. A press of a key with nothing typed, such as Shift+K through `press_key`, is
#2428.

**Live (2026-10-27, Windows, a Debug build of this branch merged locally with #2418, headless `claude -p` through the
hexide tools).** Text1's KeyDown, KeyPress and KeyUp each logged a token: `D<KeyCode>/<Shift>`, `P<KeyAscii>`,
`U<KeyCode>/<Shift>`.

| Call | Logged | The oracle |
|---|---|---|
| `press_key` K, `modifiers` Shift | `D16/1 D75/1 U75/1 U16/0` | the same, with KeyPress 75 before the KeyUp: #2428 |
| `press_key` A, `modifiers` Ctrl | `D17/2 D65/2 P1 U65/2 U17/0` | KeyCode 65, KeyAscii 1 |
| `press_key` X, `modifiers` Alt | `D18/4 D88/4 U88/4 U18/0` | KeyCode 88, no KeyPress |
| `press_key` LeftShift | `D16/1 U16/0` | Shift 16, mask 1 held and 0 on release |
| `type_text` "Kk", keyboard mode | `D16/1 D75/1 P75 U75/1 U16/0 D75/0 P107 U75/0` | K and k both 75, KeyAscii 75 and 107 |

---

## `set_window_state` reached only the IDE's own window — **CLOSED** (#2357, 2026-10-27)

**Symptom.** `set_window_state` and `get_window_state` took no `window` and acted on the IDE's main window only, and no
tool minimized, restored, moved or resized any other window. A running form's `Form_Resize`, a `WindowState` read in a
Timer, and a layout that depends on the form's size couldn't be driven through the tools, and neither could a tool's own
handling of a minimized window. Found twice. #1925's live check couldn't try `includeFrame`'s refusal for a minimized
form. #2182's needed a form moved partly off the desktop and did it with `SetWindowPos` from a harness, which bypasses
the surface being checked.

**Fixed (#2357).** Both tools take the tree tools' `window`, and act on the IDE's own window when it is omitted, as
before. The reply's `name` is what reaches the window again: `ide`, a running form's name, or a window's title.

- **As a person does it.** Another window changes through the same Avalonia properties its caption buttons and frame
  set, so a running form's Resize and Activate run as they do for a click. The reply waits for the program, as
  `interact`'s does, then reads the window again.
- **What a person couldn't do is refused**, with nothing changed:
  - a state the window has no box for (a form's MinButton, MaxButton, ControlBox and BorderStyle decide);
  - a size for a fixed border, where `x` and `y` alone still move it;
  - any change to a disabled window or one under a modal dialog, through the refusal `interact` uses.
- **An unknown name** is refused, listing the open windows. The message says the IDE's window is the default here, where
  the tree tools' says `auto` is.

Guarded by `WindowStateOtherWindowTests` and `ForegroundWindowTests`.

**Live (2026-10-27, Windows at 150%, a Debug build of the fix, headless `claude -p` through the hexide tools).** Form1
(Sizable) counted its Resize and Activate into a label. Form2 was a Fixed Dialog with no minimize or maximize box.

- **Omitted `window`.** `get_window_state {}` gave `{"state":"Normal","x":-11,"y":-11,"width":1280,"height":649,"name":"ide"}`,
  as before, apart from `name`.
- **Form1, from `resize 1, activate 1`.**
  - Minimize answered `"state":"Minimized","x":-32000,"y":-32000`.
  - Restore: `resize 3, activate 2, state 0, width 4500`.
  - Maximize: `resize 6, activate 2, state 2, width 19200`.
  - Normal at `x 200, y 150, width 500, height 300`: `resize 11, activate 2, state 0, width 7500`, at the position and
    size asked for.
- **Form2.**
  - Minimize was refused: `'Form2' has no minimize box, so a person can't minimize it. …`.
  - `width: 400` was refused: `'Form2' has a fixed border, so a person can't resize it; …`.
  - `x: 700, y: 200` moved it.
- **Form1 under the program's MsgBox.** Minimize was refused: `'Form1' is in a window a modal dialog has disabled:
  'WinState' (a message box: "Hold", OK). …`. Once the MsgBox was answered, the same call minimized the form, a second
  one found it minimized by name, and Normal restored it. The first run named the form by its class, `VBFormRuntime`,
  which is fixed.
- **An unknown name.** `get_window_state {"window":"Nope"}` answered `Unknown window 'Nope'. Use "ide" (the default
  here: …), "auto" (…), or the name or title of an open window: Form2 ("Fixed"), Form1 ("Resize probe"). …`.
- **The same changes from a person's caption buttons**, sent as the `WM_SYSCOMMAND` a click sends, gave the same
  Resize counts as the tool's:
  - maximize and restore from maximized, three each;
  - minimize and restore from minimized, one each, with one Activate on the restore.

  Three for a maximize is the runtime's own behaviour whichever route it comes by: #2389.

## The code window's completion description: desktop snapshot composition needed a live check — **CLOSED** (#1334, 2026-10-27)

**Original symptom.** Recorded when #942 closed (#1247), from the code and headless tests; no call was recorded.
The description tooltip beside a completion item was absent from `dump_visual_tree` and `take_snapshot`, and
`interact` `double_click` selected an item without committing it. [#1334](https://github.com/hexide-io/HexIDE/issues/1334).

**Fixed in code, with headless regression tests (#1678).** The dump reaches the open description popup through
`AttachedPopupsOf`, under the completion list, with its text readable and its path resolvable. `double_click`
selects the target and commits it through the list's own commit path. The snapshot composer discovers the
description's top-level root by the same route and composes it after the list, which headless rendering can't prove:
a headless popup renders in the window's own overlay.

**Why the check took a foreign server.** The bundled VB6 server sends no `detail` on any completion item, so with it
alone no item has a description, and the tooltip never shows. The carried-file editor asks no server for completion
(#266), so a C file carried by the project couldn't stand in either. clangd attached to `.bas` beside the bundled server
can, once it parses the module: its first attempt answered `twice` as `"kind":1,"deprecated":true` with no `detail`,
clangd's fallback for a file it can't parse, because `Attribute VB_Name = "Module1"` isn't C. A `.clangd` beside the
probe adding `-xc` and `-DAttribute=char*` made that line a declaration missing its semicolon, which clang recovers from.

**Live (2026-10-27, Windows at 150%, a Debug build of main at 8ae02d39 plus #2401, which touches neither path; clangd
22.1.6 from the foreign-server cache, attached for `.bas` in the session's own profile; headless `claude -p` through
the hexide tools, with `--capture-lsp`).** Module1 read `int twice(int x);` and `int main(void) { return tw; }`.

- `open_file {"name":"Module1","line":3,"column":27}` put the caret after `tw`, and `press_key` Ctrl+Space on the
  editor opened the list: clangd's item first, then the bundled server's keywords.
- `get_lsp_message` on clangd's completion response gave `{"detail":"int",…,"kind":3,"label":" twice(int x)",…}`.
- `interact` `select` on `…/List[PART_ListBox]/Pane[PART_ScrollViewer]/ListItem[twice(int x)]` answered
  `selected ' twice(int x)'`. `dump_visual_tree` rooted at the list then listed `…/List[PART_ListBox]/Text[int]`, a
  `TextBlock` named `int`, beside the list's scroll viewer.
- `take_snapshot {"window":"ide"}` drew the tooltip: a bordered box reading `int`, just right of the list, level with
  the selected row, whole, with the list unclipped beside it. Checked by eye on the image as well as by the caller.

**Found on the way.** `open_file` with a `line` refuses a carried file and opens nothing: #2403, in the live document.

---

## `hover` couldn't say that the real mouse, resting over the window, took the tip — **CLOSED** (#2376, 2026-10-27)

**Symptom (2026-10-27, Windows, #1303's live check).** With the real cursor resting over the IDE's window, a second
`hover` on `…/Button[Standard.AddForm]` answered `no tip opened within 1500ms (its ToolTip.ShowDelay may be longer than
the watch, or a real pointer moved over the window and took the tip); declared tip: Add Form`, though the first had
reported the tip. A first Auto Data Tip in break mode missed the same way. With the cursor on the taskbar, nine hovers
running each reported their tip.

**Cause.** Windows sends the window under a resting cursor a real mouse move when windows change around it, a tip's own
popup among them. Avalonia's pointer goes back under the real cursor, and its tooltip service moves to whatever is there,
as it does for a person. The reply offered that as one of two causes and couldn't say which; with no tip declared, as for
quick info and Auto Data Tips, it offered none.

**Workaround.** Move the real mouse off the window before hovering, which no tool can do.

**Fixed (#2376).** While `hover` watches for a tip, `UiAutomationDriver.ForeignPointerWatch` reads the input manager's
stream, which is what Avalonia's tooltip service follows, for pointer input the hover didn't send. Routed pointer events
that `click` and `drag` raise on a control can't move a tip, never reach that stream, and aren't counted. Tool calls run
concurrently, so the input is told apart by its device: every hover sends through one device of its own, and anything
else is the platform's. When no tip opened and such input came, the reply says which, declared tip or not:

- the real mouse: `the real mouse moved the pointer to (x, y) in the window, over …, during the watch, and the tip went
  with it, as it would for a person: one resting over the window is sent such moves as windows change around it. Move it
  off the window and hover again`;
- another hover moving elsewhere, or ending: `another hover moved the pointer to (x, y) …` or `another hover ended during
  the watch, …`, each ending `Hover one control at a time`.

It names the control whose tip a person would now see, else the nearest named one. When nothing came, the ShowDelay
explanation stands alone. `HoverThroughWindowInputTests` covers the hover's own move and a routed move, which aren't named,
another hover's move and its end, and the headless platform's mouse standing in for the real one. Removing the watch, the
device check or the hovered-point filter each fails it.

**Live (2026-10-27, Windows at 150%, a Debug build of the fix, headless `claude -p` through the hexide tools).** A harness
held the real cursor still and logged it every half second.

- **Cursor over the IDE**, at (100, 540) on the screen. Of eleven hovers, five on Add Form, three on Add Project and three
  of quick info on `count`, six reported their tip. Each of the five that didn't said `the real mouse moved the pointer to
  (66.7, 337.3) in the window`, the cursor's point at 150% in window units. The quick-info miss, `no tip opened within
  3000ms (the real mouse moved the pointer to (66.7, 337.3) in the window, over
  ScrollContentPresenter[PART_ContentPresenter], …)`, would before this have said only that no tip opened.
- **Cursor parked on the taskbar.** Disabled End and Break gave `…; no tip opened within 1500ms; declared tip: End` (and
  `Break`), with no note: nothing is claimed when nothing came. Three Add Form hovers, then three pairs of Add Form and Add
  Project sent as parallel calls, each reported its tip. The pairs didn't overlap enough to interfere, so the other-hover
  wording is covered headless only.

---

## 7. `hover`'s move through the window's input was measured headless only — **CLOSED** (#1303, 2026-10-27)

**The gap (narrowed 2026-10-21).** `hover` sends its move through the window's own input, so a declared `ToolTip.Tip`
opens (#1265). That was shown headless only. Not yet run against the IDE: that a toolbar button reports `tip: Add Form`;
where the tip is placed, now that the move updates the pointer position Avalonia tracks; that quick info and Auto Data
Tips still open on Windows and Linux, the same identifier twice in a row; that a real mouse behaves normally after a
hover ends; and that a disabled toolbar button says it is disabled.

**Placement needed a change to measure (#1303).** A tip closes when the hover ends, before any snapshot could show it,
so "a snapshot taken during the watch" could never be taken. `hover` now reads where an open tip is while it is open:
`tipBounds` gives x, y, width and height in the window's device-independent units, as `inspect_element`'s `boundingRect`
does, and `detail` gives its offset from the point hovered. `TipPlacementTests` covers both.

**Measured (2026-10-27, headless `claude -p` through the hexide tools, on a Debug build of #1303's branch).** On Windows
at 150%, and on Linux under WSLg (Ubuntu 26.04, X11). The project's Form1 held `Dim count As Integer`, `count = 42`,
`Stop`.

- **Toolbar tip.** `hover` on `…/Button[Standard.AddForm]`, whose `boundingRect` is `[23.3, 26, 22, 22]`, answered
  `hovered (11, 11) through the window's input; tip: Add Form (its top-left 0.3 left of and 19.7 below the point
  hovered)` with `"tipBounds":[34,56.7,63.3,22.7]` on Windows. On Linux the offset was 0 right and 20 below, with
  `[34,54,68,20]`. Each was the same three times running.
- **Placement.** The tip opens about 20 below the point hovered, as Avalonia places a tip at the pointer, not at the
  real pointer's last position, as the old route did (the archived gap "7. A declarative `ToolTip.Tip` cannot be made to
  open").
  - A test harness then moved the real mouse to the same point. A screen capture showed its tip at about (34.7, 58.7)
    in the window, where `tipBounds` put it.
  - Quick info at the caret on `count` gave `tip: count As Integer (its top-left 0.5 left of and 19.4 below the point
    hovered)`, `[108.7,224,98.7,22.7]`.
- **Quick info and Auto Data Tips, on both systems.** Each was hovered three times running: quick info in design mode
  (`count As Integer`), and Auto Data Tips paused at `Stop` (`count = 42`). All twelve reported their tip.
  - With the real cursor resting over the IDE's window, a second toolbar hover and a first Auto Data Tip reported no
    tip. Windows' real mouse moves put Avalonia's pointer back under the cursor, and the tip went with it, as it does
    for a person.
  - The reply can't say that was the cause: #2376.
- **The real mouse afterwards.** A harness moved the real cursor onto Add Form, then Add Project, then over the
  designer. Screen captures showed each button's tip and hover highlight, and nothing left behind once the cursor
  moved away.
- **A disabled toolbar button.** `hover` on `Standard.End`, and on `Standard.Break`, in design mode answered `hovered
  (11, 11) through the window's input; the target is disabled, so it takes no pointer input and shows a tip only when
  it sets ToolTip.ShowOnDisabled; no tip opened within 1500ms; declared tip: End` (and `Break`).

**Also found.** On Linux, the project named on the command line by its absolute path wasn't opened, because a
positional argument starting with `/` is taken for a flag: #2375.

---

## `set_tool_window_visible` hid every tool tabbed with the one it was asked to hide — **CLOSED** (#2362, 2026-10-27)

**Symptom (2026-10-27, Windows, #2351's live check).** With AI Chat and the Toolbox tabbed together in the left dock,
`set_tool_window_visible {"name":"AI Chat","visible":false}` answered `{"success":true,"note":"AI Chat is now hidden."}`,
and `get_tool_windows {}` then gave both `"Toolbox"` and `"AI Chat"` as `visible: false`. A person closing AI Chat's tab
closes only AI Chat.

**Workaround.** Close the tool with its own tab's close button through `interact`, or hide only tools alone in their dock.

**Cause.** `MainViewViewModel.SetToolWindowVisible` closed the tool's parent dock, on the assumption that a dock holds
exactly one tool. Since #2239 and #2277 a drag can put several tools in one dock as tabs. An add-in tool window
unregistering closed its parent dock the same way.

**Fixed (#2362).** Both close the tool itself, as its tab's close button does; its dock goes only when it is left
empty. The reply names the window as `get_tool_windows` does, says when it was already in the state asked for, and
names any other tool window that opened or closed with it. Two tests in `MainViewViewModelTests` tab Properties
with the Toolbox, hide Properties and show it again; both fail with the dock closed instead.

**Live (2026-10-27, Windows, a Debug build of the fix, headless `claude -p` through the hexide tools).** The session's
saved layout put the Toolbox and Properties on the left, which builds them as two tabs of one dock; `dump_visual_tree`
showed `…/TitleBar[Toolbox]/Pane[Toolbox]/Tab[PART_TabStrip]/…/TabItem[Properties]`.

- `set_tool_window_visible {"name":"Properties","visible":false}` answered `{"success":true,"note":"Properties is now
  hidden."}`. `get_tool_windows` then gave Toolbox `visible: true` and Properties `visible: false`, and the snapshot
  showed the Toolbox filling the left panel with no tab strip.
- `{"name":"Properties Window","visible":true}` answered `Properties is now shown.`, and both were visible again.
- The same call with `"Properties"` answered `Properties is now shown, as it was already.`

## `press_key` reported success on a disabled control — **CLOSED** (#2366, 2026-10-27)

**Symptom (2026-10-27, Windows, #623's measurement on main at 759df134).** Options → Language's region combo is
disabled while the language combo shows the system default.

- `inspect_element {"target":"Window/Custom/Custom/ComboBox[#1]","window":"Options"}` gave `"isEnabled": false` and
  `"isKeyboardFocusable": true`.
- `press_key {"target":"Window/Custom/Custom/ComboBox[#1]","key":"Down","window":"Options"}` answered
  `{"success":true,"mechanism":"routed-key","detail":"pressed Down"}`.

`interact`'s `click` and `invoke` refuse the same control.

**Workaround.** Read `isEnabled` with `inspect_element` before pressing.

**Cause.** A disabled control can't take focus, so `PressKey` fell back to raising the key as a routed event on it, which
no person's keyboard could do, and reported that as success. `inspect_element` took `isKeyboardFocusable` from the
automation peer, which answers for `Focusable` alone.

**Fixed (#2366).** `press_key` refuses a target that isn't effectively enabled, with the refusal `interact` gives every
verb since #2223 (`DisabledInteractionRefusal`), which names a container that disables it. A modal dialog over the window was already refused, by `KeysTakenBy`.
`isKeyboardFocusable` is false for a control that isn't effectively enabled. `PressKeyDisabledTests` covers a disabled
combo, a disabled container and the focusable read; each fails with its half of the fix removed.

**Live (2026-10-27, Windows, a Debug build of the fix, headless `claude -p` through the hexide tools).** On the same
combo, `inspect_element` gave `"isEnabled": false` and `"isKeyboardFocusable": false`, and the same `press_key` answered
`{"success":false,"mechanism":"keyboard","error":"'ComboBox' is disabled (\"isEnabled\": false). No person could use
it, so 'press_key' was refused and nothing was done. Wait until it is enabled before trying again; inspect_element
reports its current state"}`.

---

## A running Toolbar's dropdown menu, open, was in neither the dump nor the snapshot — **CLOSED** (#2323, 2026-10-27)

**Symptom (2026-10-27, Windows at 150%, #2309's live check).** A click on a dropdown's arrow,
`Window/Custom[Toolbar1]/Button[#4]/Text[▾]`, opened the menu: the program raised ButtonDropDown. But `take_snapshot`
showed no menu and gave no note, and `dump_visual_tree` with `match: "First"` or `"MenuItem"` found nothing, in
`Form1`, `auto` or `ide`. The next click landed on a `LightDismissOverlayLayer`, and Down then Enter raised
ButtonMenuClick, so the menu was open. A caller who trusted the snapshot would conclude the dropdown never opened.

**Cause.** On a desktop a popup is a window of its own, so the dump and the snapshot reach a control's context menu or
flyout from the control that owns it (`AttachedPopupsOf`). `FlyoutsOf` read `Button.Flyout` for a Button and
`FlyoutBase.GetAttachedFlyout` only for other controls. `VBToolbar.Press` attaches its MenuFlyout to the dropdown's
Button with `SetAttachedFlyout` and opens it with `ShowAt`, so that route missed it.

**Workaround.** None to see it. Keys reach it blind.

**Fixed (#2323).** `FlyoutsOf` reads the attached flyout for every control, Buttons included, after a Button's own
Flyout and before a ContextFlyout. The snapshot composer asks the same question, so the menu is drawn as well as
listed. `LogicallyParentedPopupTests.An_open_flyout_attached_to_a_button_is_in_the_tree`, for a Button and a
ToggleButton, asserts the owner route. Headlessly a popup is an overlay inside the window, so the walk finds the items
either way, and the owner route is the part that fails with the old `FlyoutsOf`.

**Live (2026-10-27, a Debug build of the fix cherry-picked onto #2309's branch on Windows at 150%, headless `claude -p`
through the hexide tools).** The project was the Toolbar corpus form, with a ButtonMenuClick handler appending
` MC:<key>` to the caption.

- A click on the arrow opened the menu, and `take_snapshot` showed it under Menu ▾, with its one visible item, First.
- `match: "First"` found `Window/Custom[Toolbar1]/Button[#4]/Pane/MenuItem[First]`, offering `invoke`.
- Invoking it closed the menu and raised ButtonMenuClick: the caption became `Form1 MC:m1`.

## `invoke_menu_item` couldn't reach an item with a control header, or a submenu filled when it opens — **CLOSED** (#2321, 2026-10-27)

**Symptom (2026-10-27, Windows, #2140's live check).** #2140's Window → Panel Layouts submenu has both kinds of item.

- **A control header.** Each saved layout has a TextBlock header and an automation name. `invoke_menu_item
  {"path":"Window/Panel Layouts/Beta/Switch"}` answered `No item 'Beta' in menu 'Panel Layouts'. It holds: Save Layout
  As…, Full Editor, Avalonia.Controls.TextBlock, Avalonia.Controls.TextBlock`.
- **A submenu filled when it opens.** The submenu holds a placeholder until its SubmenuOpened handler fills it. In a
  fresh session, `"Window/Panel Layouts/Full Editor"` answered `It has no items.`, and the same call worked once
  something had opened the submenu.

**Workaround.** `interact` `expand` along the path, then `invoke` on the item by its tree path.

**Cause.** `MenuPath` read a header by its `ToString`, which for a control is its type's name. It also walked a
submenu's logical items without opening it, so a handler that fills the submenu when it opens had not run.

**Fixed (#2321).** A header that is a control is read by the item's automation name, which the dump shows it by, or
else its text. Each closed submenu on the path gets its SubmenuOpened before its items are read, as opening it does,
without a popup. `MenuPathTests` covers both, and each test fails with its half of the fix removed.

**Live (2026-10-27, a Debug build of the fix cherry-picked onto #2140's branch on Windows, headless `claude -p` through
the hexide tools).**

- In a fresh session, with no menu opened by hand, `Window/Panel Layouts/Full Editor` answered `Invoked 'Full Editor'.`
- After saving a layout named Beta, a miss in Panel Layouts listed `Save Layout As…, Full Editor, Beta`.
- `Window/Panel Layouts/Beta/Switch` answered `Invoked 'Switch'.`

## `hover` reported a tooltip that is a control by its type name — **CLOSED** (#2336, 2026-10-27)

**Symptom (2026-10-27, Windows, #2309's re-check).** `hover` on the designer's box for a carried control,
`…/List[ControlsContainer]/Pane[Toolbar1]`, answered `hovered (200, 22) through the window's input; tip:
Avalonia.Controls.StackPanel`. The box's tip is a StackPanel of three text blocks, the control's name, its class and
"Not supported in HexIDE" (`FormEditView.axaml`, `CarriedControlBox`). A person sees those; the reply read as an answer
and wasn't one.

**Cause.** `hover` read a tip by its `ToString`, for an open tip and a declared one alike, and a control's is its type's
name.

**Fixed (#2336).** `UiAutomationDriver.TipText` reads a string as it is, and a control by the text of its visible text
blocks, in order, joined with ` / `. Anything that gives only a type name is reported as having no text to read. The
fix's first live run answered `tip: a StackPanel with no text to read`: the tip counts as open a moment before its
blocks have bound their text, and the first poll caught it then. So a tip read with no text keeps `hover` polling, and
is reported only if no text comes before the dwell ends. `TipTextTests` covers the reading, and fails with control
tips read by `ToString` again. The poll is guarded only by the live run below.

**Live (2026-10-27, a Debug build of the fix on Windows, headless `claude -p` through the hexide tools).** The
project's Form1 held a Toolbar, which main carries. Three `hover`s on its designer box each answered `tip: Toolbar1 /
MSComctlLib.Toolbar / Not supported in HexIDE`.

---

## No way to turn a lock key on for a running program, so a StatusBar key panel's on state couldn't be checked — **CLOSED** (#2296, 2026-10-27)

**Symptom (2026-10-27, Windows, #2254's live check).** A StatusBar panel of Style 1 to 4 draws its Text black while
its key's toggle is on and grey while it's off. A live check could see the off state but not the on state. On Windows
the StatusBar reads the toggle the operating system keeps, and `press_key` raises Avalonia's key events without
changing it. On the test form, `press_key` targeting `Window` was also refused, since nothing there takes keyboard
focus.

**Workaround.** None live. The headless tests reached both states through an injected key reader.

**Fixed (#2296).** A new DEBUG-only tool, `set_lock_key {key, state}`. It sets CapsLock, NumLock, Insert or ScrollLock
`On` or `Off` as programs running in HexIDE read it, and `Keyboard` returns the key to the keyboard's own toggle. The
state lives in `LockKeys` in the runtime, beside the keyboard read it overrides. A StatusBar prefers it, and repaints
when it changes. Every reply lists all four keys, each with `on` and a `source` (`Set`, `Keyboard`, or `Window` where a
host has no toggle to read). The machine's keyboard is untouched. A new tool rather than a change to `press_key`: a
press can't set the OS toggle, and teaching the StatusBar to count automation's presses on top of the OS reading
would double-count a real one. Guarded by `SetLockKeyTests` and by
`StatusBarDrawingAndEventsTests.A_key_panel_follows_a_lock_key_set_by_automation`, whose four cases fail with the
override ignored.

**Live (2026-10-27, a Debug build of the fix on Windows at 150%, headless `claude -p` through the hexide tools).** The
form was the StatusBar corpus form with its third panel made an enabled Caps panel reading `CAPS`.

- With no arguments, the reply listed all four keys off, with source `Keyboard`, and changed nothing.
- The running form's `CAPS` was grey. After `{"key":"CapsLock","state":"On"}` it was black, with no other pixel
  of the window changed: 69 dark pixels in the text against none.
- After `Off` it was grey again, as it was after `Keyboard`. Both pictures were identical to the first.
- `Shift` and a key with no state were refused, each naming the accepted keys and states.

## A double click sent no second press when its first click replaced what it landed on — **CLOSED** (#2324, 2026-10-27)

**Symptom (2026-10-27, Windows, #2309's live check).** `interact` `click` with `double` on a running Toolbar's
`Button[New]` answered `…; the second click was not sent because the target is hidden or closed, or detached`. The
same happened on `Window/Custom[Toolbar1]` with `16,22 double`, though the bar is never replaced. So the Toolbar's
measured double-click couldn't be checked live: a second ButtonClick, and no DblClick (#1088).

**Cause.** Before the second press, `SecondPress` required the element the first press landed on to be still attached.
A Toolbar rebuilds its buttons on each release (`VBToolbar.Release` → `RefreshButtons`), so that element was always
gone.

**Workaround.** None. Two single clicks are not a double click.

**Fixed (#2324).** The second press hit-tests the first press's point again and goes to whatever takes the pointer
there, with a click count of 2, as a person's second press lands. It is refused only when the window is hidden or
closed, or nothing is at the point. When it reaches a different element from the first, the reply says so and names
it. The first live run of the fix still missed about one time in three: the second press found the bar's own presenter
though the new button was laid out, attached and composited. Hit-testing reads the composition the render thread
holds, which lagged the layout. So the second press now waits for the window's next frame and for the render thread to
process a composition batch, each bounded at 250 ms. `DoubleClickReplacedTests` guards the hit-test, and fails with the
old refusal back. The headless compositor doesn't lag, so the wait is guarded only by the live runs below.

**Live (2026-10-27, a Debug build of the fix cherry-picked onto #2309's branch on Windows at 150%, headless `claude -p`
through the hexide tools).** The project was the Toolbar corpus form, without its ImageList (#2309's branch predates
it), with handlers appending ` BC:<key>` for each ButtonClick and ` DC` for each DblClick to the form's caption.

- Without the frame and batch wait, three bar double-clicks in a row added one, two, then one ButtonClick. Diagnostics
  showed the misses' hit chain going straight to the bar's presenter.
- With them, six double-clicks on `Window/Custom[Toolbar1]` at `16,22` and two on `Button[New]` each added two
  ButtonClicks and no DblClick, as #1088 measured. Each reply said the second press went to the new button's
  `AccessText`, at `Window/Custom[Toolbar1]/Button[New]`.

---

## `inspect_element` reported `selectionItems: []` for a tree whatever was selected — **CLOSED** (#2288, 2026-10-27)

**Symptom (2026-10-27, Windows, #2287's live check).** After `interact select` on the Watches row `TreeItem[x]`
answered `selected 'x'`, `inspect_element` on its `Tree` answered `"selectionItems":[]`. It gave the same after
`remove_from_selection`, so a check that expected `[]` after clearing the row was checking nothing.

**Cause.** `Inspect` filled `selectionItems` from the peer's `ISelectionProvider`, or else from a
`SelectingItemsControl`. Avalonia's `TreeView` is neither: it derives from `ItemsControl`, and its peer offers no
selection provider. So neither branch ran, and the default empty array went out as the answer. All four of the IDE's
trees were affected: Watches, Locals, Project Explorer and the Options page tree.

**Workaround.** None in the tree itself. #2287's check used a consequence instead: Debug → Edit Watch opens its dialog
only while a row is selected.

**Fixed (#2288).** `Inspect` reads a tree's `SelectedItems` and names each row as `remove_from_selection` does: by
its container's label, falling back to `NameWithoutRow` for a row not realised. `InspectTreeSelectionTests` covers a
tree of rows and a bound tree named the way the IDE's trees are. Both fail with the new branch disabled.

**Live (2026-10-27, a Debug build of the fix on Windows, headless `claude -p` through the hexide tools).**

- **Project Explorer:** after selecting Form1's row, `selectionItems` was `["Form1 (Form1.frm)"]`.
- **Watches:** with a watch on `x`, `selectionItems` was `[]` before selecting, `["x"]` after `select`, and `[]` after
  `remove_from_selection`.
- **Options:** the page tree read `["General"]` on opening and `["Formatting"]` after selecting Editor → Formatting.

## `get_locals` failed with run-time error 393 on a form holding a Timer — **CLOSED** (#2289, 2026-10-27)

**Symptom (2026-10-27, Windows, #2281's live check).** Paused in any procedure of a form with a Timer, `get_locals {}`
answered `'get_locals' failed with an unhandled HexIDE.Runtime.Interpreter.VBRunTimeException: Run-time error '393': Get
not supported at runtime`. `get_locals {"maxDepth":1}` worked, because it doesn't expand `Me`. The Locals window
listed Timer1 under `Me` with no expander.

**Cause.** Not the tool's. The default depth of 3 expands `Me`, then Timer1, and a control expands to every property
`AvaloniaInteroperability.ReadProperties` can read. A Timer's Left and Top raise 393 when read, as VB6's do (#2069),
and the raise ended the whole read. The Locals window met the same throw when it asked for Timer1's children, and
showed no expander.

**Workaround.** `maxDepth` 1, which loses everything under `Me`.

**Fixed (#2289).** `ReadProperties` catches a raising read one property at a time. The Locals tree lists that property
with the raised error's own description in angle brackets and no type, and lists the rest as before. The angle brackets
are VB6's own convention for a listed member whose read raises (measured on #2289), though VB6 lists no Left or Top for a
Timer at all: which members Locals lists is #2329. Guarded by `ControlLocalsTests`, whose two new tests fail with the
catch removed.

**Live (2026-10-27, a Debug build of the fix on Windows, headless `claude -p` through the hexide tools).** The project
was a form holding Timer1, paused by a `Stop` in Form_Load.

- `get_locals {}` succeeded. Under `Me`, Timer1 listed 13 rows, Left and Top among them as
  `<Get not supported at runtime>` with an empty type, and Interval as `250`.
- In the Locals window, Timer1 had an expandCollapse provider, and expanding it showed the same rows.
- The same rows showed properties a VB6 Timer doesn't have, two of them reading `NaN`. That belongs to #801 and #2329, and
  is recorded on #801.

---

## A drag can't stop before its release, and the designer's preview and grab handles were not in the tree — **CLOSED** (#2231, 2026-10-27)

**Symptom (2026-10-26, Windows, #2216's live check).** `click` with `to` presses, raises its held moves and releases in
one call, so nothing that shows only while the button is down can be seen: the designer's draw preview
(`NewControlSpawnerPhantom`), the rubber band, a control's outline while it moves or resizes, Dock's drop targets, and a
running form's MouseMove with a button held. `dump_visual_tree {"window":"ide","match":"NewControlSpawner","interactiveOnly":false}`
answered `Nothing matches 'NewControlSpawner'.`, though a drag's reply named `Canvas[NewControlSpawner]`. On 2026-10-27,
#2232's live check found a selected control's grab handles missing too: `match "Resize"` found nothing, though a move
drag's reply named `'ResizeAdorner'` as the pointer capturer.

**Workaround.** Check only where a control lands. For a resize, press at a guessed point on the control's edge.

**Part 1, fixed (#2231).** The preview, its canvas, the rubber band, the resize adorner and its eight handles carry
AutomationIds, and the control view lists an adorner under the element it adorns. A handle is
`…/ListItem[Command1]/None[ResizeAdorner]/None[PART_BottomRight]`, and `inspect_element` reads the preview's bounds.

**Part 2, fixed (#2231).** `click … to x,y hold` stops after the held moves with the button still down, and the new
`release` action lets it go in a later call, at an optional point it moves to first, with an unheld drag's reply. While
it is held, every reply carries a `heldButton` note (`HeldButtonNote`, beside `OpenDialogNote`), `click`, `double_click`
and `hover` are refused, and keys still go. After 120 s with no call the hold is abandoned, as decided on the issue:
Escape, held moves back to the press, and a release there. The next reply says what the designer's undo stack recorded,
if anything. Measured headlessly (`HeldButtonTests`): an abandoned move and an abandoned Dock drag change nothing, and an
abandoned draw places a 2 by 2 control, `Add: Command1`.

**Live (2026-10-27, a Debug build of the fix on Windows, headless `claude -p` through the hexide tools).**

- With the Command tool on, `interact click` on `…/None[NewControlSpawner]` with `"20,20 to 100,60 hold"` replied that
  the left button was still held and that the canvas had the pointer capture, with a `heldButton` note. `inspect_element`
  on `…/None[NewControlSpawnerPhantom]` then had no `isHidden` and a `boundingRect` 80 by 40, and `take_snapshot` showed
  the preview, a solid 2-pixel outline, on the form.
- `get_project_info` carried the same `heldButton`. `click "5,5"` and `hover` were refused, naming the held button.
- `release` with `"140,90"` replied as an unheld drag does, released at (140, 90), with no `heldButton` after it.
- Holding `…/ListItem[Command1]/None[ResizeAdorner]/None[PART_BottomRight]` with `"4,4 to 44,34 hold"` grew Command1
  from 121 by 65 to 160 by 88 while held. With nothing called for two minutes, the next `get_project_info` said the hold
  was abandoned at 07:46:27 UTC: Escape went to the focused `ControlItem`, and it was let go where it was pressed.
  Command1 was then 120 by 64, not 121 by 65: a resize returns to the grid, and a drawn control is a pixel larger than
  its grid cells, so the note said `The designer recorded 'Resize: Command1' anyway, so it did not cancel; Undo takes it
  back.` The call after that had no `heldButton`, and `release` was refused, since nothing was held.

## `take_snapshot` left out text a control drew after popping a clip, above 100% scaling — **CLOSED** (#2293, 2026-10-27)

**Symptom (2026-10-27, Windows, 150% scaling, #2254's live check).** `take_snapshot` of the running form in
`corpus/inbox-controls/saved/statusbar` showed "Ready" in the StatusBar's first panel and nothing in its second, in a clean
reply. The compositor's own frame of the same window showed the date there too. So the live check reported a StatusBar
fault that wasn't there.

**Cause.** An Avalonia.Skia defect that only a `RenderTargetBitmap` at a DPI other than 96 meets. Its scale is kept apart
and multiplied into each transform set, `PopClip` rebuilds the current transform from the canvas matrix, which already
holds the scale, and so the first transform pushed after a popped clip gets it twice. Drawing text pushes one for the
text's origin, so above 100% the glyphs landed outside the bitmap. `SnapshotComposer` rendered at `96 * scaling` DPI, so
it met this whenever a control's own `Render` drew text after popping a clip, as the StatusBar does per panel.

**Workaround.** None in the tools. A diagnostic build saved the compositor's frame.

**Fixed (#2293).** `SnapshotComposer` draws the window and each popup with `Compositor.CreateCompositionVisualSnapshot`,
which is what the screen shows. A window in no frame yet, as one shown a moment ago is, is waited for. If the compositor
gives no picture within 5 s, a render target draws it, and the reply's `note` says so, since that picture can lack such
text. `SnapshotScalingTests` draws two clipped texts at 100%, 150% and 200%: the second is missing from a render target
at 150% and 200% (0 dark pixels against 142 in the compositor's image at 150%), and present in the snapshot.

**Live (2026-10-27, a Debug build of the fix on Windows at 150%, headless `claude -p` through the hexide tools).**

- Merged locally with #2254, which isn't on main yet, the running StatusBar form's `take_snapshot` showed "Ready" in the
  first panel and "27/10/2026" in the second, with no fallback note.
- On the fix alone, the IDE, the File menu dropped down (a window of its own on the desktop, composed in place under
  the menu bar), the Options dialog and a running form all captured as before, at scale 1.5.

---

## `set_window_state` with no window manager said the window manager hadn't confirmed, and to re-read — **CLOSED** (#2185, 2026-10-27)

**Symptom (2026-10-26, openSUSE Leap 16 VM, Xvfb `:99` with no window manager, #1755's live check).**
`set_window_state {"state":"Minimized"}` answered `"error":"The window manager has not confirmed the requested change."`
with a note to re-read with `get_window_state`, and `Maximized` the same. Nothing would ever confirm either, and
re-reading showed `Normal` every time. Nothing said that minimize and maximize couldn't work on that display while a
restore with a position and size could.

**Fixed (#2185).** When a minimize or maximize goes unconfirmed, HexIDE reads the X11 root window's
`_NET_SUPPORTING_WM_CHECK`, which a window manager sets (`X11WindowManager`, libX11 through `LibraryImport`, in the
DEBUG-only server). With none there, the reply says `No window manager is running on this display, so nothing will
apply Minimized.`, explains why, and says Normal with x, y, width and height still works. A window manager that is
there but slow, a display that can't be read, another platform, and an unconfirmed restore all keep the old text.
Guarded by `WindowStateUnconfirmedTests`.

**Live (2026-10-27, a Debug build of the fix under WSL, Ubuntu 26.04, headless `claude -p` through the hexide tools).**

- On Xvfb `:99`, where `xprop -root _NET_SUPPORTING_WM_CHECK` finds no such atom, Minimized and Maximized each
  answered with the new text, and `get_window_state` agreed with the reply's `window` field. A Normal with
  `x 40, y 30, 900×640` was confirmed.
- On WSLg's `:0`, whose root window carries `_NET_SUPPORTING_WM_CHECK`, the same build kept the old text for an
  unconfirmed Minimized, and confirmed Maximized and the same Normal.

## `set_ide_language`'s Keep didn't save the language — **CLOSED** (#2268, 2026-10-27)

**Symptom (2026-10-27, rdcore's live check).** `set_ide_language` opened the confirmation gate, and Keep was pressed on
it. The IDE stayed in the new language for the session, and the next start came back in the one saved before, *System
default*. The reply said only that the gate "reverts on its own unless Keep is pressed", which a caller reads as Keep
being the whole of confirming.

**Cause.** The gate's Keep only leaves the language applied. The Options page saves it when the dialog closes with OK
(`LanguagePageViewModel.SaveToSettings`), and the tool has no OK to press. The localization spec's *Confirming the
change* says a confirmed language persists across restarts.

**Fixed (#2268).** The tool saves the language to `ActiveLanguage` when its gate closes on Keep. Revert or the countdown
saves nothing. It saves in the tool and not in the gate service, because there Cancel in Options still takes back a
kept language. The reply says what Keep and Revert will do. Asking for the language already in use opens no gate, and
the reply now says so, where before it claimed a gate was open. Guarded by `SetIdeLanguageSavesTests`.

**Live (2026-10-27, Windows, a Debug build of the fix, headless `claude -p` through the hexide tools, in the session's
own profile).**

- `set_ide_language {"id":"fr"}` replied `Switching to French ('fr'). A confirmation gate is open: Keep saves French
  for later starts, as OK in Options does, and Revert or its countdown goes back to 'system' and saves nothing. …`.
- `interact` invoked `Conserver  /  Keep`. The profile's `settings.json` then held `"activeLanguage": "fr"`.
- After `shutdown_ide` and a relaunch, the menu bar and status bar were French from the start.
- `set_ide_language {"id":"fr"}` then replied `French ('fr') is already the IDE's language, so no confirmation gate
  was opened. It is saved for later starts.`, and no dialog was open.
- The check also found the Properties description staying in English after the live switch, which is #2270.

---

## Every designer drag's reply said "nothing was docked" — **CLOSED** (#2232, 2026-10-27)

**Symptom (2026-10-26, Windows, #2216's and #2217's live checks).** All 18 drags on the form designer, moving, resizing
and drawing controls, ended their replies with a sentence about Dock, though none of them involved it:
`interact {"target":"…/List[ControlsContainer]/ListItem[Command1]","action":"click","value":"5,8 to 5,56","window":"ide"}`
→ `dragged the left button at (5, 8) on 'ControlItem', … (pointer capturer); Dock's layout is unchanged, so nothing was
docked: …`. Each drag did what it should. A first-time caller reads "nothing was docked" as the drag having failed.

**Cause.** #2175 added the clause for every drag pressed inside a `DockControl`, and the designer is inside one.

**Fixed (#2232).** The reply says what Dock moved whenever it moved something. It says the layout is unchanged only for a
drag pressed on one of Dock's drag areas (`DockProperties.IsDragArea`: a tab, a tool window's title strip), which is
where Dock itself starts a drag. Any other drag says nothing about Dock. Guarded by `DragDockTests`.

**Live (2026-10-27, Windows, a Debug build of the fix, headless `claude -p` through the hexide tools).**

- Moving Command1 by `5,8 to 45,58`, and drawing a TextBox with the toolbox's Text tool, replied without the clause.
- Properties dragged along its own title strip still said the layout was unchanged, and dragged onto the Project dock
  said `Dock moved 'Properties' from a dock of its own to the dock with 'Project Group - Group1'`.

## `press_key` refused a chord written as one key as an unknown key — **CLOSED** (#2189, 2026-10-27)

**Symptom (2026-10-26, Windows, #2030's live check, Sonnet).**
`press_key {"target":"…/Custom[Root]/None[TextEditor]","key":"Ctrl+H","window":"ide"}` answered
`unknown key 'Ctrl+H' — use an Avalonia Key name (Enter, Tab, Back, Escape, Down, S, …)`. `H` was never the problem:
modifiers go in their own parameter, and the refusal didn't say so. The caller gave up on the keyboard and opened
Replace from the Edit menu.

**Fixed (#2189).** A `key` with a `+` between two parts is read as its last part, with the parts before it as
modifiers added to any `modifiers` given, and the reply says so: `pressed Control+H …; 'Ctrl+H' was read as key 'H'
with modifiers 'Ctrl'`. A leading part that is not a modifier is refused naming it, and an unknown last part is named
in the chord. Combined modifiers now read `Control+Shift+End`, not `Control, Shift+End`. Guarded by
`UiAutomationDriverTests`.

**Live (2026-10-27, Windows, a Debug build of the fix, headless `claude -p` through the hexide tools).** `Ctrl+H` in
the code window opened Replace, as `H` with `Ctrl` does. `A+B` and `Ctrl+Nope` were refused naming the wrong part, and
`Shift+End` with `Ctrl` replied `pressed Control+Shift+End …, added to 'Ctrl'`.

## Keyboard `type_text` counted a prompt-raising key as typed — **CLOSED** (#2035, 2026-10-26)

**Symptom (2026-10-26, Windows, #1975's live check).** Keyboard `type_text` of `xyz` into the code window
while running replied `typed 1 of 3 key(s)` and told the caller to answer the reset prompt, then send the
rest. After Cancel, `get_file_content` showed the line unchanged and `hasUnsavedChanges: false`: the `x`
had not landed. The brief does not record the full call's target arguments.

**Workaround (before the fix).** Answer the prompt, then read the text before choosing what to resend.

**Fixed (#2035).** The stop reply identifies the key sent and the dialog it raised, warns that the key
may not have landed, says nothing after it was sent, and directs the caller to answer and check the text
before deciding what to send next. It makes no claim about what each answer does to an arbitrary edit.
`TypeKeysTests` covers dialogs over the typing window, another window, and in a window of their own.
Live verification of the revised reply remains for the supervising session.

## A drag couldn't dock a tool window, and its reply read as a drag that worked — **CLOSED** (#2175, 2026-10-26)

**Symptom (2026-10-26, #1877's live check, Windows).** Dragging AI Chat's title strip onto the Immediate dock's centre,
or the Toolbox dock's, did nothing. The reply was the same as for a drag that docked:
`interact {"target":"…/List[#1]/TitleBar[AI Chat]","action":"click","value":"120,15 to …/List[#0]/TitleBar[Immediate]:460,63","window":"ide"}`
→ `dragged the left button at (120, 15) on 'ToolChromeControl' … release received by 'Grid[PART_Grid]' … (pointer
capturer)`. The same verb drove Dock's column splitters.

**Workaround (before the fix).** None through the surface.

**Cause.** The eight held moves were raised in one synchronous loop, with no layout between them. Dock adds its drop
target on the first move past its drag threshold and finds the drop from that target's layout, so the release came
over a target with no size. A layout pass between the moves was enough to make a headless drag dock, which confirmed
it. A splitter adds nothing mid-drag, which is why it worked.

**Fixed (#2175).** Each held move is input of its own. `FinishAsync` raises them one at a time and lets the
dispatcher run everything above Background between them, layout included. A drag pressed in Dock's layout now ends
its reply with what Dock moved, as `Dock moved 'Properties' from a dock of its own to the dock with 'Immediate'`, or
with `Dock's layout is unchanged, so nothing was docked: …`. Guarded by `DragDockTests`.

**Live (2026-10-26, Windows, a Debug build of the fix, headless `claude -p` through the hexide tools).**

- Properties dragged by its title strip onto the Immediate dock became a tab beside Immediate, and the reply said so.
- A drag back onto its own title strip said the layout was unchanged.
- A drop on the Toolbox dock's upper half split it, with Properties in a new dock above the Toolbox. The reply said
  Properties moved "to a dock of its own".

## A path not starting with `Window` was refused without saying where paths come from — **CLOSED** (#2080, 2026-10-26)

**Symptom (2026-10-26, #534's first-contact baseline, Sonnet 5.5, Windows).** Having seen "Editor > General" in a
snapshot of the Options dialog, it sent `interact {"target":"Editor/General","action":"select","window":"Options"}`
and was refused with `path must start with 'Window' (got 'Editor')`. The refusal was accurate, but it didn't say
where paths come from. Sonnet recovered by loading `dump_visual_tree` on its own.

**Fixed (#2080).** An empty target, and one not starting with `Window`, now end with the next step: `a path comes
from dump_visual_tree, whose every node carries its 'path', and its 'match' finds a control by name`. A guessed
path also gets a call to send as it stands, matching its last segment in the window it was looked for in:
`as dump_visual_tree {"window":"Options","match":"General"}`. Guarded by `PathRefusalTests`.

## `inspect_element`'s `selectionItems` named a selection with no row by its view model's type — **CLOSED** (#2026, 2026-10-26)

**Symptom (2026-10-26, #1981's live check, Windows).** At the IDE's default size the Properties window's categorized
list was 0 px tall, so no row was made. `inspect_element` on `…/List[CategorizedProperties]` answered
`"selectionItems":["HexIDE.VisualDesigner.PropertyViewModel"]`. Once the window was maximized, it answered
`["Caption"]`.

**Fixed (#2026).** A selected item with no row is named by its `AccessibleName`, the name the IDE's view models give
their rows to bind, which is the name the dump shows. Failing that, it is named by its own text. With neither, the
reply says `#25, which has no row to read a name from: a list makes rows only for what it shows`, never a
type's name. Guarded by `UnrealizedSelectionTests`.

## Focus in a flyout read as no focus, and `press_key` moved it out before pressing — **CLOSED** (#2030, 2026-10-26)

**Symptom (2026-10-26, #1960's live check, Windows).** With Replace's Cut/Copy/Paste flyout open,
`dump_visual_tree {"window":"auto","match":"Paste"}` gave `"focusedPath":null`, and
`press_key {"target":"Window","key":"Escape","window":"Replace"}` replied `moved focus from
Window/List/Pane/MenuItem[Paste] to Window/Custom/Edit[#0]; pressed Escape on TextBox …`. That one Escape closed the
whole dialog, where a person's closes only the flyout.

**Cause.** On the desktop a flyout's items sit in a popup root of their own, and the focus checks walked visual
ancestors, which never reached the dialog. The popup root has no logical parent leading back either: a first fix that
followed it was measured live and changed nothing.

**Fixed (#2030).** Focus is placed in the control view as the dump places popups. The walk goes from a popup's
content to the control the popup is placed at, so focus in a flyout counts as in its owner's window. `focusedPath` now
names `Window/Custom/Edit[#0]/Pane[#0]/MenuItem[Paste]`, `press_key` presses Escape on the flyout's item without moving
focus, and its reply names that item by the same path. Guarded by `FocusInPopupTests`.

## A waiting dialog's buttons included its check boxes and radio buttons — **CLOSED** (#2079, 2026-10-26)

**Symptom (2026-10-26, #534's first-contact baseline, Windows).** Opening Tools > Options gave
`'Options' (Prompt for project/Reload files changed outside the IDE/Reset All/Restore Defaults/OK/Cancel), a modal
dialog over 'Project1 - HexIDE [design]', opened during this call …`. The first two are check boxes on the page that
was showing, not ways to answer the dialog. All three runs that opened Options showed it.

**Cause.** `OpenDialogs.DescriptionOf` listed every `Button` it found, and Avalonia's `CheckBox` and `RadioButton`
derive from `ToggleButton`, which derives from `Button`.

**Fixed (#2079).** The list leaves out every `ToggleButton`. The five texts that describe a waiting dialog all go
through `DescriptionOf` (#1975), so all five gain the fix. Guarded by `OpenDialogsTests`.

## `take_snapshot` threw "Positive number required" with a text box's flyout reopened in a dialog — **CLOSED** (#1960, 2026-10-26)

**Symptom (2026-10-25, Windows, IDE in Arabic).** With Replace open and its first box's Cut/Copy/Paste flyout reopened by a
second `interact` `expand`, `take_snapshot()` and `take_snapshot(window: 'استبدال')` failed with an unhandled
`System.ArgumentException: Positive number required (Parameter 'size')`, thrown from `RenderTargetBitmap`'s constructor
in `SnapshotComposer.Compose`. Escape closed the flyout and the next snapshot worked.

**Workaround (before the fix).** Close the popup, then take the snapshot.

**Cause (by decompiling Avalonia 12.0.4 and its Skia backend).** That message comes only from a render target whose
sides are each at least 1 and whose size in bytes, an `Int32`, has wrapped negative: past 2 GiB. A side below 1 gives a
different message. The canvas is the union of the window and every open popup in screen pixels, so a popup reported
far from the window does it: one at (-32000, -32000) throws the same exception. One whose own size is the largest
`Int32` does too. Where the reopened flyout was really reported is not established.

**Fixed (#1960).** `Compose` leaves out a popup that lies on no screen, and one that would take the image past
16,384 × 16,384 pixels' worth, reckoning the extent in longs. `take_snapshot`'s `note` names each: what it held, where
it was reported in screen pixels, and why. A popup with no area is named too. The window and every other popup are
drawn. `SnapshotComposerTests` and `SnapshotLeftOutNoteTests` pin it. A wrap that lands positive is worse than the
exception, since the canvas is then too small for what is drawn into it, and the cap covers that as well.

**What is still owed.** If a snapshot carries the note, its coordinates are what explains the original placement.

**Live (#1960, 2026-10-26).** Windows, a Debug build of the fix, from a Claude Code session's hexide tools. The report's
steps did not reproduce the failure: across six cycles of `collapse` and `expand` on Replace's first box (three in English,
three in Arabic, the dialog titled `استبدال`), 14 snapshots each drew the flyout inside the 546×291 dialog, with no note.
Where the reported flyout really was placed is not established, so the fix is pinned by the composer's tests, not by a
live reproduction.

## Four texts named a waiting dialog only by its title, which two of the IDE's dialogs share — **CLOSED** (#1975, 2026-10-26)

**Symptom (2026-10-22, #1329, Windows and the openSUSE VM).** During a run, both the reset prompt and the run-time error
dialog are titled `'HexIDE'`. One held a `MessageBox` with Yes and No (OK and Cancel since #1360, measured 2026-10-25),
the other a `RuntimeErrorView` with Continue, End, Debug and Help. The other half of #1365, that only a call naming a
window that doesn't exist lists the open ones, is done: #1624 says so in the `window` description.

**Narrowed (#1624, measured under #1330 on 2026-10-25).** The `modalDialog` note now says what the dialog is:
`'HexIDE' (the run-time error dialog: "Compile error:…", Continue/End/Debug/Help)`, or
`'HexIDE' (a message box: "This action will reset your project, proceed anyway?", OK/Cancel)`.

Three texts still give the title alone:

- the program wait's "has a window open over its form, 'HexIDE'";
- keyboard `type_text`'s stop when a dialog opens part way;
- the refusal from `type_text` and `press_key` while a modal dialog is open.

The calls are in [#1975](https://github.com/hexide-io/HexIDE/issues/1975).

**Workaround.** Read the `modalDialog` note of the next reply that has one, or dump the dialog before answering it.

**Suggested fix.** Use the note's description in the three texts.
[#1975](https://github.com/hexide-io/HexIDE/issues/1975).

**Fixed (#1975).** The program wait, keyboard `type_text`'s stop, the refusal from `type_text` and `press_key`, and
`interact` `close`'s refusal for a window with a dialog over it now describe the dialog as the `modalDialog` note does,
through the same `OpenDialogs.DescriptionOf`. That is its title, then its kind, first line and buttons where it has them.
A dialog of a kind the describer doesn't know, an InputBox among them, gives its title and buttons. One with no title is
"one with no title" in all five texts. The wait describes a MsgBox once it has built its text and buttons, which it does
after it shows. Guarded by `ProgramWaitTests`, `TypeKeysTests` and `CloseWindowTests`.

**Live (#1975, 2026-10-26).** Windows, a Debug build of the fix, from a Claude Code session's hexide tools, on a form with
a 40 ms Timer, a button calling an undefined Sub and a button showing `MsgBox "Hello"`:

- The wait, for the MsgBox: `has a window open over its form, 'DialogProbe' (a message box: "Hello", OK); a MsgBox holds
  its handler`.
- The wait, for the compile error, on 3 of 10 clicks, with the note naming it on the other 7: `has a window open over its
  form, 'HexIDE' (the run-time error dialog: "Compile error:…", Continue/End/Debug/Help)`.
- Keyboard `type_text` into the code window while running: `typed 1 of 3 key(s) into TextEditor, then stopped: 'HexIDE'
  (a message box: "This action will reset your project, proceed anyway?", OK/Cancel) opened over the window`.
- With that prompt open, `type_text` and `press_key` were refused with replies starting `'HexIDE' (a message box: "This
  action will reset your project, proceed anyway?", OK/Cancel), a modal dialog over 'DialogProbe - HexIDE [run]'`.

## `dump_visual_tree {}` on the IDE window was more than Claude Code takes inline — **CLOSED** (#1982, 2026-10-26)

**Symptom (2026-10-25, #1330's check of #942, Windows).** `dump_visual_tree {}`, the call a first-time caller makes,
returned 66,822 to 83,944 characters on the IDE's main window. Claude Code saved each reply to a file and told the model
to read it in chunks, so nothing could be read from the reply itself.

**Fixed (#1982).** Claude Code (2.1.310) saves an MCP tool's text reply over 50,000 characters to a file, counting
characters, not tokens. So a tree over 40,000 characters is now cut at the most levels down that fit, and the reply's
`note` says how large the whole tree was, where it was cut and how to see the rest: a node marked `truncated` holds
more, so dump again with its path as `root`, or use `match`. A node with too many children to fit even one level down
says so. The note is written before the tree, so a client that keeps only the start of a long reply still has it. The
tool's description says so too. Guarded by `DumpFitsReplyTests`.

**Live (#1982, 2026-10-26).** Windows, a Debug build of the fix, from a Claude Code session's hexide tools, with Form1's
code and designer open:

- `dump_visual_tree {}` came back inline: `The tree to maxDepth 20 is about 75,967 characters, more than some clients
  take inline in a reply, so it was cut at maxDepth 7, about 33,631 characters.` Every `truncated` node was at level 7.
  After review the note says "cut 7 levels down" instead, since under `interactiveOnly` a cut is not the same tree as a
  dump with that `maxDepth`.
- `dump_visual_tree {"interactiveOnly": false}`: about 108,281 characters, cut at maxDepth 6, about 35,875, inline.
- A dump rooted at a truncated node, the designer's `Custom[Root]`, listed its children, with no note.
- `maxDepth: 3` came back whole, with no note.

The cut was one depth everywhere, so hidden toolbars came back whole while the open document was cut
([#2033](https://github.com/hexide-io/HexIDE/issues/2033)). The #2033 fix first omits children below hidden
nodes when a tree exceeds the budget, retaining their paths, `isHidden` and `truncated` markers. Only if
that still exceeds the budget does it cut levels in other branches, preserving the active document
subtree and its ancestors. The requested root keeps its children
so dumping from a hidden node makes progress. This applies with either `interactiveOnly` setting; trees
that already fit and `match` searches are unchanged. The note explains what was omitted and how to see it.
PR #2169's live check failed: `dump_visual_tree {}` still cut the designer at `Custom[Root]/Pane`,
eight levels down, with both 3 and 26 controls (supervisor, 2026-10-26). Hidden pruning alone was insufficient.
The follow-up preserves the active tab's content while cutting other branches; if that content alone
exceeds the budget the note says it is still over and advises `root`.
Covered headlessly by `DumpFitsReplyTests` and `ActiveDocumentDumpTests`; live verification of this
follow-up remains for supervisor review.

## `dump_visual_tree` kept a row a list had scrolled away from, with `isOffscreen: true` — **CLOSED** (#1981, 2026-10-26)

**Symptom (2026-10-25, #1330's check of #1292, Windows).** In the Properties window, after `select` with value `Width`
scrolled the list to that row, `scroll` `home` and a collapse of Appearance, the next dump listed Width with
`isOffscreen: true`, first, before the rows in view. #1292 had left rows out of view out.

**Cause.** A virtualizing panel keeps its list's tab-once row realized when it scrolls out of view, so a Tab into the
list can land on it, and selecting a row makes it that row (Avalonia 12.0.4's `VirtualizingStackPanel`, decompiled). It
is arranged where it would be, outside the view, and its index is real, so #1292's test for a released row kept it.

**Fixed (#1981).** A row a virtualizing panel holds apart from the rows it has realized for the view, one its list
still indexes but no longer counts as realized, is left out of the dump, of path addressing and of `select`'s
candidates, as rows out of view are. It is told by membership, not position: a hidden row's bounds are stale, and the
held row's place is an estimate. `select` with its value now scrolls to it and says so, as for any row out of view.
`press_key`, `type_text` and hover no longer reach an editor in it. A row with keyboard focus in it, or with a dropdown
of its own open, is still listed. A hidden row in view is listed with `isHidden: true` as before, and a
non-virtualizing list's rows out of view are listed as before. `dump_visual_tree`'s description now says a long ListBox
lists its rows in view. Guarded by `HeldRowTests`.

## `toggle` read the state back before work it set off had run, so a refused uncheck was reported as done — **CLOSED** (#2050, 2026-10-26)

**Symptom (2026-10-26, #1103, Windows).** The Components dialog refuses an uncheck of a library a form uses, and puts
the box back from posted work. `interact {"window":"Components","target":"…/CheckBox[Microsoft Windows Common
Controls 6.0 (SP6)]","action":"toggle"}` answered `toggled 'Microsoft Windows Common Controls 6.0 (SP6)': it is
now off`. After the refusal's message box was answered, `inspect_element` read `"toggleState": true`.

**Fixed (#2050).** On the IDE's own windows, `toggle` now reads the state back once the dispatcher has run what is
queued ahead of `Background`, so a box put back from a posted callback is reported as put back: `…, but its handler or
a binding put it back: it is still on`. A refusal that waits for its message box to be answered can't be known
before the reply, so when a dialog opened during the call the reply adds `a dialog opened during this call, which
modalDialog names, and the state may change again once it is answered`. A running form's toggle was
already read once its program had run (#1280). Guarded by `ToggleSettledTests`.

## `take_snapshot`'s `inline` image has not been checked through an MCP client, over a tunnel or locally — **CLOSED** (#1330, 2026-10-25)

**Symptom.** #957 added `inline` and closed. Its archived entry records one measurement, taken through the server's
HTTP endpoint rather than a client's tool call ("a 3840×2054 window came back as a 1568×838 PNG of 75 KB"), and
says "Not yet run through Claude Code over a tunnel". A raw request shows nothing about what a client does with the
image block, and a client on another machine receiving the image is what #957 was filed for. Covered headless by
`InlineSnapshotTests` only.

**Workaround.** Without `inline`, copy the file from the IDE's machine by hand.

**Suggested fix.** No code change is expected. Call `take_snapshot {"inline":true}` through the hexide tools, on the
IDE's machine and from another one through a tunnel, and record what the client shows. [#1330](https://github.com/hexide-io/HexIDE/issues/1330).

**Closed (#1330).** Checked through Claude Code's hexide tools, from Windows, on a Debug build of main at beada72.

- **On an openSUSE Leap 16 VM through an SSH tunnel:** `take_snapshot {"inline": true}` returned its JSON, with
  `inlineWidth` 1440 and `inlineHeight` 756, and one `image/png` block the client showed. Read from the client's own
  stream, the block is a 1440×756 PNG.
- **On the IDE's own machine:** the same call returned a block of 1418×671, its `inlineWidth` and `inlineHeight`.
- **Without `inline`:** `take_snapshot {}` had no image either way.

The measurements are under #957's entry below.

## The code window's gutters are not in the control tree, so a gutter click cannot be driven — **CLOSED** (#707, 2026-10-25)

**Symptom (2026-10-01, during #273 task 3.12's live check).**
`dump_visual_tree {"root":"…/Custom[Root]/None[TextEditor]/Pane[PART_ScrollViewer]/None","interactiveOnly":false,"maxDepth":4}`
returns the `TextArea` with two children: an empty `ItemsControl` and the hidden `PART_Watermark`. The
breakpoint gutter, the bookmark gutter and the line-number margin are all absent, because AvaloniaEdit's
`AbstractMargin` has no automation peer, so `interact` has nothing to click. The task's own named case, a
click on a folded header, could only be tested headlessly (`MarkGutterOnReadOnlyLineTests`, which raises
the press on the gutter itself).

**Workaround.** Place the caret with `interact set_property CaretOffset=N` on the code view, then
`invoke_menu_item Debug/Toggle Breakpoint` or `Edit/Bookmarks/Toggle Bookmark`. That reaches the same
toggle a gutter click calls, but not the gutter's own mapping from a point to a visual line, which is
what decides what a click on a folded row marks.

**Suggested fix.** Undecided: a peer on the gutters that `interact` can drive with a line, a `click` action
on `interact` at a point relative to its target (which would cover other drawn surfaces too), or a
dedicated tool if the authoring policy justifies one. Filed as
[#707](https://github.com/hexide-io/HexIDE/issues/707).

**The fold margin is the same (2026-10-02, during #273 task 3.14's live check).**
`dump_visual_tree {"interactiveOnly":false,"maxDepth":40}` over the whole window has no node whose class is
`FoldingMargin` or `FoldingMarginMarker`, so a fold cannot be expanded or folded by its marker. The
`TextEditor` node does report `expandCollapse`, which reads as the answer and is not: `inspect_element` on it
shows no expand state, and the provider is advertised because the editor owns a context menu, which is what
`expand` opens. **Workaround:** placing the caret inside a folded section with `interact set_property
CaretOffset=N` on the code view expands it, because the editor unfolds any fold the caret moves into. Nothing
folds one again. A `click` action on `interact` would cover this margin as well as the gutters.

**A member's Attribute lines fold too since #273 task 4.3**, into a box at the end of the line they describe, and
the workaround reaches them with one difference, read in code and not run. An offset strictly inside the run opens
it, as for any fold. An offset at the run's very end does not: the window moves a caret that lands after a folded
run's box to the end of the declaration, in front of the box (`CodeEditorView.KeepCaretOffFoldedRunsEnd`), and the
run stays folded.

**Fixed by #1033; paperwork in #707.** `interact` now takes `action: "click"` with a
point relative to its target. `UiAutomationDriver.DoClick` translates that point into window coordinates
and hit-tests the rendered window, then delivers pointer press and release to what it hits. The margin
needs no automation peer: address the editor that contains it. The old `expandCollapse` trap above still
applies; use `click` for a fold marker, not `expand` on the editor.

Call shape (substitute the discovered editor path and the measured point; this is a recipe, not a
record of a live call made in this pass):

```json
{"target":"<editor path from dump_visual_tree>","action":"click","value":"<x>,<y> left","window":"ide"}
```

Discover the editor with `dump_visual_tree {"window":"ide","interactiveOnly":false,"maxDepth":40}`,
then `inspect_element {"target":"<editor path>","window":"ide"}` for its bounds. Use
`take_snapshot {"window":"ide"}` to locate the desired gutter and row; subtract the editor's origin
from the point in the window to obtain `x,y`. Coordinates depend on layout, font, scrolling and scaling;
there is no fixed gutter offset to reuse across sessions. The click reply names what the hit test reached;
confirm the resulting mark or fold as well, since delivery alone does not prove a toggle.

**Verification.** Existing `InteractClickTests` cover target-relative coordinates, pointer press/release
on a peerless Border, and delivery to the control at the point rather than necessarily the addressed
control. No product behaviour changes in #707.

**Live check (2026-10-25, Windows, Debug build).** The breakpoint margin is now in the control tree as its own
node, so target it, not the editor:

```json
{"target":"…/Custom[Root]/None[TextEditor]/Pane[PART_ScrollViewer]/None/List/Custom[BreakpointMargin]","action":"click","value":"7,75","window":"ide"}
```

Its reply was `clicked the left button at (7, 75) on 'BreakpointMargin'`; `get_breakpoints` then gave `[21]`, and the
same call again gave `[]`. The margin is 14 units wide: the same row addressed on the editor at `10,77` fell on its
edge, landed on `ScrollContentPresenter`, and toggled nothing, which the reply's "landing on" names. The fold marker
has no node, so it is clicked through the editor:

```json
{"target":"…/Custom[Root]/None[TextEditor]","action":"click","value":"66,10","window":"ide"}
```

Its reply named `FoldingMarginMarker`; the form's header unfolded, and the same call folded it again. Bookmarks have
no gutter click to drive. Not checked live: a breakpoint click beside a folded header.


---

## Replies that report or act on a document without taking its name do not say which project's it is — **CLOSED** (#886, 2026-10-24)

**Symptom.** Read in code, not run, at ec9bfc4. #273 made every tool that takes a document's name find it in any
loaded project and name it back as `project` and `document` (archived under the #575 and #467 entries). The tools
that report or act on a document without taking its name were left as they were. The shapes, from the code, with a
group holding a `Form1` in each of two projects:

```
step_over {}               → "note": "Paused at Form1 line 9 (Step)."
                             as run_project, break_program, continue_program, step_into, step_out, run_to_cursor
get_locals {}              → "context": "Form1.Form_Load", the Module.Procedure header, as the watch tools' context
invoke_designer_undo {}    → "note": "Undid '…'. Next undo: …", on the active designer, its form not named,
                             as invoke_designer_redo, get_undo_state and invoke_format_command
get_document_tabs {}       → each tab's title, which activate_document_tab and close_document_tab take
get_open_editors {}        → each editor's title, and 'activeWindow' as a title
```

None of these says which project's `Form1` it means. `get_debug_state` and `get_call_stack` now carry the running
`project`; the run-control notes beside them do not. A tab's title starts with its project's name
(`Second - Module1 (Code)`, measured in the #575 entry), but it is the IDE's display string, whose suffix follows
the IDE's language, and not the `project` and `document` every naming tool takes and returns.

**Workaround.** For a run, `get_debug_state {}` gives `project` beside `module`, and every frame `get_call_stack {}`
lists is in that project. For a designer, `view_designer` with `project` first makes the intended form the active
designer, and its reply names it. Address a tab by the title `get_document_tabs` returns, never by one composed from
a name.

**Suggested fix.** #886's: the running project beside a bare module name in the run-control notes and in the
locals and watch `context`; `project` and `document` for the document behind the active designer in the designer
tools' replies; and the tab tools reporting `project` and `document`, and accepting them in place of a title.
Until then the hexide-mcp-server requirement #273 added, that "a tool that accepts or reports a form, module or class
SHALL identify it by its project and its name", is only partly met.
[#886](https://github.com/hexide-io/HexIDE/issues/886).


**Fixed (#886).** Run-control pause notes include the running project; locals and watches replies carry
`project` (the project in which watch expressions are evaluated). Designer undo state and undo/redo/format
replies carry `project` and `document`, including refusals after finding a designer. Tab listings carry the
same identity; `get_open_editors` adds structured `editors` and `activeEditor` alongside its existing titles.
Activate and close accept `name` with optional `project` through `NamedDocuments`; `type` chooses code or
designer when both are open. Title addressing remains available for tool tabs, and duplicate matches are
refused. Headless guards cover the reply shapes and lookup wiring; direct tests cover tab targeting.
Live verification of focus, close prompts and grouped-project replies remains the supervisor's check.

---

## Every automation route to a new document saves it — **CLOSED** (#502, 2026-10-24)

**Symptom.** `add_file` says, in its own description, "Adds a new form or module to the startup project, or to
the loaded project `project` names …, **saves the new file to disk**, and returns its path." (Reworded by #596 and
by #273, which added `project`; this entry first quoted "saves it to disk", and the point is the same.)
`--newproject` — the flag the documented dev loop uses on every launch — goes further and saves the whole project
into `%TEMP%` before the first tool call
(`DesktopStartup.cs`, the `--newproject` hook's `SaveProjectToDirectory` call). So a caller driving HexIDE through automation never sees a document that has no
file.

**Why that matters more than it sounds.** #489 measured that in real VB6 a document with no file is the
*ordinary* state: nothing is written until the project is saved. #273 names such a document
`untitled:<Project>/<Name>.<ext>` on the wire, and that branch was measured against five foreign language
servers precisely because a branch nothing exercises is a branch that rots. A model asked to verify it
through this surface will call `add_file`, see a `file:` URI, and reasonably conclude the `untitled:`
spelling is dead code.

**Workaround, and it is not obvious.** Drive the real menu: `dump_visual_tree` to find
`MenuItem[Project]`, `interact` with `expand`, `dump_visual_tree` again scoped to it, then `interact` with
`invoke` on `MenuItem[Add Module]`. That reaches `ProjectService.AddNewModule` — which, as of today, also
writes (hexide-io/HexIDE#500). The only genuinely pathless document reachable at all is the initial `Form1`
of a project created through `File > New Project` **in the UI**, which `--newproject` skips.

**Such a form also reads without its header, since #273 task 3.18.** Read in code, not run. `get_file_content`
returns a form's whole file, except a form created in this session and not yet saved or changed in the designer,
which has no header yet, so its text is its code alone; the tool's description says so. Its first committed designer
change or save gives it one (`HeaderRefresher.ApplyHeader`), its marks move down by the header's height, and from then
its lines count from the top of that header. So a line number read before then is out by the header's height after
it. **Workaround:** read the content again after any designer change or save before using a line number from it.
Where a created form's first header should come from, so that it holds one from the start, is
[#885](https://github.com/hexide-io/HexIDE/issues/885).

**Suggested fix.** Two parts, and the first is cheap. (1) `add_file` should say which of the two states it
leaves the document in, and gain a way to ask for the other — a caller cannot currently express "add it the
way the user's own menu does". (2) A launch flag that creates a project without saving it, so the dev loop
can reach the state the product will normally be in once #500 lands. Until then, any verification of
`untitled:` naming through this surface is verifying something the surface itself prevents.

**Fixed.** #500 made `add_file` pathless: its description and reply explain the null path and the Save Project
pickers. #502 adds `--newproject-unsaved` (also `/newproject-unsaved`), which creates the initial project and
form without saving. It wins over `--newproject` and a positional path; `--newproject` keeps its existing save.
For an `untitled:` check launch with `--capture-lsp --newproject-unsaved`, then open Form1's code and inspect
its `didOpen`. No new VB6 behaviour is inferred: this uses #489's measured no-file state. The initial-header
limitation above remains open as #885 in the live gaps document.

## `add_to_selection` names the new selection but not its reference, so a caller learns what Align measured against only afterwards — **CLOSED** (#951, 2026-10-24)

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
interact {"target":"Window/.../List[ControlsContainer]/ListItem[Command1]","action":"add_to_selection"}
→ {"detail":"added 'Command1' to the selection; 2 items are selected now: Command0, Command1"}
```

Only the next `invoke_format_command` reply, "…, aligned to Command0", named the reference, the control every
Align and Make Same command measures against. A snapshot between the two showed Command0 with the solid handles.
The reply lists the selection in index order, as does `inspect_element`'s `selectionItems`, so no read names the
reference before a command runs, which the form-designer spec requires of the designer. The same goes for
`remove_from_selection`. Cross-platform by code reading.

**Workaround.** Select the intended reference first and `add_to_selection` the rest: by code reading, the
reference stays on the first item selected, and removing it moves it to the lowest-indexed item still selected.
A snapshot shows the reference with the solid handles.

**Suggested fix.** With two or more selected, the reply names the primary item, and when `remove_from_selection`
removes it, names the new one. [#951](https://github.com/hexide-io/HexIDE/issues/951). Which control should be
the reference is #928.

**Fixed.** Multi-item selection replies list the primary first and name it as the designer reference for Align
and Make Same. Removing it reports the replacement. The action description says how to choose it: select it
first. Headless regression tests cover both selection orders and removal; live designer verification remains
for the supervising session. Selection semantics (#928) are unchanged.

## No call double-clicks a running form — **CLOSED** (#1389, 2026-10-24)

**Symptom (2026-10-22, #1031, Windows).** A running form's `DblClick` comes from a double click's second press, told by
the press's click count. `interact {"target":"<a Label on a running form>","action":"double_click"}` raises Avalonia's
`DoubleTapped` and selects the target, with no pointer press, and answers `success: true`; the program's `Label1_DblClick`
never runs, and nothing in the reply says no press was sent. Two `interact {"action":"click"}` calls in a row each press
with a click count of 1, and an MCP round trip puts them past the 500 ms double-click time anyway.

**Workaround.** None through the surface. `MouseEventTests` drives a double click headlessly, with real pointer input.

**Suggested fix.** Accept `double` in `click`'s value, as in `10,5 double`, raising press, release, press and release
with the second press's click count 2. Have `double_click`'s reply say it raised `DoubleTapped` and no press.
[#1389](https://github.com/hexide-io/HexIDE/issues/1389).

**Fixed.** `interact {"target":"<a running form>","action":"click","value":"10,5 double"}` sends press,
release, press and release, with the second press carrying ClickCount 2; `10,5 right double` uses the right button,
and plain `double` uses the centre. Between clicks it waits for the first release's program work. If the target
has hidden, closed or detached, the reply says the second click was not sent. The reply names the double click,
button and coordinates. `double_click` keeps its selection and DoubleTapped behavior for the Project Explorer;
its reply now names DoubleTapped, says no pointer press was sent, and points to `click` with `double` for DblClick.
Running-form integration tests pin the vb6.exe event order already recorded in the oracle and #1031's tests.

## A control's path changes when it gains a provider, so a saved path stops resolving as text grows — **CLOSED** (#1436, 2026-10-23)

**Symptom (2026-10-22, #1386, Windows).** While paused, `dump_visual_tree {"window":"ide","root":"<Imm>","maxDepth":4,
"interactiveOnly":false}`, with `<Imm>` the Immediate window's `.../TitleBar[Immediate]/Pane[Immediate]/Custom`, gave
`<Imm>/Pane[PART_ScrollViewer]/None` for its text area, and `type_text` on that path worked for three lines. The fourth
`type_text` on the same path answered `no child matching 'Pane[PART_ScrollViewer]' under '<Imm>'`. A fresh dump showed
`<Imm>/None[Editor]`, the TextEditor, now with `providers: ["scroll"]`, holding the scroll viewer. The control view hides a
`None` node with no provider and no focus, and the editor stopped qualifying once its text overflowed.

**Workaround.** Dump again and use the new path; `<Imm>/None[Editor]` takes `type_text` and `press_key` from then on.

**Suggested fix.** Decide whether a node is hidden on what its content can't change: leave `scroll` out of it, or never
hide a node with an x:Name or AutomationId. And when a segment misses, say which node has appeared above it.
[#1436](https://github.com/hexide-io/HexIDE/issues/1436).

**Resolution.** Transparency ignores scrolling, including the synthetic scroll capability a TextEditor gains on
content overflow. An x:Name alone still collapses; an AutomationId still keeps a wrapper. Range capability is
read from the peer independently of current usable bounds, so `rangeValue` appearing cannot insert a segment.
When a missed segment and its remaining path resolve under exactly one immediate control-view child, the refusal
names that wrapper and the complete corrected path, without acting on it. The tool description states the rule.

**Verification.** Headless production-view census: code editor, Immediate window, ListBox, a populated Properties
grid with its production templates, and a running VB6 form. Before the change, named and unnamed TextEditor
paths inserted a `None` segment on overflow, and the wrapper-miss refusal omitted the correction; those regression
tests failed. The census moved only the overflowed Immediate editor: `None[Editor]` is collapsed, and its 16
emitted descendants lose that segment, matching their paths before overflow. No paths moved on the other four
surfaces; the code editor remains a node because its context menu advertises `expandCollapse`. The range regression
also checks a `None` peer across an unusable-to-usable range. Integration suite: 2,063 passed. IDE unit suite:
3,273 passed, 28 environment-dependent skips. ToolDescription tests: 31 passed. Debug desktop build: zero warnings.
Tree hygiene passed. Live MCP verification was not run in this task, which prohibited MCP calls.

## `invoke_command` on a designer context-menu item looks in the wrong view model — **CLOSED** (#1336, 2026-10-23)

**Symptom.** Recorded when #836 closed (#1271), read in the code and not run. The designer's context flyout binds
Delete, Bring to Front and Send to Back through the form's view model
(`#Root.((visualDesigner:FormEditViewModel)DataContext).DeleteSelected` and the like, in `FormEditView.axaml`), while
each item's own DataContext is the control the flyout opened on. `invoke_command` and `inspect_element` read the
DataContext of the control addressed, so on one of these items they look in the control's view model, which has
none of the three, and `inspect_element` does not list them. A command property bound the same way is missed the
same way.

**Workaround.** Open the flyout with `interact` `expand` on the control, then `invoke` the item, which runs the
command it is bound to through its automation peer. That route was measured on the Project Explorer's flyout
(#947), not on the designer's.

**Suggested fix.** For an item whose `Command` is bound through another element's DataContext, have `invoke_command`
and `inspect_element` look where the binding resolves, or have the refusal say that the command is bound elsewhere
and that `invoke` on the item reaches it. [#1336](https://github.com/hexide-io/HexIDE/issues/1336).


**Fixed.** The target’s resolved `Command` is listed and accepted as `invoke_command` value `Command`, with its binding description and a hint that `invoke` runs it. Execution uses the control’s `CommandParameter` and checks `CanExecute`. Headless regressions open a real designer control’s flyout, inspect Delete, invoke it and assert the selected control is removed, and check a wrong name’s refusal. All three fail against the previous driver.

## A lone surrogate in a call's arguments gets a bare `-32603` before any tool runs — **CLOSED** (#1337, 2026-10-23)

**Original symptom.** Recorded in #1274's review through a raw request whose body was not kept. A string argument
holding an unpaired surrogate got the SDK's JSON-RPC error `-32603 "An error occurred."` before any tool ran, naming
neither tool nor parameter and not saying that nothing was done.

**Reproduced.** `McpRestartTests` starts the real SDK server in-process with the IDE's transport and filters. The
SDK 1.3.0 client accepts a C# string containing `\uD800`, but its encoder replaces it with U+FFFD; `echo` returns
that replacement character. It cannot send the escaped lone surrogate this way. An `HttpClient` POST inside the
test, to the same `/mcp` endpoint, with `Content-Type: application/json` and
`Accept: application/json, text/event-stream`, sent this exact body:

```json
{"jsonrpc":"2.0","id":1337,"method":"tools/call","params":{"name":"echo","arguments":{"text":"\uD800"}}}
```

The original HTTP 200 response was an SSE frame (followed by a blank line):

```text
event: message
data: {"error":{"code":-32603,"message":"An error occurred."},"id":1337,"jsonrpc":"2.0"}

```

**Where it failed.** Request-parameter deserialization, before argument binding or any tool-call filter. The SDK's
`RequestHandlers.Set` calls `JsonSerializer.ReadFromNode`; rewriting the parameter node calls
`JsonDocument.WriteComplexElement`, which throws `InvalidOperationException: Cannot read incomplete UTF-16 JSON
text as string with missing low surrogate.` The JSON-RPC envelope itself had already been read. The #1270
argument-binding refusal therefore never got a chance to explain it.

**Fix.** An SDK incoming-message filter checks argument text before that conversion, including strings nested in
arrays or objects. It returns #1270's tool-error shape (`isError: true`), with the same request id. This call's
content now says:

```text
'echo' refused its arguments, and nothing was done: 'text' is not valid text (an unpaired UTF-16 surrogate). Its input schema names each parameter and its type.
```

If an invalid argument property name cannot be quoted, the refusal instead names the request's arguments as
invalid text. The workaround before the fix was to send the surrogate pair whole, or the character itself.

**Guard.** `Unpaired_surrogate_arguments_are_refused_as_invalid_text` sends both lone high and lone low surrogates
over HTTP, asserts the tool-error flag, tool, parameter, reason, no action and request id, and checks that valid
pairs and literal backslash escapes still work through the SDK client. Restoring both production files to HEAD
makes the wire assertions fail with the original bare error; restoring the fix passes.
[#1337](https://github.com/hexide-io/HexIDE/issues/1337).

## 1. Blind to runtime modal dialogs layered over a running form — **CLOSED** (#61, 2026-09-01)

> **Fixed.** The root cause below was close but not quite right: the dialogs *were* enumerated. Both
> `take_snapshot` and `ResolveActiveWindow` picked with `Windows.FirstOrDefault(w => w != mainWindow &&
> w.IsVisible)`, which is correct only while exactly one non-main window exists. A running program adds a
> `VBFormRuntime`; its `MsgBox` adds a second window *on top* — but the form was created first, so
> first-wins returned the form and the dialog above it was invisible.
>
> Both call sites now share `ForegroundWindow.Pick` (`IDE/HexIDE/IDE/`), which keys on **ownership**:
> every dialog is shown via `ShowDialog(owner)`, so a window owning a visible window is underneath it by
> construction. Discard those and the foreground is what remains. `IsActive` is only a tiebreak — it is
> false for every window when the app is backgrounded, and headless never sets it.
>
> Verified live: a `Form_Load` `MsgBox` over a running form is now captured by `take_snapshot`, reported
> in `activeDialog`, and its OK button is addressable via `dump_visual_tree` → `interact`. Dismissing it
> hands the foreground back to the form rather than stranding the tools on a dead dialog. Guarded by
> `ForegroundWindowTests`, which fail on the old rule.
>
> **`activeDialog` no longer reports the title alone.** A VB6 `MsgBox` reaches the runtime with an empty
> caption (issue #131), and a blank label reads as *"no dialog is open"* — the exact confusion this gap
> existed to remove. It now falls back to the content type, e.g. `MessageBox`.

**Symptom.** When a *running* VB6 program shows a `MsgBox`, or the runtime surfaces a compile/runtime-error
dialog (e.g. "Variable not defined"), `take_snapshot` and `dump_visual_tree` capture the `VBFormRuntime` window
*underneath* — `activeDialog` reports the form, and the modal is invisible. The modal only becomes visible to
the tools once the form is torn down (`stop_project`), at which point it lingers as the active window.

**How it bit.** The `Debug.Print` "Variable not defined (Debug)" error dialog was invisible to me — the user
had to tell me it was on screen. Same for a program's `MsgBox`.

**Root cause.** The snapshot/tree tools enumerate the active `VBFormRuntime` window; the runtime's managed
`MessageBox`/`InputBox` and error dialogs are **separate top-level windows** owned outside that control-view
hierarchy, and the "prefer a visible modal dialog" selection logic doesn't enumerate them.

**Workarounds used.** (a) `stop_project` to surface a lingering dialog, then `take_snapshot`; (b) read the
Serilog log at `%LOCALAPPDATA%\HexIDE\logs\ide\ide-*.log` (flushed by `shutdown_ide`) to see errors that were
logged rather than dialog'd.

**Fix.** The modal-preference logic should also enumerate the runtime's managed dialog top-level windows and
prefer them (reporting the title in `activeDialog`), the same way it prefers an IDE modal. *(Already filed in
the old `docs/TODO.md` MCP section; this consolidates it.)*

---

## 12. A menu popup on a running form cannot be seen or driven — **CLOSED** (2026-09-01)

> **Fixed for the tree and for driving; `take_snapshot` still cannot show a dropped-down menu.**
>
> The suggested fix said #61 would cover this. It did not, and could not: #61 picks between *windows*, and
> a popup never enters `lifetime.Windows`. Two separate causes, both now addressed.
>
> **Seeing.** A popup's content is not under the popup in the visual tree — it is realised in the popup's
> own root — so the walk crossed nothing and every open menu reported `"children": []`. The control-view
> collector (and the `#AutomationId` descendant search) now step through an open `Popup` into its content.
> Because `Resolve` shares that collector, the emitted paths round-trip, so `interact` and `press_key`
> reach popup items too.
>
> **Driving.** `MenuItemAutomationPeer` exposes **no providers at all** — not `invoke`, not
> `expandCollapse` — which is why every verb failed, not just `expand`. `Interact` now falls back to
> `MenuItem.Open()`/`Close()` for a submenu, and to raising `Click` for invoke (MenuItem's class handler
> for that event executes a bound `Command`, so one raise does what a real click does). `DescribeProviders`
> reports those tokens for a `MenuItem`, because an action nobody can see advertised is an action nobody
> tries — `expandCollapse` only where `HasSubMenu`, so a leaf still refuses honestly.
>
> **Separators** are no longer collapsed as structural. They match every "transparent wrapper" condition
> and are still not plumbing: a separator's *position* is the VB6-fidelity question. **They appear only
> under `interactiveOnly: false`** — the default filter drops them, since they have no providers and no
> interactive descendants.
>
> Verified live against `demo/bill-of-fare` while running: `File` and `View` open via `interact expand`;
> `Save As...` reports `isEnabled: false`; `PART_InputGestureText` carries the shortcut text; the
> `Separator` sits between `Zoom` and `Refresh`; `View ▸ Zoom ▸ Zoom In` — two popups deep — resolves and
> invokes, and the form's own label reads *"you chose: View > Zoom > Zoom In (a submenu, two levels
> deep)"*. Eight headless tests in `MenuPopupTests` pin the behaviour.
>
> **The snapshot half is closed too, in a follow-up.** `SnapshotComposer` renders the window and each open
> popup root separately and composes them at their real screen positions, so a dropped-down menu is now in
> the picture — verified live on `demo/bill-of-fare`: *Zoom ▶*, the separator as a rule, *Refresh  F5* with
> its shortcut, and the Zoom submenu open beside its parent in the right z-order.
>
> Three things that cost a build cycle each, worth knowing before touching this code:
>
> - **Render the popup's ROOT, not `Popup.Child`.** The root owns the border, padding and shadow the menu
>   is drawn with; the child alone came out short and shifted.
> - **`DrawingContext.DrawImage(image, destRect)` samples the source's *device-independent* extent.** On a
>   150% display that reads the top-left two-thirds of the piece and stretches it to fill — a correctly
>   placed, correctly sized menu box with magnified, clipped contents. The three-argument overload with an
>   explicit pixel source rect is what is wanted.
> - **The window still goes through `RenderTargetBitmap.Render`, not the context.** That path was already
>   right; routing it through `DrawImage` made the whole form `scaling` times too big.
>
> None of this is visible headlessly, because **headless hosts popups as overlays inside the window** — so
> the defect cannot occur there and a "the popup appears" test passes with the fix removed. The headless
> tests therefore cover the overlay path (the composer must not draw an already-rendered popup twice) and
> the desktop path is verified against the running IDE.


**Symptom.** Verifying that a running form's menus render correctly, `take_snapshot` shows the menu *bar*
but never a dropped-down menu, and `dump_visual_tree` reports every top-level `MenuItem` with
`"children": []`. `interact` refuses both `expand` and `invoke` on a top-level `MenuItem`
(`element does not support 'expand'`), and pressing Enter or Down on it via `press_key` highlights the item
without opening it.

The consequence is that the *inside* of a menu is unverifiable through MCP: separators, shortcut text drawn
right-aligned in the item, item enabled/checked state, and submenu nesting are all only reachable as data.

**Cause.** A menu's items are realised in a popup, which is its own top-level window. `take_snapshot`
captures the form's window, and the popup is not in it — the same reason
[#61](https://github.com/hexide-io/HexIDE/issues/61) reports runtime modal dialogs as invisible. The empty
`children` array is not a bug in the dump: sub-items genuinely do not exist in the tree until the menu is
opened.

**Workaround.** Assert the structure headlessly, where the objects the loader builds can be inspected
directly — `RuntimeMenuTests` covers the bar's contents, sub-item nesting, separators, `InputGesture` and
click dispatch that way. Then verify live only what the *form's own window* can show: the bar's captions,
and the effect of a shortcut, by having the handler write to a `Label` on the form. That combination did
verify #85 end to end, but it verified the dropdown's contents by proxy rather than by looking at them.

**Suggested fix.** Whatever fixes #61 for modal dialogs should cover this, since it is the same underlying
limitation — snapshot and tree-walk the topmost popup root rather than only the form's window. An
`interact` action that opens a menu (`open`/`expand` mapped to `MenuItem.IsSubMenuOpen`) would be the other
half, since a popup that cannot be opened cannot be captured either.

---

## 13. `shutdown_ide` reports success but the process survives a runtime modal — **CLOSED** (2026-09-01)

> **Fixed.** The probable cause below was right about the symptom and wrong about the mechanism. Nothing
> was blocking the message loop: HexIDE sets no `ShutdownMode`, so Avalonia's default
> **`OnLastWindowClose`** applies, and closing the main window while a running program's `VBFormRuntime`
> and its `MsgBox` were still open simply left windows open — so the app kept running, exactly as
> configured. Confirmed by the inverse: with no project running, the old `shutdown_ide` exited cleanly
> every time.
>
> `shutdown_ide` now stops a running project and closes any window still open before closing the main
> one — both gated on `force`, because `force=false` promises what a *user* closing the window sees, and
> a user does not have their program stopped or their dialogs shut from under them. There, the IDE
> staying up is a correct outcome rather than this bug.
>
> It also returns a result now: `{requested, projectStopped, dialogsClosed, note}`. `requested` is
> deliberately not `succeeded` — the reply has to be sent *before* the process exits, so no in-process
> result can honestly claim it did. The note says to poll `/health`, which is the only real confirmation.
>
> Verified live on both paths, so neither is dead code: a `Form_Load` `MsgBox` over a running program
> gives `projectStopped: true, dialogsClosed: 0` (ending the project takes its dialog with it), and an
> open Tools ▸ Options modal gives `projectStopped: false, dialogsClosed: 1`. `/health` stops answering
> within a second in both cases, the process is gone, and the build that used to fail on a lock succeeds.


**Symptom.** With a running VB6 program showing a `MsgBox`, `shutdown_ide` returned without error and the
process stayed alive. The next `dotnet build` then failed on a file lock:

```
error MSB3027: Could not copy "apphost.exe" to "bin\Debug\net10.0\HexIDE.Desktop.exe".
Exceeded retry count of 10. The file is locked by: "HexIDE.Desktop (13436)"
```

**How it bit.** It breaks step 1 of the documented rebuild cycle in `CLAUDE.md` precisely when a modal is on
screen — which, since [#61](https://github.com/hexide-io/HexIDE/issues/61), is exactly the state you are
most likely to be in while verifying dialog behaviour. The failure is silent at the tool boundary: the
success result says shutdown happened, and the lock error arrives one step later looking like a build
problem rather than a shutdown one.

**Cause (probable, not confirmed).** Shutdown runs the normal close path, and a modal dialog owning the
message loop keeps a window alive, so the lifetime never completes. The tool returns once it has *requested*
shutdown rather than waiting for the process to exit.

**Workaround.** The fallback already in `CLAUDE.md`: `Stop-Process -Name HexIDE.Desktop -Force`, then build.
Stopping the running project first (`stop_project`) should also clear it.

**Suggested fix.** Make `shutdown_ide` close any runtime dialogs and stop a running project before closing
the shell, and have it confirm the lifetime actually completed rather than returning on request. A result
that distinguished *requested* from *exited* would be enough on its own to make the failure legible.

---

## A carried file cannot be opened at all — FIXED

**Symptom.** A project's related documents (a `RelatedDoc=` in the `.vbp` — a README, a changelog, a
`.sql`) appear in the Project Explorer as `RelatedDocViewModel` nodes, and there is no MCP route to open
one. Every door is shut:

- The tree opens an item on **double-tap** (`ProjectToolView.axaml` → `TreeView_OnDoubleTapped` →
  `ProjectToolViewModel.OpenSelected()`), and `interact` has no double-click action.
- `interact select` on the `TreeViewItem` fails with *element does not support 'select'* — it reports only
  a `scroll` provider, so the row cannot even be **selected**, which `OpenSelected` reads.
- `OpenSelected()` is a plain method, not an `ICommand`, so `interact invoke_command` cannot reach it. The
  context menu's `ViewCodeCommand` handles forms and modules only.
- `open_file` and `add_file` are documented as forms and modules only, and reject a related document.
- `Project > Add File...` on an already-carried file does open it, but goes through
  `IStorageProvider.OpenFilePickerAsync` — a native Win32 dialog no MCP tool can drive.

**Consequence.** A whole editor type is unverifiable. This blocked the live check for #255: the point of
that change is attaching a language server for a file type HexIDE has no other support for, and a carried
`.md` is exactly that file. The configuration, the registry and the bundled server were all confirmable;
the foreign server's diagnostics rendering in an editor were not, and needed a human to double-click.

**Fixed** in the change that needed it. `open_file` now accepts a carried file by name, and
`get_project_info` lists them, so the whole editor type is drivable. That route was chosen over a
`double_click` action on `interact` for a reason worth recording: **it changes no tool's parameters**, so
it needed no schema change and therefore no session restart — the fix was usable in the session that
found the gap. `open_file` was already the "open this project item" tool; its refusing one kind of item
was the surprising part.

**Still open, and independent:** the `TreeViewItem` in the Project Explorer exposes no `selectionItem`
provider, so a row cannot be selected through `interact` at all. That blocks every context-menu path in
that tree, not just this one, and `open_file` does not help there. `interact` also still has no
double-click action, which is the general form of the problem.

---

## 9. `press_key` on a non-focusable container silently does nothing — **CLOSED** (2026-09-08)

> **CLOSED (2026-09-08).** Both of these were one root cause: `press_key` raised the event on a control
> the caller did not mean, and reported success either way. `type_text` and `press_key` now use separate
> resolvers — `press_key` takes the DEEPEST input surface (AvaloniaEdit's `TextArea`, not the `TextEditor`
> that wraps it), falls back to the first focusable descendant, and FAILS rather than succeeding when
> nothing under the target can take keyboard focus. The reply names the receiving control whenever it is
> not the one addressed, so a no-op is attributable. Pinned by `UiAutomationDriverTests.PressKey_*`.


**Symptom.** `press_key` against the document pane path returned `{"success":true,"detail":"pressed A"}` and
nothing happened — for **both** a read-only form and an editable one. Taken at face value that looks like
"read-only is working"; it is actually "the key went nowhere", and the two are indistinguishable from the
result.

**Cause.** `inspect_element` on that path shows `isKeyboardFocusable: false` and a `DocumentDock`
DataContext — the pane is a dock container, not the editor. The key is raised on a control that cannot take
focus, so nothing consumes it. `success: true` reports only that the event was raised.

**Workaround.** Do not infer "input was blocked" from a no-op `press_key`. Establish a positive control
first (the same key on a surface that *should* accept it), or verify at the model/file level instead.

**Suggested fix.** Have `press_key` resolve to the nearest focusable text surface the way `type_text`
resolves to the nearest editor, and report which control actually received the event — or return
`success:false` when the resolved target cannot take keyboard focus.

---

## press_key cannot reach a handler attached to TextArea, which is where the code editor's handlers live — **CLOSED** (2026-09-08)

> **CLOSED (2026-09-08).** Both of these were one root cause: `press_key` raised the event on a control
> the caller did not mean, and reported success either way. `type_text` and `press_key` now use separate
> resolvers — `press_key` takes the DEEPEST input surface (AvaloniaEdit's `TextArea`, not the `TextEditor`
> that wraps it), falls back to the first focusable descendant, and FAILS rather than succeeding when
> nothing under the target can take keyboard focus. The reply names the receiving control whenever it is
> not the one addressed, so a no-op is attributable. Pinned by `UiAutomationDriverTests.PressKey_*`.


**Symptom.** `press_key` reports `{"success":true,"mechanism":"keyboard","detail":"pressed F12"}` and nothing
happens. No exception, no log line, no visible effect — indistinguishable from a feature that is bound and
broken. It cost a wrongly-filed issue (hexide-io/HexIDE#325) and a round of diagnosis into the wrong layer.

**Mechanism.** `UiAutomationDriver.PressKey` resolves its target through `FindTextSurface`, which returns
the first **`TextEditor`** descendant, then raises `KeyDownEvent` on it. But AvaloniaEdit nests
`TextEditor` → `TextArea` → `TextView`, and `CodeEditorView` attaches its key handling to **`TextArea`**
(`CodeEditorView.axaml.cs:229`, `RoutingStrategies.Tunnel`).

A routed event raised on `TextEditor` tunnels *down to* it and bubbles *up from* it. `TextArea` is beneath
it in the tree, so it is on neither route. The handler cannot fire, however correct it is — and the tool
reports success, because raising the event did succeed.

So **every keyboard command in the code editor is undrivable at the obvious target**: F12 (go to
definition), Enter auto-indent, Shift+Alt+F (format), and the whole edit-while-running reset prompt.

**Workaround.** Address the `TextArea` explicitly. It is in `dump_visual_tree` as the `None` node with
`className: "TextArea"` under the editor's `PART_ScrollViewer`:

```
…/Pane[#0]/Custom/Pane[PART_ScrollViewer]/None
```

`FindTextSurface` returns a `TextArea` unchanged when handed one, so the event raises on the right element
and tunnel handlers fire. Verified: F12 at a call site then moves the caret to the declaration.

**Suggested fix.** `FindTextSurface` prefers `TextEditor` over `TextArea`
(`descendants.FirstOrDefault(c => c is TextEditor) ?? descendants.FirstOrDefault(c => c is TextArea)`),
which is right for `type_text` — inserting at the caret wants the editor's own API — and wrong for
`press_key`, which wants the innermost element so the route covers everything above it. The two tools want
opposite ends of the same chain, so the preference belongs at the call site rather than in a shared helper:
`press_key` should prefer the *deepest* text surface, `type_text` the outermost.

**The general lesson, which is the reason this is written down.** A synthetic `RaiseEvent` reproduces a real
keypress only for handlers on the target or its ancestors. Anything attached below the element the harness
picked is unreachable and reports success. When a keyboard-driven feature appears to do nothing, establish
that the handler is on the event's route **before** concluding anything about the feature.

## 4. Can't snapshot or drive the IDE while a form is running — **CLOSED** (#340, 2026-09-08)
> **Fixed.** Every window-addressing tool — `take_snapshot`, `dump_visual_tree`, `inspect_element`,
> `interact`, `type_text`, `press_key`, `hover` — takes an optional `window`: `"auto"` (the default,
> unchanged) or `"ide"`. The policy lives in `ForegroundWindow.Pick(scope, …)` beside the rule it
> qualifies, and `take_snapshot` now shares the resolver instead of repeating the selection inline.
>
> The diagnosis below was right, and its most useful part is the *negative* result: activation is not the
> lever. `set_window_state`, breaking before the form is shown, and `activate_document_tab` all succeed and
> change nothing, because the preference is in target *selection*. Three failed workarounds are why the fix
> is a parameter and not a focus call.
>
> **Measured while paused at a breakpoint**, which is the state the whole entry is about:
>
> ```
> dump_visual_tree()              → window: "Form1"   (VBFormRuntime — the old behaviour)
> dump_visual_tree(window:"ide")  → window: "MainWindow", "Project1 - HexIDE [run]"
> hover(<code editor>, window:"ide")
>                                 → tip: total = 42
> ```
>
> That last line is the **debugger's Auto Data Tip**, read as text — the thing gap 7 was filed for in P6c
> and could not verify. Confirmed on screen by the maintainer at the same moment.
>
> A `"form"` scope was considered and left out: `"auto"` already resolves to the running form, and an enum
> cannot say *which* form once a project shows more than one. An unrecognised value is named rather than
> quietly treated as `"auto"` — a caller passing this at all wants a window the default would not give them.


**Symptom.** While a VB6 program is *running* — including when the **interpreter is paused at a breakpoint** —
`take_snapshot` captures the `VBFormRuntime` window, not the IDE main window (`activeDialog` reports the form).
So the IDE's **paused-state editor** — the amber current-statement bar and the red breakpoint gutter as they
appear *during* a break — cannot be captured. (This is the inverse of gap #1: there the modal *over* the form is
invisible; here the *IDE behind* the form is.)

**How it bit.** Verifying the interpreter debugger (Phase 1): the pause was fully confirmable via
`get_debug_state` (Paused / module / 1-based line / reason) and the reveal was confirmable *after* `stop_project`
(the caret lands on the break line — `Ln 6, Col 1` in the status bar), and the red dot is snapshottable while
*not* running — but the **amber bar while paused** could only be confirmed by the **user watching the live IDE**.
The functional path is the same `Stopped`-event handler that `get_debug_state` reflects, so it's provable; the
one *pixel* needs a human.

**Also blocks DRIVING the IDE while a form runs (not just snapshotting).** `dump_visual_tree` walks the *active
window* — which is the running `VBFormRuntime` — so it never returns the IDE's code-editor control, and
`press_key` / `type_text` (which need a path from `dump_visual_tree`) therefore can't target the editor while a
form is up. **How it bit (Phase-3 E&C affordance):** the "edit code while running → VB6 reset-project prompt" can't
be triggered over MCP — sending an editor keystroke needs the IDE editor addressable, which it isn't while the form
is foreground. The prompt logic is VM-tested (`ConfirmResetWhileRunningAsync`, `IsProjectRunning`) and the run-state
(`IsSessionActive`) is runtime-tested, but the live keystroke→dialog step needs the **user** (or a fix below).

**How it bit (Phase-5 Call Stack window).** The populated Call Stack pane (and by extension the populated Locals
pane) while paused can't be pixel-snapshotted for the same reason. Two escape hatches were tried and **both fail**:
(1) breaking early in `Form_Load` **before** the form is shown does **not** hand the IDE the foreground — the
`VBFormRuntime` window already exists and is preferred the moment the run starts, even pre-`Show`; (2)
`set_window_state("Maximized")` on the IDE main window does **not** override `take_snapshot`'s form preference
(`activeDialog` still reports the form). So the earlier "break in `Form_Load`-before-show → IDE foreground" note is
**wrong** — the only reliable IDE-foreground state is `stop_project`, which clears the paused panes. The pane's data
was instead verified via `get_call_stack` (the exact model the pane binds), its behaviour via `step_over`/`step_out`
+ `get_debug_state`, its binding via VM tests, and its **chrome** (title + "Procedure"/"Line" headers) via a
post-`stop_project` IDE snapshot — only the *populated rows* pixel needs a human.

**Root cause.** Same window-selection logic as gap #1: the running form is a separate top-level window that
`take_snapshot` **and** `dump_visual_tree` prefer; there's no way to ask for the IDE main window specifically while
a form is up. Confirmed (P5) that `set_window_state` doesn't change which window is captured — the preference is in
the capture-target selection, not window Z-order/activation.

**And now `hover`, which is what makes this the blocker it is (2026-09-08).** The `hover` action closed the
editor half of gap 7, so **Auto Data Tips are the one paused-state feature that a tool could otherwise verify
outright** — not a pixel, but the tip's actual text. It cannot: `hover` resolves a path against the active
window like everything else, so with a form up there is no path to the code editor to aim at.
`activate_document_tab` was tried as an escape hatch and **also fails** — it succeeds, and the tools still
resolve against `VBFormRuntime` — which is a third confirmation, after `set_window_state` and the
break-before-`Show` attempt, that the preference is in target selection and not in activation.

So the "highest-value dev-server fix" note below is now understating it: this no longer blocks only *pixel*
verification of paused-state panes. It blocks a **textual, assertable** one.

**Workarounds used.** (a) `get_debug_state` for the pause fact; (b) a post-`stop_project` IDE snapshot to confirm
the caret-reveal + Immediate output + tool-pane chrome; (c) the user confirming live paused-state pixels
(amber bar, populated Locals/Call Stack rows).

**Fix consideration.** A `take_snapshot` **and** `dump_visual_tree` target/scope parameter
(e.g. `window: "ide" | "form" | "auto"`), so debugger/IDE-chrome verification can force the main window even while a
form runs. This is now the single highest-value dev-server fix — it blocks pixel-verifying *every* paused-state tool
pane (Locals, Call Stack, and future Watches/data-tips).

---

## 15. `dump_visual_tree` showed a hidden element as though it were on screen — **CLOSED** (#341, 2026-09-08)


> **Fixed.** A node carries `"isHidden": true` when it is in the tree but not on screen; the field is
> absent when it is showing. `inspect_element` reports the same on a single control.
>
> **Effective visibility, not the local flag.** A control can be `IsVisible` itself and still be invisible
> because an ancestor is collapsed, so reporting `Visual.IsVisible` would have moved the same trap up one
> level instead of closing it. `IsEffectivelyVisible` answers the question a caller is actually asking.
>
> **Null rather than false**, so the field is absent from the overwhelming majority of nodes: a dump runs to
> hundreds, and the hidden one is the exception worth spelling out.
>
> `isOffscreen` is left alone and still means what UIA means by it — clipping and scroll position. The two
> are genuinely different: a control scrolled out of view is *offscreen and visible*; a collapsed one is
> *visible to UIA and not showing*. Neither implies the other, which is why one more flag was the fix
> rather than a correction to the existing one.
>
> Verified against the case in the report below — the read-only banner on a form that is **not** read-only:
>
> ```
> "path": ".../Custom[Root]/Text[Read-only — HexIDE cannot yet reproduce this form faithfully, ...]",
> "isEnabled": true, "isOffscreen": false, "isHidden": true
> ```
>
> Its visible siblings in the same dump carry no `isHidden` at all. The entry had gone on misleading after
> it was written, too: the same banner appeared in every dump taken while investigating #334 and was read,
> again, as a form being held unsaveable.


**Symptom.** The read-only banner `TextBlock` appears in the tree for a form that is *not* read-only, with
`isOffscreen: false` and no other flag distinguishing it from a rendered element:

```
"path": ".../Custom[Root]/Text[Read-only — HexIDE cannot yet reproduce this form faithfully, ...]",
"isEnabled": true, "isOffscreen": false
```

The banner is bound to `IsReadOnly` and was collapsed. Nothing in the node says so.

**How it bit.** Verifying #152 against a brand-new UserControl. The tree said the read-only banner was
present and on screen, which would have meant a fresh, empty, perfectly reproducible `.ctl` was being held
unsaveable — a serious bug, and one entirely consistent with the change under test. It took a
`take_snapshot` to establish that nothing was rendered and the IDE was behaving correctly.

**Workaround.** Treat presence in the tree as "exists in the template", never as "visible". For anything
whose whole meaning is *whether it is showing* — banners, validation text, overlays, empty-state
placeholders — confirm with `take_snapshot`, or assert the bound view-model property via
`inspect_element` rather than reading the tree.

**Suggested fix.** Carry the real visibility on the node — `isVisible` from `Visual.IsVisible` (and ideally
`isEffectivelyVisible`, since an ancestor may be the one collapsed). `isOffscreen` is a UIA concept about
scroll position and clipping, and it does not answer this question. Without it the tree cannot be used to
assert the absence of a warning, which is exactly the assertion a fidelity gate needs.

## A runtime error dialog could be missed entirely, and its text could not be read — **CLOSED** (#342, 2026-09-08)


> **Fixed, in both halves.**
>
> *Its text* is readable because `inspect_element` now reports property values (see the entry below):
> `ErrorText` comes back as a string instead of pixels.
>
> *Missed entirely* is fixed by `get_last_runtime_error`, backed by a `RuntimeErrorLog` recorded **before**
> the dialog is shown and deliberately not tied to its lifetime. `run_project` clears the message, so a
> result belongs to the most recent run; the `sequence` is **not** cleared and only increases, because a
> caller needs to tell "no error this run" from "the same error again" — comparing message text cannot,
> since a loop can raise the identical error twice.
>
> Measured live, in the exact rhythm that used to destroy the evidence — raise, dismiss, stop, then look:
>
> ```
> run → End → stop_project → get_last_runtime_error
>   {"raised":true,"message":"Run-time error '11': Division by zero, at 1 / 0",
>    "at":"2026-09-08T04:12:13+01:00","sequence":1}
>
> (clean program) run → get_last_runtime_error
>   {"raised":false,"sequence":0}
> ```
>

**Symptom.** Running a project that raises a runtime error opens a modal dialog over the running form.
`take_snapshot` does report it (`"activeDialog": "HexIDE"`) and captures it — but only while it is still up.
`stop_project` and `shutdown_ide` both close open dialogs, so the ordinary automation rhythm of run → stop →
snapshot destroys the evidence before it is ever seen. A run whose form silently did nothing looks identical
to a run whose error dialog was dismissed a moment earlier.

Separately, once the dialog IS captured, its text is only readable as pixels. `inspect_element` on the
`RuntimeErrorView` lists `ErrorText` among the DataContext members but returns no value for it, so the
message has to be read off a PNG.

**Consequence.** This is how a real defect stayed hidden: a runtime error raised inside a `.bas` module was
being swallowed (the handler threw before it could show the dialog), and the automated symptom — a form that
runs and does nothing — was indistinguishable from success. It was found only because a human happened to
see a dialog flash on screen.

**Workaround.** `take_snapshot` BEFORE `stop_project`, always, on any run that might raise. Treat
`activeDialog` in the snapshot result as the signal — it names the dialog even when the image is hard to
read. To confirm a swallowed error, check the IDE log at
`%LOCALAPPDATA%\HexIDE\logs\ide\ide-*.log`; an exception thrown inside the error handler lands there.

**Suggested fix.** Expose string property values in `inspect_element`'s `dataContextMembers` (at least for
simple scalars), so a dialog's message is assertable rather than only legible. And consider a
`get_last_runtime_error` that survives the dialog being dismissed — the interesting state currently exists
only for as long as a modal is on screen.

## inspect_element listed a property but never its value — **CLOSED** (#342, 2026-09-08)


> **Fixed.** `dataContextMembers` now carries each property's current `value` where it can be read as text.
>
> **Only recognised types are called at all** — strings, primitives, enums, `decimal`, the date/time types,
> `Guid`, and AvaloniaEdit's `TextDocument`. Reading a property invokes a getter, which is arbitrary code;
> restricting it keeps the blast radius to reads that are ordinary. Anything else is listed exactly as
> before, with no value.
>
> **`TextDocument` earns its special case**, and is why this closes rather than half-closes: the Immediate
> window's contents are a `TextDocument`, not a string, so a scalars-only reader would have left the most
> useful text in the IDE unreadable.
>
> **Null means "not read", `""` means "empty".** Collapsing the two would make *no output yet* and *cannot
> see the output* identical, which is the failure this entry is about. A throwing getter yields null rather
> than failing the whole inspection, and a value over 4000 characters is truncated with a marker saying so —
> `Debug.Print` output has no natural limit.
>
> Measured live, with the Immediate pane only ~4 lines tall:
>
> ```
> ImmediateToolViewModel.Document  → "before
clean run
"   (both runs, not the visible strip)
> RuntimeErrorViewModel.ErrorText  → "Run-time error '11': Division by zero, at 1 / 0"
> ```
>

**Symptom.** `dataContextMembers` gives each member's name, type and `canWrite`, but no current value. Two
cases hit in one session: `ImmediateToolViewModel.Document` (the Immediate window's contents) and
`RuntimeErrorViewModel.ErrorText` (a runtime error message). Both had to be read by screenshotting and
squinting, and the Immediate window's buffer scrolls, so output beyond the visible ~4 lines is unreachable
without resizing the pane.

**Consequence.** Any assertion about text the IDE produced — program output, an error message, a status
line — degrades from a structured check to reading a PNG. That is slower, and it silently caps at whatever
the pane happens to show.

**Workaround.** Keep program output to one line per run so it fits the visible strip, and restart the IDE
between runs when the buffer needs clearing (there is no clear-Immediate action). Note that setting the
containing `ToolDock`'s `Proportion` via `interact set_property` reports success but does not re-run the
layout, so it does not actually enlarge the pane.

**Suggested fix.** Return scalar property values (string/number/bool) alongside the member list. A
`get_immediate_output` returning the Immediate buffer as text would remove the whole class of workaround.

## A MenuFlyout on a toolbar Button opened blind, and its items were invisible — **CLOSED** (#343, 2026-09-08)


> **Fixed — and the entry was wrong about why, in the direction that sends the next reader looking for the
> wrong fix.** Corrections first, because they were measured:
>
> - *"`interact invoke` ... does nothing visible — it fires the button's own invoke, which is not what opens
>   a flyout."* **False.** `Button.OnClick` opens the flyout; invoke has always opened it. What failed was
>   *seeing* the result.
> - *"`press_key Space` ... does not open it either"* and the heading *"cannot be opened"* — false for the
>   same reason.
>
> The single real cause: a `ContextMenu` and a `Button.Flyout` are attached with
> `ISetLogicalParent.SetParent`, so their `Popup` is never a **visual** child of anything and a visual walk
> cannot reach it however deep it goes. A menu-bar dropdown's popup *is* a visual child, which is why menus
> looked like they worked and these did not.
>
> `AttachedPopupOf` now finds a popup from its owner by all three routes — `Button.Flyout`,
> `FlyoutBase.GetAttachedFlyout`, and `Control.ContextFlyout` — and feeds it to the existing popup walk, so
> the items appear as children of the owner and their paths round-trip. `SnapshotComposer` shares the same
> definition, so a popup you can address is one you can also see. A control owning either now advertises
> `expandCollapse`, and `expand`/`collapse` open and shut it: the verb existing while nothing said so is how
> these eight commands came to be recorded as unreachable.
>
> Measured live, all the way through:
>
> ```
> interact invoke  Button[Standard.AddForm]        → success
> dump_visual_tree root=<that button>
>   → Pane/MenuItem[Form], MenuItem[MDI Form] (isEnabled:false), MenuItem[Module], … MenuItem[Add File...]
> interact invoke  …/Pane/MenuItem[Module]         → success
> get_project_info                                 → modules: ["Module1"]
> take_snapshot                                    → the flyout is in the picture
> ```
>
> Not merely observable: driven end-to-end. And `MDI Form` reporting `isEnabled:false` answers the
> Consequence's "not even *observed* to be enabled or disabled".


**Symptom.** The Add Item toolbar button (`Standard.AddForm`) carries a `MenuFlyout` holding eight
Add commands. None of it is reachable:

- `interact invoke` on the button reports success and does nothing visible — it fires the button's own
  invoke, which is not what opens a flyout.
- `interact expand` fails: the button advertises only an `invoke` provider, no `expandCollapse`.
- `press_key Space` on the button reports success and does not open it either.
- `dump_visual_tree(root=<button>)` returns the button with `children: []`, both with
  `interactiveOnly: false` and at full depth — the flyout's items are simply not in that subtree.
- A synthetic Win32 click at the button's rect (DPI-corrected, see below) did not open it either.

**Consequence.** Eight toolbar commands cannot be verified through MCP at all — not driven, and not even
*observed* to be enabled or disabled. Note the contrast that makes this easy to misdiagnose: menu-bar
dropdowns work fine (`interact expand` on `MenuItem[Project]` opens it, and its items appear in the tree and
in `take_snapshot`), so the obvious inference is that "menus work" — they do, but only the menu bar's.

**Workaround.** Verify the command, not the menu item. `inspect_element` on the button lists the
DataContext's members, so the presence of the bound command (e.g. `AddFileCommand`) is confirmable there,
and the same command can be driven end-to-end through its menu-bar twin where one exists. Say explicitly
that the toolbar entry was verified structurally rather than driven.

**Suggested fix.** Give a control that owns a `FlyoutBase` an `expandCollapse` provider, so `interact
expand` opens it and the popup's contents then become dumpable; failing that, an `open_flyout(target)`
action. Whichever route, the flyout's items need to reach `dump_visual_tree` — a popup that opens but
cannot be walked only moves the problem.

## A context menu opened but was invisible to both `take_snapshot` and `dump_visual_tree` — **CLOSED** (#343, 2026-09-08)


> **Fixed, same cause as the MenuFlyout entry above — one parenting problem wearing two faces, exactly as
> this entry predicted.** The Project Explorer's menu turned out to be a `MenuFlyout` on
> **`TreeView.ContextFlyout`** (Tools/Projects/ProjectToolView.axaml:125) — not a `ContextMenu`, and not on
> the `TreeViewItem`. Handling only `ContextMenu` and `Button.Flyout` left an open, plainly visible menu
> reporting nothing, which reads exactly like a menu that never opened, and was twice diagnosed that way
> before the AXAML was read.
>
> **The second half was lifetime, not visibility.** A popup opened over an UNFOCUSED owner light-dismisses
> almost immediately: `press_key Apps` does open this menu — watched on screen — but it is gone before the
> next MCP call can walk it, so every later look reported nothing. `interact expand` now focuses the owner
> before opening, and the menu stays up across calls. The same trap as the transient tooltip, one layer up.
>
> Measured live:
>
> ```
> interact expand  …/Custom/Tree                   → "opened flyout on 'Tree'"
> dump_visual_tree root=…/Custom/Tree
>   → MenuItem[Properties], MenuItem[Add] (expandCollapse), MenuItem[Print]
>     plus Set as Start Up / View Object / View Code, each isHidden:true
> take_snapshot                                    → Properties / Add ▸ / Print, in the picture
> ```
>
> The visible three match a photograph the maintainer took of this menu earlier the same day, and the
> `isHidden` flags — added the day before — are what separate them from the items that are merely present.
>
> **Still transient across calls.** Snapshot it in the call straight after opening: an intervening
> `dump_visual_tree` was enough for it to dismiss before the picture was taken.


**Symptom.** A context menu can be opened — and then neither tool can see it. `take_snapshot` returns the
window with no menu in it, and `dump_visual_tree` returns the ordinary window tree with no popup root. The
menu is plainly on screen the whole time.

**This is not a missing verb, and I recorded it as one until it was pointed out.** `interact` has no
right-click action, so the first conclusion was "a context menu cannot be opened at all". It can:

```
press_key(…/Custom/Tree/Pane/TreeItem, "Apps")
  → {"success":true,"detail":"pressed Apps on Border[SelectionBorder]"}
```

…opens the Project Explorer's menu (Properties / Add ▸ / Print). The keypress works. What fails is
*seeing* it, and the two failures look identical from the tool output — which is exactly how a capture
problem gets written up as an interaction problem.

**Cause.** Both tools discover popups by walking the **owner window's visual tree** for `Popup` children —
`SnapshotComposer.CollectOpenPopupRoots` (`:105-124`) and `UiAutomationDriver` (`:643`, `:683`). That
reaches a **menu-bar** popup, because `Menu` → `MenuItem` → `Popup` really are visual children, which is
why the fix for the closed gap 12 works. A `ContextMenu` is not: it is set as a *property* on the control
that owns it, so it is only logically parented and the walk never arrives.

Predicted, as it turns out: `artifacts/design/259-language-server-visibility.md` noted that these two call
sites "cross `Popup`s found via `GetVisualChildren()` and a flyout's popup may be only logically parented".
This is that, measured.

**A second oddity, recorded rather than explained.** A keyboard-invoked context menu is placed at the
pointer, and a synthetic key press carries no pointer position — so it opened at the last real pointer
location, which was over a *different application* entirely. Whether the placement contributes to the
capture failure or is merely untidy is not established: the composer never finds the popup at all, so it
never gets as far as caring where it is.

**Workaround.** `press_key` with `Apps` opens the menu, and its items can be invoked blind through
`interact invoke_command` on the owning DataContext if the command is known. So a context-menu *action*
can be driven; its *presentation* — that the right items appear, enabled, in the right order — cannot be
asserted at all.

**Suggested fix.** Enumerate open popups from the application's own top-level list rather than by walking
the owner window's visual tree; Avalonia tracks popup roots globally, and that reaches logically-parented
popups as well as visual ones. Both call sites want the same change, and closing it would also make the
`MenuFlyout` gap above (a flyout attached to a toolbar `Button`) tractable, since that is the same
parenting problem wearing different clothes.

A `context_menu(target)` action raising `ContextRequested` is still worth adding — `Apps` depends on the
target being focusable and on Avalonia's own key handling — but it is a convenience, not the blocker.

## 11. `add_control` mutated the designer but never persisted — **CLOSED** (#344, 2026-09-09)


> **Fixed.** `add_control` now saves the form, like its sibling `set_control_property` always did. The pair
> disagreed about whether a designer edit was durable, and the one that did **not** save is the one that
> creates things — which is why nine controls could exist, be reported as added, and not survive a crash.
>
> The save runs **on the UI thread**: a save first publishes `ApplyAllUnsavedChangesEvent`, whose handler
> reads AvaloniaEdit's `Document.Text` and throws off it, leaving the previous content to be written and
> reported as success (#334, closed the same week). And a refusal is **not** reported as success (#147): an
> unfaithful form comes back `success:false`, naming the control that is in the designer but not on disk,
> because a caller told "saved" would not find out otherwise until something else read that file.
>
> Measured live: `add_control(Form1, CommandButton, 40, 40, 100, 30)` → `Command0`, and the `.frm` on disk
> carried `Begin VB.CommandButton Command0` immediately, with no further call.


**Symptom.** Nine controls were added successfully via `add_control`; the IDE then crashed and **all of
them were lost** — `frmOrders.frm` on disk still held only the bare form. `set_control_property` saves;
`add_control` does not.

**Workaround.** For anything more than a couple of controls, author the `.frm` directly and open the
project — the format is small and well understood (`Left`/`Top`/`Width`/`Height` in twips, i.e. pixels
× 15, plus `Caption`/`Text`). That is also faster than one round trip per control, and it survives a
crash. Use `add_control` for interactive exploration, not for composing a form.

**Fix consideration.** Either save after `add_control` (consistent with `set_control_property`), or add
an explicit `save_form` tool so a caller can batch adds and commit once.

---

## 14. `set_file_content` silently dropped a form's `Attribute` header — **CLOSED** (#344, 2026-09-09)

> **The mirror half is superseded, 2026-10-13 (#273 task 3.18).** A whole `.frm` is no longer refused.
> `get_file_content` now returns a form's whole file, designer block included, so a whole `.frm` is what a
> caller round-tripping a form sends back, and refusing the shape would refuse what the read returned.
> `set_file_content` compares the incoming header with the form's instead: a designer block exactly as the
> form has it applies nothing and is accepted, and one that differs is refused with the reason and nothing
> changed. That meets the argument below more exactly than refusing the shape did. The controls a caller
> supplies are still never silently discarded, because a changed block is refused rather than dropped, and a
> designer block never reaches the code, where it would be compiled as VB. Keeping a form's `Attribute` lines
> when the content leaves them out is unchanged.
>
> **And a member's Attribute lines, since #273 task 4.5b (2026-10-14).** A change to the run under a procedure's
> or variable's declaration is refused the same way, with nothing changed, and the refusal names each member whose
> lines changed, were added or were dropped. Moving a run with its declaration, renaming it with it, or deleting
> it with it is not a change. Left as they are (#273's tasks.md, 4.5b): content that changes both the header and a
> run is told about the header alone, and a refusal says lines were removed or added where only what they describe
> changed.

> **Fixed, in both directions — the entry's case and its mirror (#338).**
>
> A form's leading `Attribute VB_*` block is now **kept** when the incoming content omits it, and the result
> says so rather than adjusting silently. Preserving beats refusing here: omitting the block is what a caller
> does when they mean exactly what they said — *replace the code* — and the block is not code.
>
> The mirror case is **refused**: a whole `.frm`, opening with a `VERSION` / `Begin` designer block, comes
> back as an error rather than being written into the code buffer where it is compiled as VB (#338).
>
> **The asymmetry with the module branch is deliberate.** A module's header carries nothing the model does
> not already own, so stripping it loses nothing. A form's designer block describes its **controls**, and
> this tool does not apply them — so accepting the file would either bury the header in the code or silently
> discard the controls the caller supplied. Strip where nothing is lost; refuse where something would be.
>
> Measured live, all three paths, checking the bytes on disk each time:
>
> ```
> set …  "Attribute VB_Name … / Private Sub Form_Load() … one"   → success
> set …  "Private Sub Form_Load() … two"        (no header)      → success + note
>        disk: Attribute VB_Name = "Form1" survives, body is "two"
> set …  "VERSION 5.00 / Begin VB.Form … "                       → refused; disk unchanged
> ```


**Symptom.** Writing a fresh body to a form removes its attribute block:

```
-Attribute VB_Name = "frmBillOfFare"
-Attribute VB_GlobalNameSpace = False
-Attribute VB_Creatable = False
-Attribute VB_PredeclaredId = True
-Attribute VB_Exposed = False
```

No warning, no error, and the change is written straight to disk — `hasUnsavedChanges` reads `false`
afterwards, because as far as the IDE is concerned the save succeeded.

**How it bit.** Using `set_file_content` to drop a few probe lines into `demo/bill-of-fare`'s form for a
live check. It replaced the whole code section, taking the header and every event handler with it, and the
damage reached a commit before it was spotted in `git diff`.

**Not a defect in the tool.** `get_file_content` returns the attribute block too, so the pair is
self-consistent and a **get → modify → set** round-trip preserves everything. The trap is that "the VB6
source code of a form" *includes* the `Attribute` header, which is easy to omit when replacing only the
procedures. VB6 hides it, but HexIDE showed it even before #273 made it part of the folded, read-only
header. The original entry incorrectly called it invisible in HexIDE's editor. `VB_Name` is load-bearing.

**Workarounds.** (a) Always `get_file_content` first and edit the returned text. (b) For a throwaway probe,
use a scratch project (`--newproject`) rather than a demo or a real one. (c) `git status` before committing
after any live verification — that is what caught it here, one commit late.

**Suggested fix.** Either preserve the `Attribute` block when the incoming content has none — the IDE knows
the form's name and can re-emit the header it just parsed — or refuse the write with "content is missing
the Attribute header; call get_file_content first". Silently accepting a body that destroys a form's
identity is the one behaviour that should not be available.

---

## The Project Explorer could be read in full and not driven at all — **CLOSED** (2026-09-14)

> **Fixed in the same change that found it.** Two independent holes that only bite together, and the
> reason a whole navigation surface was unreachable.
>
> - **Tree nodes advertised no selection.** Every `TreeViewItem` reported `providers: ["scroll"]` — no
>   `selectionItem`, no `invoke` — so `interact(<node>, "select")` failed with *"element does not support
>   'select'"*. `DescribeProviders` now advertises `selectionItem` for a `TreeViewItem`, and `DoSelect`
>   sets `IsSelected`, which the owning `TreeView` reflects into `SelectedItem` — the property a view
>   model is actually bound to. Exactly the shape of the `DataGridRow` fix directly above it.
> - **Nothing could double-click.** UI Automation has no pattern for a double-click — it is a gesture, not
>   a control contract — so `interact` had invoke / select / set_value / toggle / expand / collapse and no
>   way to express the one that opens a document. Added as its own verb, `double_click`, which raises
>   `DoubleTapped` on the target and lets it bubble to whichever container handles it. It **selects the
>   target first**, because a real double-click does, and a verb that skipped that would fire the handler
>   against whatever was selected before — the wrong document, silently. The reply says whether the
>   selection moved.

**The measurement worth keeping.** The two gaps were invisible separately and fatal together, and the
reflection fallback did not cover either: `invoke_command` needs an `ICommand` and the Project Explorer's
`OpenSelected()` is a plain method (reasonably — it is a gesture handler, not a bindable command), while
`set_property` coerces from a string and `SelectedItem` is an `Object` holding a live node no string can
express. So the surface read perfectly through `dump_visual_tree` and refused every attempt to act on it,
which is the worst shape a tool can have: a caller sees a tree it appears able to address and finds out
only on the failing call.

**How long it hid, and behind what.** `open_file` already carried a branch routing carried files straight
to `EditorService`, with a comment naming all three halves of this — no double-click, no selection
provider, `OpenSelected` not a command — as the reason it existed. That workaround made one editor type
reachable and left the root cause in place, so the next feature to depend on the gesture (the read-only
project document) hit the same wall. A bypass that resolves one symptom is worth writing down as a gap
even when it unblocks the task, which is what that comment did and why this was quick to place.

---

## `invoke_menu_item` could not reach an item whose access key is not its first letter — **CLOSED** (#501, 2026-09-22)

> **Fixed** by hexide-io/HexIDE#519, which strips the access-key underscore wherever it falls (`MenuPath`,
> `IDE/HexIDE/Automation/`) and makes a miss name the menu it looked in and every item that menu holds.

**Symptom.** `invoke_menu_item(path: "Project/Add Module")` answers
`{"success":false,"error":"Menu item 'Add Module' not found"}`. The item is plainly there:
`dump_visual_tree` after an `expand` lists it as `MenuItem[Add Module]` with an `invoke` provider, and
invoking it that way works. The top-level `Format` menu fails the same way, so `Format/Align/Lefts` never
gets past its first segment.

**Cause.** The resolver matches each segment against the item's raw header, and strips only a **leading**
access-key underscore. `Add _Module` and `F_ormat` carry theirs mid-word, so no spelling a caller would type
matches them. The resolver walks the menu's logical `Items`, which are complete whether or not the menu has
been opened.

**This entry first recorded the wrong cause**, and it is worth keeping as the trap it was: it blamed
Avalonia for not realising a closed menu's children, on the evidence that a dump of the closed menu bar
shows every item with `"children":[]`. That is true of the *visual* tree `dump_visual_tree` walks and
irrelevant to the logical tree the resolver walks. Corrected by the session working hexide-io/HexIDE#501.

**Why the reply misleads.** The tool's description warns that built-in items using routed commands "may not
execute correctly", so the natural reading of a failure is that the item was found and its command would
not run. It was never found, and "not found" cannot tell a caller a wrong path from a spelling the matcher
does not normalise.

**Workaround.** The generic trio: `dump_visual_tree`, `interact` with `expand` on the top-level item,
`dump_visual_tree` scoped to it, then `interact` with `invoke`.

---

## Insert File opened a native picker that `answer_next_file_dialog` could not answer — **CLOSED** (#532, 2026-09-22)

> **Fixed** by moving Insert File into `CodeEditorViewModel.InsertFileAsync`, which asks
> `IWindowManager.OpenFilePickerAsync`: the picker every other file dialog in the IDE uses, and the one an
> armed answer replaces.

**Symptom.** With an answer armed, `invoke_menu_item("Edit/Insert File...")` answered `{"success":true}`
and nothing was inserted. `clear_file_dialog_answers` then reported the answer still pending, which reads
as "the command never ran". It had run. Each attempt left a real native "Open Text File" picker open, three
of them by the time anyone looked, and they were found only by listing the process's top-level windows.
The automation server kept answering throughout, so nothing on the MCP surface showed that a modal dialog
was waiting.

**Cause.** `CodeEditorView.InsertFile` called `TopLevel.StorageProvider.OpenFilePickerAsync` itself, the
only picker in the IDE that did, so it never reached the hook in `WindowManager` that
`answer_next_file_dialog` arms.

**The wrong reading, recorded because it is the natural one.** The tool's description warns that routed
commands "may not execute correctly", and Insert File is a routed command, so a success with no effect
looked like exactly that. The next two attempts went into putting keyboard focus in the editor first. The
pending answer was the misleading evidence: it proved the IDE's picker had not been asked, not that the
command had not run.

**How to tell next time.** A native picker is invisible to every tool here. If an armed answer survives an
action that should have consumed it, list the process's visible top-level windows (a native dialog appears
there under its title) before concluding that the command did nothing.

---

## A scrolled tool window can only be verified down to its first screenful — **CLOSED** (#361, 2026-09-22)

> **Fixed, both halves, each measured on the running IDE.** `interact` now has a `scroll` action (`value`
> = `up`/`down`/`left`/`right` for a page, `line_up`/`line_down`/`line_left`/`line_right`, `home`/`end`).
> It moves the target, or the nearest control containing it that can scroll along the requested axis, so a
> caller can aim at the content it wants more of rather than at a `ScrollViewer` that is often a template
> part. Driven against the Properties list: `scroll down` aimed at a property *row* paged the list to
> 28.8%, and the snapshot showed rows below the fold. The reply says where the scroller now is, and a
> scroll that cannot move (`nothing to scroll vertically`, `already at that end`) fails rather than
> reporting a success that changed nothing.
>
> **The "cannot read the tab's content" half was never real, and neither was "cannot scroll" (#362).**
> The entry's attempt ran `dump_visual_tree` with its defaults, and `interactiveOnly: true` filters out
> plain text. With `interactiveOnly: false, maxDepth: 14` the same document returned every field of every
> server card; `inspect_element` on one card returned its whole `LanguageServerRowViewModel`, including a
> card below the fold; and `interact invoke` on the tab's own `PART_PageDownButton` already scrolled it,
> moving that card from y = 870 to 607. So before #361 scrolling was possible but roundabout, and
> reading was possible all along. What #361 added is a direct `scroll` action.
>
> An earlier version of this note said the reading half "had already closed" before #361. That was wrong
> in the same way as the entry: it read a working result as a fix, when nothing had been broken. The entry
> below is kept as written, because the mistake in it is the useful part.
>
> **What the entry did not notice.** `dump_visual_tree` was already *advertising* a `scroll` token, on 42
> nodes, while `interact` had no such action, so the capability this entry asked for was being promised
> and refused at the same time. Those phantom tokens also kept 40 otherwise-inert nodes in the default
> tree. With the action added and the token reported only where something would actually scroll, the
> default tree of a fresh Standard EXE project went from 256 nodes to 198.


**Symptom.** The Language Servers window (#259) lists every attached server, one card each, so its content
is routinely taller than the pane. `take_snapshot` captures what is painted, and there is no way to scroll
the content, so every server below the fold is unverifiable. Hiding a bottom-docked tool via
`set_tool_window_visible` buys one more card and no more.

**What was tried.** `press_key` with `End`/`PageDown` needs a `target` path, and the only addressable node
in that region is the `TabItem` — pressing a key there does not reach the `ScrollViewer` inside the tab's
content. `dump_visual_tree` returns the tab chrome (the `TabItem`, its close `Button`, its header `Text`)
but not the realised card content beneath it, so the values could not be asserted structurally either.
Two shapes of the same limit: the content of a document tab is neither drivable nor readable.

**Consequence.** Verification stopped at "the first two groups render correctly, with real servers and the
right fields". The case that most wanted checking — a server further down the list whose row shows
`Running` with nothing advertised — was covered by a view-model test instead. That is a reasonable place
for it, but it means the *rendering* of the most diagnostic row in the window is unverified, which is
exactly the substitution the visual-verification rule exists to prevent.

**Workaround used.** Assert the projection in `LanguageServersToolViewModelTests` and snapshot only what
fits. Note this is weaker than it sounds: a binding typo renders an empty row and passes every view-model
test.

**Suggested fix, cheapest first.** A `scroll` action on `interact` (`value` = `up`/`down`/`home`/`end`,
resolving to the nearest ancestor `ScrollViewer`) would close it for every scrolled surface at once, not
just this one — the Object Browser, the Translation Editor and the Locals tree have the same shape. Failing
that, `take_snapshot` could accept an optional element `path` and capture that element at its full desired
size rather than clipped to the viewport, which would also make long content diffable.

---

## 6. Can't open a Debug-menu dialog (e.g. Add Watch) via MCP to snapshot it — **CLOSED** (#362, 2026-09-22)

> **Closed, measured on the running IDE (#362).** `invoke_menu_item(path: "Debug/Add Watch...")` opened the
> dialog, `take_snapshot()` captured it (`activeDialog: "Add Watch"`), and `dump_visual_tree()` listed its
> expression box, three radio buttons and OK/Cancel, each drivable. The row half is closed by the tree
> fallback in `interact select`: a `TreeViewItem` is now reported with `selectionItem`, and selecting one
> sets the owning tree's `SelectedItem`. That path was driven on a Project Explorer node in the same pass,
> not on a Watches row.

**Symptom.** Verifying debugger P6a's **Add Watch** dialog rendering couldn't be driven through MCP. The dialog opens
from a Debug-menu item / keyboard shortcut backed by a *routed* command (AvaloniaLabs CommandManager), not a VM
`ICommand`: `interact invoke_command AddWatchCommand` finds no such property on `MainViewViewModel`; `interact invoke`
on `MenuItem[Debug]` reports "element does not support 'invoke'" (a top-level menu exposes no invoke provider until
opened, and its children aren't in the tree until then). The Watches rows render as `TreeViewItem`s but don't
advertise a `selectionItem` provider, so selecting a row to enable `EditWatchCommand` (which opens the same dialog)
wasn't reachable either.

**How it bit (debugger P6a).** The Watches *window* was fully verified live — it renders (columns + rows in
`dump_visual_tree`, snapshot after Stop) and evaluates correctly while paused (`get_watches`: x=42, s=hello, x*2=84,
arr expandable). Only the **Add Watch dialog's** live render couldn't be reached; deferred to P6b (where its
Break-type radios become functional and it's exercised again), backed meanwhile by the dialog-VM unit tests.

**Fix consideration.** Best: (a) surface a `selectionItem` provider on tool-window TreeView rows so `interact select`
can set the selection (unlocking the row's context-menu commands like Edit/Delete); also useful: (b) make a top-level
`MenuItem` invoke open the menu and realize its children, or (c) a thin MCP action to open a named debugger dialog.

---

## A Project Explorer node that is not a form or module cannot be selected or opened — **CLOSED** (#362, 2026-09-22)

> **Closed (#362).** A Project Explorer node is now reported as `TreeItem` with `selectionItem`, and
> `interact(target: ".../Custom/Tree/Pane/TreeItem/TreeItem", action: "double_click", window: "ide")` on
> Form1's node answered `double-clicked 'TreeItem' (selected it first, as a real double-click does)` and
> activated its designer. `open_file` also resolves carried files by name or filename
> (`HexIdeTools.OpenFileAsync`; the filename half only from #548, which found it matched the name alone). **Not driven in this pass:** a carried-file node specifically. The project
> had none, and `add_file` cannot create one. The route is the same one that worked on the form node.

**Symptom.** There is no way to drive "select this tree node, then open it" for any node kind beyond forms
and modules. Verified against a related-document node (a file the project carries but does not compile):

- `interact select` on its `TreeViewItem` → `element does not support 'select'`; the item exposes only a
  `scroll` provider, no `selectionItem`. (Since #361 a tree node reports no `scroll` either: nothing
  behind its peer scrolls, so the token was a lead that went nowhere.)
- `press_key` on the `TreeView` does nothing useful — `inspect_element` reports the tree as
  `isKeyboardFocusable: false`, so arrow keys never reach a selection model.
- `interact set_property` cannot help either: `ProjectToolViewModel.SelectedItem` is typed `Object`, and the
  reflection fallback coerces a **string** to the property type. There is no way to name an existing
  view-model instance as a value.
- `open_file(name)` opens a form or module by name only, so it cannot reach anything else.

**Consequence.** The double-click-to-open gesture is unverifiable through MCP for any new node kind. That
matters because CLAUDE.md requires a UI feature be confirmed against the running IDE, and here the *tree*
can be confirmed by snapshot while the *gesture* cannot be driven at all.

**Workaround.** Split the verification and say which half was driven. The node's presence, icon and caption
are visible in `take_snapshot` and assertable via `dump_visual_tree` (the node reports its
`dataContextType`, so its type is checkable). The routing — selection to the right editor — and the editor's
own behaviour are covered by tests instead. State plainly that the gesture itself was not driven.

**Suggested fix.** Either surface a `selectionItem` provider on tree items so `interact select` works, or
add a selection-by-path action (`interact select_node` taking the same `path` the tree dump already
returns). The second is probably better: paths are already the addressing scheme everywhere else in this
server, and it would work for every current and future node kind rather than needing a provider per
control. A narrower `open_file` that accepts any project member would help too, but would not fix
selection, which is what several context-menu commands key on.

---

## get_project_info omits every project member that is not a form or module — **CLOSED** (#362, 2026-09-22)

> **Closed (#362).** `get_project_info` returns `relatedDocuments` beside `forms` and `modules`
> (`ProjectInfoResult`), seen live on 2026-09-22 as `"relatedDocuments":[]`. The suggested single
> `members` array was not adopted, so the next member kind will need this edit again.

**Symptom.** `get_project_info` returns `forms` and `modules` only. A project carrying a related document (a
file it does not compile) reports it nowhere, so after adding one the tool's output is byte-identical to
before.

**Consequence.** The obvious check after an add — call `get_project_info` and see the new member — silently
answers "nothing happened" for a whole member kind. It reads as a failed feature rather than a blind tool.

**Workaround.** Confirm through the Project Explorer instead: `take_snapshot` shows the node and its icon,
and `dump_visual_tree` reports the node's `dataContextType`, which distinguishes a related document from a
module.

**Suggested fix.** Add a `relatedDocuments` array, and prefer a shape that will not need this edit again the
next time a member kind is added — a single `members` array of `{name, kind, path}` would cover forms,
modules, related documents and whatever follows.

---

## `list_lsp_messages` described a vocabulary it does not use, and promised data that was not there — **CLOSED** (#400, 2026-09-22)

> **Closed by the guard this entry asked for.** A tool whose reply renders an enum now carries
> `[DescribesEnum(typeof(T))]`, and `ToolDescriptionEnumTests` fails the build when its description leaves
> a member out. Opting in the four tools that render one found sixteen more members no description named:
> `get_debug_state`'s stop reasons, `get_watches`' watch types, `get_diagnostics`' severities (which it
> never mentioned at all), and `list_lsp_messages`' outcomes. All are now described. The check is for
> omission only: a stale name left in prose cannot be told from an ordinary capitalised word, but a
> renamed member is still caught, because its new name is missing.

**Symptom.** The tool's own description enumerates what a `kind` can be, because a caller who reads
`Unconsumed` in a reply has no other way to learn what it means. `ConversationEntryKind` has **nine**
members; the description named **seven** of them, and got one of those seven wrong:

- **`StandardError` and `Note` were absent entirely.** A caller shown `"kind": "StandardError"` had been
  told the set and it was not in the set, which reads as a bug in the tool rather than a gap in the
  sentence.
- **`Lifecycle` was described as "a process starting, stopping, its standard error and exit code".** It
  covers none of standard error and, at the time the sentence was written, no exit code either — nothing
  in the tree read one. So the description was the only place in the repository claiming the record held
  data it did not hold, and a caller who trusted it would have concluded the *server* was silent.
- **`direction` said "Sent, Received, or Local for the entries that are not messages at all"**, which
  puts every non-message under `Local`. A standard error line is `Received`, and the vocabulary's own
  definition says so.

**Why it survived.** The two omissions were kinds the enum had and the wire never produced — `StandardError`
because nothing raised it, `Note` because the only path to it was an undecodable frame that the recorder
never saw. A description drifts exactly where the code is unreachable, so the sentence agreed with the
observable behaviour and disagreed with the design. Nothing checks a `[Description]` string against the
enum it enumerates; the coverage guard that would have caught it is the one this repository applies to
`docs/lsp-client.md` and to the language packs, and tool descriptions have no equivalent.

**Fixed** by rewriting both sentences to the full set of nine — four for wire traffic, five that are not
messages — and to what each kind actually carries. The
underlying data gaps were closed in the same change: standard error and the exit code now reach the record
(`ILspTransport.Notice`), and an undecodable frame is recorded as a `Note` with its bytes rather than
dropped. Filed as hexide-io/HexIDE#400 for the general problem — a tool description that enumerates
a C# enum should be guarded against it, the way the LSP coverage table is guarded against the
specification.

---

## `add_control` on a document with no file opened a native picker and stopped the whole server — **CLOSED** (#514, 2026-09-22)

> **Fixed** by hexide-io/HexIDE#539, which refuses before anything is placed: a form with no file and no
> armed answer gets a reply naming `answer_next_file_dialog`, and the description now says so beside the
> sentence about saving. An armed answer, including an armed cancel, lets the save go ahead as before.
> `set_file_content` and `set_control_property` refuse a pathless document outright but only after the
> edit, with advice that leads back to the picker; that is #538.

**Symptom.** `add_control` on the `Form1` of a project created through `File > New Project` never returned.
The call was moved to the background after 120 s and every later MCP call hung behind it. Nothing on the
tool said a save was involved, let alone one that could stop the server.

**Cause.** `add_control` "places a control ... and SAVES the file" — its own description says so. A form with
no path takes the save picker (`ProjectService.WriteFormToDisk`), and a modal native dialog is outside the
control tree: the server cannot answer while one is up, which `answer_next_file_dialog` already documents
for the flows built around it. The two facts are each written down; the consequence of combining them is not.

**Why it misleads.** The save is a clause at the end of a sentence about placing a control, and it is
harmless on every form that has a file — which is every form the dev loop normally reaches, because a demo
project is opened from disk. The one case that hangs is the one a caller reaches deliberately when testing
the pathless state, having just been told by the entry above that it is hard to reach at all.

**Workaround.** Arm `answer_next_file_dialog` immediately before the call: with a path to let the save
happen, or with none to answer it as cancelled, which leaves the document pathless and is what a test of the
pathless state wants. The reply is then honest and useful —
`{"success":false,"controlName":"Command0","error":"'Command0' was added to the designer but the save
failed: The operation was canceled."}` — naming the control that is in the designer and not on disk.

**Suggested fix.** Any tool whose implementation can reach a file picker should say so in its description,
in the same sentence as the save, and name `answer_next_file_dialog`. Better still, refuse rather than block:
a save that would open a picker with no answer armed could return "this document has no file; arm
`answer_next_file_dialog` first", which is a reply instead of a hang. The list of such tools is knowable —
they are the ones that reach `SaveForm` or `SaveModule` with a null `AbsolutePath`.

## `invoke_menu_item` misses items with `_` in their text, and describes menus it cannot read (#544) — **CLOSED** (#560, 2026-09-22)

> **Closed (#560).** `MenuPath.Resolve` matches a segment as typed first and with its access key
> stripped second, so `File/Remove My_App` reaches the item showing that text. An `ItemsSource`-backed
> submenu such as Recent Projects is resolved through each entry's view model (`Header`, `Command`,
> `CommandParameter`), so its entries are listed and run without the submenu being opened first. A hidden
> item is neither listed nor run, at any level of the path, and the reply names it as hidden instead.
> Pinned by three `MenuPathTests`, each failing with its fix undone. Still open: a trailing `...` has to be
> typed (#564), and an entry built from a view model is always treated as visible.

**Symptom.** Three, all from #519's resolver (`MenuPath`). (1) An item whose displayed text contains an
underscore cannot be invoked: with a project named `My_App`, `invoke_menu_item(path: "File/Remove My_App")`
misses, because the caller's segment is stripped of an access key as if it were a header and becomes
`Remove MyApp`. The miss then lists `Remove My_App` among the items the menu holds. This worked before #519.
(2) `invoke_menu_item(path: "File/Recent Projects/anything")` answers `No item 'anything' in menu 'Recent
Projects'. It has no items.` while Recent Projects is visible, which it only is when it has entries: its
items come from `ItemsSource` and are not `MenuItem`s. (3) `invoke_menu_item(path: "Tolls/Options")` lists
`Go to github repo`, which is hidden on desktop, and invoking that path opens the browser.

**Workaround.** For (1) and (2), open the menu with `interact` `expand`, then `dump_visual_tree` scoped to
it, then `interact` `invoke` on the item: the realised containers carry the displayed text. For (3), check
`isHidden` in a dump before trusting a listing.

**Fix.** In the issue: match the segment as given, resolve item containers as well as `MenuItem` children,
and skip hidden items.

---

## `interact scroll` misses a target's own scroller, and `set_range_value` detaches a scroll bar (#545) — **CLOSED** (#566, 2026-09-22)

> **Closed (#566).** `scroll` now looks in the target's own template before walking upward, so aiming at
> an editor or a `DataGrid` scrolls it. `set_range_value` on a `ScrollViewer`'s bar moves the viewer's
> `Offset`, and any other range control goes through `SetCurrentValue`, so the template binding survives
> and a later `scroll` keeps the bar in step. The `DataGrid` case read from the code was real: setting its
> bar moved the bar and not the rows. A grid is now moved with the public `DataGrid.ScrollIntoView`, a
> whole row at a time, and the reply gives the offset it landed on beside the one asked for. A grid's
> horizontal bar has no such route and is refused. `NaN` and infinities are refused. Known limit: rows are
> located through `ItemsSource`, so a grid the user has sorted by a column can land on a different row
> from the one asked for; the reply still reports where it actually landed.

**Symptom.** (1) `interact(target: "…/Custom[Root]/None[TextEditor]", action: "scroll", value: "down")` on a
141-line code window answers `nothing to scroll vertically: neither 'TextEditor[TextEditor]' nor anything
containing it has content taller than its viewport`. The same call on `…/None[TextEditor]/Pane[PART_ScrollViewer]`
scrolls. The scroller is a template part inside the target, and the search only walks upwards. (2)
`interact(target: "…/Pane[PART_ScrollViewer]/ScrollBar[PART_VerticalScrollBar]", action: "set_range_value",
value: "2391")` moves the view and the thumb together. Afterwards,
`interact(target: "…/Pane[PART_ScrollViewer]", action: "scroll", value: "home")` puts the text back at line 1
and leaves the thumb at the bottom for the rest of the session: the local value outranks the template
binding. From reading, not yet reproduced: a `DataGrid`'s scroll bars take `set_range_value` without
scrolling, and `NaN` is accepted.

**Workaround.** Aim `scroll` at the `PART_ScrollViewer` inside the control, never at the control itself. Do
not use `set_range_value` on a `ScrollViewer`'s scroll bars; `scroll` with `home`/`end`/a page does the same
without breaking the binding.

**Fix.** In the issue.

---

## `inspect_element` does not show a range control's value (#550) — **CLOSED** (#561, 2026-09-22)

> **Closed (#561).** `inspect_element` reports a `range` object for any control with a range provider:
> `value`, `minimum`, `maximum` and `isReadOnly`. Seen live on a code window's vertical scroll bar as
> `{"value":0,"minimum":0,"maximum":905.39,"isReadOnly":false}`, and as `"value":400` after
> `set_range_value` with `400`.

**Symptom.** `inspect_element(target: "…/Pane[PART_ScrollViewer]/ScrollBar[PART_VerticalScrollBar]")`
reports `"providers":["rangeValue"]` and no value, minimum, maximum or read-only flag, though its description
promises the "current selection/value/toggle state". The bounds could only be learned from a refusal
(`set_range_value` with `999999` answers `… outside 'PART_VerticalScrollBar''s range 0..2391.59375`), and
whether a value took only from a snapshot.

**Workaround.** Probe with an out-of-range `set_range_value` for the bounds, and `take_snapshot` to see the
result.

**Fix.** Add the range provider's `Value`, `Minimum`, `Maximum` and `IsReadOnly` to the inspection.

---

## `add_watch` turns an unrecognised `watchType` into `Expression` without saying so (#546) — **CLOSED** (#556, 2026-09-22)

> **Closed (#556).** An unrecognised non-empty `watchType` is refused and nothing is added. The error names
> the accepted values: `'BreakWhenTru' is not a watch type, so no watch was added. Use Expression (the
> default), BreakWhenTrue or BreakWhenChanged.` Null, empty and whitespace still mean `Expression`. The
> tool now carries `[DescribesEnum(typeof(WatchType))]`, so its description is guarded against the enum.

**Symptom.** From reading `HexIdeTools.AddWatchAsync`: `add_watch(expression: "x > 5", watchType:
"BreakWhenTru")` falls through the `switch` to `WatchType.Expression`, adds a watch that will never break,
and replies with the watch list as if the call had done what was asked.

**Workaround.** Read `watchType` back from the reply's list after adding.

**Fix.** Refuse an unrecognised non-empty value and name the accepted spellings.

---

## A taken `--server-port` exits without running the shutdown handlers (#547) — **CLOSED** (#559, 2026-09-22)

> **Closed (#559).** The forced exit now runs, by hand and in order, the cleanup `ShutdownRequested` would
> have run. The add-in loader is disposed first, so a consent dialog closed by the exit records nothing;
> then the server context is cancelled and disposed. A throw from any of that is logged and the exit still
> happens with its code, and the log is closed after the windows. `ConsentAtShutdownTests` pins both
> directions at the registry: a late refusal records nothing, and a late Allow neither records nor loads.
>
> **The entry's premise was partly wrong.** Avalonia's forced `Shutdown` still closes every window, so the
> main window's `Closing` handler runs and saves window state and layout; it only loses its veto. A failed
> launch therefore still writes the profile it shares, which is part of #557.
>
> **Regression coverage (#563).** `DesktopStartup` now delegates its posted cleanup to the internal
> `ForcedExitCleanup` helper. `ForcedExitCleanupTests` pins loader disposal before exit, exit code 3
> despite cleanup failures, and log closure after exit; deleting or reordering those steps fails the tests.

**Symptom.** From reading `DesktopStartup.cs`: exit code 3 (#525) goes through Avalonia's forced
`desktop.Shutdown(exitCode)`, which does not raise `ShutdownRequested`. The add-in loader is never disposed,
and a third-party add-in's consent prompt, opened from the same `MainWindow.Opened` as the server start, can
then be recorded as **Block** when the closing dialog returns. The IDE's own shutdown work and the server
context's disposal are skipped too.

**Workaround.** None needed for automation itself: the exit code is right. Don't launch into a taken port:
check that `/health` stops answering before relaunching, as the rebuild cycle says. A fresh `--user-data-dir`
does not help. A new profile asks about every third-party add-in again, which makes an open consent prompt
more likely, not less.

**Fix.** Exit through `TryShutdown(exitCode)`, or run the cleanup before the forced shutdown.

---

## The name-taking tools saw one project, could not read a carried file, and refused without saying why — **CLOSED** (#572, #573, #575, 2026-09-22)

> **Fixed.** `open_file`, `view_designer`, `get_form_controls`, `get_file_content` and `set_file_content`
> resolve a name across every loaded project, as the mark tools already did, and take the same optional
> `project`. `get_file_content` reads a carried file, from its editor or else from disk, and
> `set_file_content` refuses one by what it is. A refusal says what the name is when it is something else,
> and lists what each project holds.

**Symptoms**, all measured on a group of two projects, `Carried` (`Module1`, carried `Notes`) and `Second`
(`Form1`, `Module1`, `Module2`), with `project` left at null unless shown:

- `open_file {"name":"Module2"}` refused while `get_breakpoints {"name":"Module2"}` answered for
  `Second/Module2`. The five tools looked only in the startup project (#575).
- `get_file_content {"name":"Notes"}` answered "No form or module named 'Notes' found" straight after
  `open_file` had opened it in a tab (#572).
- `view_designer {"name":"Module1"}` answered "No form or UserControl named 'Module1' found in the project".
  That reads as a wrong name, when the name was right and the tool was not, and like every other miss it
  never said what the project did hold (#573).

**Now:**

```
view_designer {"name":"Module2"}              → 'Module2' is a module, which has no designer. open_file opens its code.
view_designer {"name":"Notes"}                → 'Notes' is a carried file, which has no designer. open_file opens it.
get_form_controls {"formName":"Module1"}      → 'Module1' names more than one document: Carried/Module1, Second/Module1. Pass `project` to say which.
open_file {"name":"Module1","project":"Second"} → success; the tab is "Second - Module1 (Code)"
get_file_content {"name":"Notes"}             → {"content":"# Notes\r\n\r\nA carried file.\r\n","hasUnsavedChanges":false}
open_file {"name":"Nope"}                     → No form, module or carried file named 'Nope' in any loaded project. Carried holds no forms and UserControls; modules Module1; carried files Notes. Second holds forms and UserControls Form1; modules Module1, Module2; no carried files.
```

**Since closed.** `get_project_info` reported only the startup project, which left a refusal as the only
reply that showed the whole group; it now lists every loaded project in `projects` (#581).

**Three tools this did not reach, closed 2026-10-13 (#273).** `set_control_property` still looked in the
startup project only. `move_control` and `add_control` matched the open designers by name and took the first,
so with a group holding two `Form1`s they acted on whichever designer happened to be found first, and never
refused. All three now resolve a name as the five above do, take the same optional `project`, and refuse an
ambiguous name with the candidates listed. `move_control` and `add_control` then look for the designer open on
the document the name resolved to, so a bare `Form1` that used to work while only one of two was open is now
refused, as it is everywhere else. `add_file` takes `project` too, naming the project to add to. The rule moved
out of the tool bodies into `NamedDocuments` in the IDE assembly, where `NamedDocumentsTests` covers the
refusals quoted above. That the tools use it is checked by `ToolDocumentNamingTests`, which reads their source:
a tool that takes `project` must find the name through the lookup and use it for nothing else, so a designer
found by its form's name, which is how `move_control` worked, fails the build.

---

## A mark tool accepted lines the document does not have, and persisted them — **CLOSED** (#569, 2026-09-22)

> **Fixed.** `set_breakpoints` and `set_bookmarks` now refuse the whole call when any line is outside the
> document, name the lines and the valid range, and change nothing:
> `set_breakpoints {"name":"Module1","lines":[1,3]}` on a two-line module answers `3 is not a line of
> Carried/Module1, which has 2 lines: breakpoints are numbered 1..2. Nothing was changed.` The bookmark
> refusal adds "counting from 0", because the two tools number lines differently.

**Symptom.** `set_breakpoints {"name":"Module1","lines":[-1,0,99]}` (`project` left at null) on a two-line
module answered `{"success":true,"note":"Carried/Module1 now has breakpoints on -1, 0, 99."}`, and
`get_breakpoints` read the same three back. `set_bookmarks` did the same. The gutter showed none of the
breakpoints, because none is a line (breakpoints count from 1). The marks survived a relaunch too: they had
been written to the project's `*.user.hexproj` sidecar.

**Why it mattered.** The reply and the readback agreed with each other, and both were wrong. A caller who
gave bookmark numbering to the breakpoint tool, or the other way round, got a confident success and then a
run that never broke.

**Still open.** A sidecar that already holds such lines is loaded as-is, and a breakpoint on a line with no
executable statement is still accepted. Both are noted on #569.

**Since then.** The sidecar half was closed by #574's fix (#620): a mark on a line its document does not have is
dropped at load, and logged (`UserSidecarService.WithinDocument`). #273 then changed what the range counts and what
else is refused (task 3.12, 2026-10-01). The range counts from the top of the file, header included, and the
refusal says so: `… breakpoints are numbered 1..N, from the top of the file. Nothing was changed.` A line of the
header or of a member's Attribute lines is refused too, saying where the code starts (`MarkLineRules.Refusal`), and
so are `run_to_cursor` and `set_next_statement` aimed at one; a sidecar's mark on such a line is dropped at load
(task 3.17). Any other line with no executable statement, a blank or a comment, is still accepted.

---

## `add_file` created a document under a name VB6 does not accept, and could write outside the project — **CLOSED** (#596, 2026-09-22)

> **Fixed.** `add_file` now applies `ProjectNaming`, the rule every other way of naming a document uses:
> `add_file {"name":"My Module","type":"module"}` answers `'My Module' is not a valid name. A name starts
> with a letter and continues with letters, digits and underscores. Nothing was added.`, and a name already
> used by any form, module or class, in any case, is refused the same way. A successful reply now carries a
> `note` saying that the project file does not list the new file until the project is saved, and when the
> file went to the project's temporary folder.

**Symptom.** `add_file {"name":"My Module","type":"module"}` on a saved project answered
`{"success":true,"path":"…\\My Module.bas"}` and wrote `Attribute VB_Name = "My Module"`. It was the one route
to such a document: Project → Add names new documents with `NextFreeName`, and adopting a file refuses an
invalid name. The tool also kept its own copy of the collision check.

**Security, and the larger half.** The name also reached a file path unchecked: `AddNew*` builds
`Path.Join(dir, name + ".bas")`, so `add_file {"type":"Module","name":"..\\..\\somewhere\\X"}` wrote a
VB6 source file outside the project folder, anywhere the user can write, and `set_file_content` could then
overwrite it. The extension is fixed (`.bas`, `.cls`, `.frm`, `.ctl`, `.pag`), and the server is DEBUG-only and
loopback-only, but it has no authentication (#352). It failed open, with success and the escaped path in
the reply. `IsValidName` admits no separator, dot or colon, which closes it: `..\x`, `../x` and `C:x` are
each refused, and nothing is written. The reviewer found this, not the audit.

**Why it mattered.** Nothing refused the name until something downstream tried to use it as an identifier.
The reply said "saves it to disk" while the `.vbp` did not list the file, so a caller checking the project
file concluded the add had failed.

---

## A run that failed to start was reported as started, and left the debugger saying `Running` — **CLOSED** (#590, 2026-09-22)

> **Fixed.** When the startup form cannot be built, the start now tears down what it had claimed (the
> controller is stopped and no project is left running) and reports the failure through the existing
> runtime-error path: the person gets the runtime-error dialog and `get_last_runtime_error` returns the
> text. `run_project`, `run_to_cursor`, `step_into`, `step_over` and `step_out` answer the failure instead
> of success: `run_project {}` on a form whose button has `Width = -300` answers `Form 'Form1' could not
> be loaded, so the project did not start. -20 is not a valid value for 'Width'. Nothing is running. …`,
> and `get_debug_state` then reads `{"running":false,"state":"Stopped"}`. `get_debug_state` also reports
> `Stopped` on a freshly launched IDE, where it used to report the controller's pre-session `Running`.
> Whether a negative size should be accepted in the first place is #589, still open.

**Symptom.** With the startup form holding a control whose `Width` is -20 (the entry above):

```
run_project {}          → {"success":true}
get_debug_state {}      → {"running":false,"state":"Running"}
get_last_runtime_error  → {"raised":false,"sequence":0}
```

The form never appears. The `state` half is not only this failure's doing: a freshly launched IDE that has
run nothing answers `{"running":false,"state":"Running"}` too, and only reads `Stopped` after a run has
been stopped. The only record is an `ArgumentException` from `VBLoader.PlaceComponentTree`
in the IDE log. `run_to_cursor` takes the same path. A person pressing F5 sees nothing either.

**Workaround.** After a start, confirm with `get_debug_state` that `running` is true, and read the IDE log
(`%LOCALAPPDATA%/HexIDE/logs/ide/`) when it is not.

**Suggested fix.** Route a failed form load through the existing runtime-error path, so the person gets the
runtime-error dialog and `get_last_runtime_error` reports it, and reset the controller to `Stopped`:
[#590](https://github.com/hexide-io/HexIDE/issues/590). Any exception while the startup form loads
takes this path, not only #589's.

---

## A tool that threw answered only "An error occurred invoking" — **CLOSED** (#603, 2026-09-22)

> **Fixed.** A call-tool filter (`ToolFailures`, registered in `IdeServer`) catches any exception a tool did
> not catch itself and answers with an MCP error result naming the tool, the exception type and its message.
> It says this is a defect to report, not a refusal of the arguments, and points to the IDE log, where the
> stack is written under `MCP tool <name> failed`. It names the log folder as each platform does. The user's
> profile directory is replaced with `%USERPROFILE%` (or `~`) in the reply, in its long and 8.3 short forms,
> because the reply asks to be pasted into an issue; the log keeps the message as thrown. `McpException`, and
> a cancellation the caller asked for, are left to the SDK. A tool's own timeout surfacing as a cancellation
> is reported like any other throw. An `AggregateException` holding one exception reports that one. It is a
> backstop: each throw it reveals is still a defect in its tool, and entries recording one stay open until
> that throw is fixed.

**Symptom.** `set_window_state {"state":"Normal","width":-50}` (x, y, height left at null) on `main` at
3eb6396 answered `An error occurred invoking 'set_window_state'.` and nothing else. The same line was
recorded separately for `take_snapshot` with a menu open and for `set_breakpoints` against a stale schema.
Each time the cause had to be dug out of the log or the source.

**After.** The same call answers `'set_window_state' failed with an unhandled System.ArgumentException: -50
is not a valid value for 'Width'. This is a defect in the tool, not a refusal of your arguments: please
report it. The stack trace is in the IDE log (%LOCALAPPDATA%\HexIDE\logs\ide), under 'MCP tool
set_window_state failed'.` The log holds `[ERR] MCP tool set_window_state failed with an unhandled
ArgumentException` and the stack. That throw itself is #602.

With the IDE's `TMP` and `TEMP` pointed at a folder under the profile, and a directory standing where
`export_lsp_conversation` writes its `.jsonl`, `export_lsp_conversation {}` answers `… System.UnauthorizedAccessException:
Access to the path '%USERPROFILE%\AppData\Local\Temp\…\hexide_lsp_conversation.jsonl' is denied. …`. That
held for both the short and the long spelling of the profile, and the log line for each carried the real path.

**Exposure.** An exception message can carry local paths. The server already returns absolute paths and
whole documents to any local caller, and it is DEBUG-only and loopback-only with `Host` and `Origin`
checked. The stack trace is kept out of the reply. Authentication stays #352.

---

## Four tools returned an exception message with the profile path unredacted — **CLOSED** (#606, 2026-09-22)

> **Fixed.** `set_file_content`, `add_file`, `set_control_property` and `add_control` catch their own write
> failures and put the exception's message in their reply. Those messages now go through
> `ToolFailures.WithoutProfile`, the same redaction #603's backstop applies. That redaction now also covers the
> forward-slash spelling of the profile, as a `file:///<drive>:/Users/…` URI carries it, in both the long and the 8.3
> short form. It is for the report, not a security control; the server's exposure is #352.

**Measured.** With the target file read-only, each of the three writing tools tried answered `Access to the
path is denied.` with no path in it, because the writer's own message does not name one here. So the redaction
is defensive at these four sites. It was exercised end to end on the backstop in #605, where an
`UnauthorizedAccessException` did carry the path.

---

## `set_window_state` threw on a negative size, ignored `x` or `y` alone, and could put the IDE off every screen — **CLOSED** (#602, 2026-09-22)

> **Fixed.** Everything is checked before anything changes, and a refusal says "Nothing was changed.":
> - a non-positive `width` or `height` is refused;
> - `x`, `y`, `width` or `height` with a state other than Normal is refused rather than ignored;
> - a position that would leave the window intersecting no screen's working area is refused, and the reply
>   names the screens.
>
> `x` and `y` now apply independently. A left-out coordinate keeps the window's current value, read after
> restoring from Maximized, so it is the restored position, not the maximised frame's -11. The reply carries
> the resulting `window` (state, position, size) as `get_window_state` reports it, read after layout, so a new
> size is not reported as the old one on synchronous backends. This layout-only wait did not cover X11
> window-manager round trips (#946).

**Symptom.** On a window at (190,190) 945×447:
- `set_window_state {"state":"Normal","width":-50,"x":-99999}` answered only `An error occurred invoking 'set_window_state'.`
- `{"state":"Normal","x":300}` answered success and left the window where it was.
- `{"state":"Normal","x":-99999,"y":-99999}` answered success and moved the IDE to (-32768,-32768), where nobody could see or reach it.

**Found while fixing it.** Applying the geometry before `WindowState = Normal` lost it whenever the window was
maximised: the restore put the window back at its restore bounds. Reading `ClientSize` straight after setting
`Width` gave the size before the call.

---

## `press_key` named the control that received the key only by its class — **CLOSED** (#611, 2026-09-22)

> **Fixed.** When the input goes somewhere other than the control addressed, `press_key` and `type_text`
> now give the receiver's path as `dump_visual_tree` prints it, walked down from the addressed control with
> the same segment choice, so the path can be fed straight back as a target. A TextArea that the control
> view folds into its editor is given as its editor's path, which resolves back to the same TextArea. Live, on
> a `--newproject` IDE with Form1's code window open:
> `press_key {"target":"Window","key":"End"}` → `pressed End on TextArea, at Window/Custom[MainView]/…/None[TextEditor]/Pane[PART_ScrollViewer]/None`.
> Pressing End again at that path answered `pressed End`, with no redirect.

**Symptom.** `press_key {"target":"Window","key":"Return"}` answered `pressed Return on TextArea`. The IDE holds
a TextArea for the Immediate window and one per open code editor, so the class said which kind of control got
the key and not which one.

---

## `hover` quoted a declared tip from an unrelated descendant, and accepted a negative dwell — **CLOSED** (#610, 2026-09-22)

> **Fixed.** The declared tip now comes from the control under the hovered point, or its nearest ancestor
> that declares one, found by bounds so a disabled button still counts. `dwellMs` outside 0 to 10000 is
> refused. When the pointer goes to an editor under the target, the reply gives that editor's path. Live,
> on a `--newproject` IDE:
> `hover {"target":"Window","dwellMs":-5}` → `dwellMs must be between 0 and 10000; it was -5`.
> `hover {"target":"Window","dwellMs":0}` → `hovered (0, 8.5) on TextEditor[Editor], at Window/…/Pane[Immediate]/Custom; no tip opened within 0ms`, with no declared tip.
> `hover` on `…/None[StandardToolbar]` → `declared tip: End`, the disabled button at its centre. At (5, 10) → `declared tip: Add Project`.

**Symptom.** `hover {"target":"Window","dwellMs":-5}` (x, y, window left at null) answered
`hovered (0, 25.6) on TextEditor[Editor]; no tip opened within -5ms (…); declared tip: Add Project`. The pointer
went to a code editor. The declared tip was the Standard toolbar's, found by a search of the target's descendants.
The negative dwell had been clamped to 0 but was reported as given.

---

## `set_control_property` could not set `Name` on anything — **CLOSED** (#494, 2026-09-22)

> **Fixed.** A property is matched by the name the Properties window shows, without regard to case or to the
> parentheses of `(Name)`, so `Name` reaches it. A rename passes the Properties window's rules on every route,
> now kept in one place (`ComponentNaming`): not empty and unique on the form, and for the form itself a valid
> VB6 name no other document of the project has. With the designer open its handler applies them and the
> tool turns the `DataValidationException` into a refusal; with it closed the tool asks them first. Live, on a
> `--newproject` form with `Command0` and `cmdOk`:
> `set_control_property {"formName":"Form1","controlName":"Command0","property":"name","value":"cmdOk"}` →
> `'Command0' was not renamed: Name must be unique in form`, with the designer open and again with it closed;
> `Form1` → `1st` refused as not a VB6 name; `Form1` → `frmMain` renamed the document.
>
> **Still open, and filed separately:** `set_value` on the Properties window row's `Edit` reports success and
> does not commit: [#625](https://github.com/hexide-io/HexIDE/issues/625), with its own entry in
> `docs/mcp-server-gaps.md`.


**Symptom.** `set_control_property(formName: "Form1", controlName: "Command1", property: "Name", value:
"Command0")` answers `Property 'Name' not found on VB.CommandButton`. The same call against the form's own
root answers `Property 'Name' not found on VB.Form`. `Name` is the first row of the Properties window and
the one property every VB6 developer sets on every control they draw.

**Measured, not inferred** (2026-09-20), while checking whether a newly added validation refusal could
escape this tool's narrow catch (`HexIdeTools.cs` catches only `FormatException` and `OverflowException`
inside its dispatcher lambda). It cannot, because the property is refused before anything is set — so the
escape is unreachable through this tool, and the guard rail nobody can reach is worth recording as such.

**Consequence.** Renaming a control or a document is not automatable through the property tool at all. The
working route is the Properties window itself: `interact` with `set_property` on the `(Name)` row's
`PropertyViewModel.Value`, which is the reflection fallback rather than a provider action, and which does
commit through the same validation the user gets. `set_value` on that row's `Edit` writes the text and does
**not** commit — the binding updates on focus loss and Enter does not stand in for it — so a caller who uses
the obvious verb sees success and no rename.

---

## `set_value` on a Properties window row reported success and did not commit — **CLOSED** (#625, 2026-09-22)

> **Fixed, for a person as well.** The row's text box commits on focus loss, and nothing handled Enter, so
> Enter did nothing for a person either. Now:
> - `PropertyBox` commits on Enter.
> - `set_value` on a bound text box commits as Enter would. The `interact` tool then reads the box back
>   after the dispatcher has run the source's reaction, so a refused value is reported as refused.
> - The Properties window posts its revert of a refused value, which was being dropped because it was raised
>   inside the binding's write. So the row no longer keeps showing a value the control does not have.
>
> Live, on a `--newproject` form:
> - `set_value` `Hello 625` on the Caption row → `…, and committed it`, and `get_form_controls` shows the new
>   caption.
> - `type_text` then `press_key` Enter on the same row → committed.
> - Height `tall` → `…, but after committing it shows '300': the change was refused or rewritten…`, with the
>   Invalid property value box open.
> - `(Name)` `1st` → `…shows 'Form1'…`.


**Symptom.** `interact` with `set_value` on a Properties row's `Edit` (e.g.
`…/Pane[Properties]/Custom/Tab[TabProperties]/List[AlphabeticProperties]/Pane[PART_ScrollViewer]/ListItem[Caption]/Edit`,
`value` `Hello 494`) answers `set value of 'Edit' to 'Hello 494'`, and `get_form_controls` still reports the
old caption. `press_key` Enter on the same `Edit` does not commit it either. The row's binding commits on
focus loss, which neither causes.

**Workaround.** Use `set_control_property`, which sets the property itself. Or, in the Properties window,
use `interact` `set_property` with `Value=…` on the row, which is the reflection fallback.

**Suggested fix.** Push the text box's binding to its source after `set_value`, or say in the reply that
nothing has been committed: [#625](https://github.com/hexide-io/HexIDE/issues/625).

## Claude Code cut three tool descriptions short, losing `window` "ide" among other things — **CLOSED** (#638, 2026-09-22)

> **Fixed.** Every description a caller reads, a tool's and each parameter's, is now at most 2048 characters,
> and `ToolDescriptionLengthTests` fails the build on one that is longer. Text about a single parameter moved
> into that parameter's own `[Description]`, which the SDK sends in the input schema.

**Symptom.** The `interact` schema Claude Code delivered to a session ended mid-word:
`…a TreeViewItem's own peer offers no prov… [truncated]`. The text before the marker is 2048 characters
exactly, counted from what was received. `list_lsp_messages` arrived cut the same way, at
`…Abandoned (the connection wen… [truncated]`.

**Measured lengths before the fix:** `list_lsp_messages` 3467, `interact` 3093, `dump_visual_tree` 2266, `hover`
2048. The first measurement missed `list_lsp_messages`: a quick script read only a `[Description]` directly after
`[McpServerTool]`, and that tool has three `[DescribesEnum]` attributes between them. The guard found it, since it
reads through the same parser as every other description check.

**What a caller lost.** From `interact` and `dump_visual_tree`, the only mention of passing `window` "ide" to reach
the IDE while a program runs or is paused, when the frontmost window is the program's form. From `interact` also
the dropdown advice and "use dump_visual_tree/inspect_element first". From `list_lsp_messages`, the end of the
`outcome` vocabulary, the sequence-gap explanation, the reply-on-the-request's-row rule, and the filters.

**Not verified in the session that fixed it.** A session caches tool schemas when it attaches, so the new text
could not be read back through the client that cut the old one. The text itself is guarded; that the client
delivers it whole needs a session attached after the change (`/mcp`, reconnect `hexide`).

**2048 is one client's limit, and the tightest known.** Other clients may cut elsewhere, which argues for keeping
a tool's description to what the tool does rather than for raising the limit.

## Every parameter of a capture tool was required, including the ones that mean "no filter" — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #393 (#369 phase 3), which gave every optional parameter a C# default.**

**Symptom.** The first call to `list_lsp_messages` had to pass five arguments to ask the simplest possible
question. `connectionId`, `method`, `failuresOnly`, `afterSequence` and `limit` were all in the schema's
`required` array, so "list everything" could not be expressed as an empty call.

**Cause, and it is a C# detail with a schema consequence.** A nullable parameter with no default value is
still a *required* parameter to the MCP schema generator. `string? connectionId` is optional-looking in C#
and mandatory on the wire. `dump_visual_tree` got this right by accident of having defaults
(`string? root = null, int maxDepth = 20`), and the new tools did not.

**Workaround used.** Pass `null` explicitly for each. It works, and it is five arguments of noise on every
call, which is exactly the friction that makes an agent reach for a different tool.

**Fixed** by giving every optional parameter a C# default. Worth knowing for the next tool: check the
generated schema's `required` array, not the C# signature — they disagree, and only one of them is what an
agent sees.

## The capture state went blank after a clear, which is the one thing it existed to prevent — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #393 (#369 phase 3).**

**Symptom.** `clear_lsp_capture` replied:

```
{"envelopesDiscarded":13,"state":{"armsEveryConnection":true,"connections":[]}}
```

The connection was alive and armed. `arm_lsp_capture` a moment earlier had listed it correctly.

**Cause.** Both mutating tools returned a state whose connection list was derived from the envelopes
present in the record — the same answer as the real list right up until somebody empties the record.

**Consequence, and why it is worse than a cosmetic wrong field.** The state is returned by those two tools
specifically so that arming is not invisible: a tool answering only "done" would leave an agent unable to
tell an armed connection from one whose id it had misspelled. After a clear it answered exactly that.
An agent clearing `hexide.vb6` and one clearing `hexide.vb` got identical replies.

**Fixed** by having the log name its own connections (`ConversationLog.ConnectionIds`) rather than
inferring them from traffic, which also makes a connection armed before it has started visible — the case
the launch flag depends on. Two tests pin it.

**Found on the first real use of the tools**, by driving them rather than by reading them. Both defects had
passing unit tests around them; neither could have been caught by one, because both are about what the
*schema* and the *reply* look like to a caller.

## There is no way to ask what is being recorded without changing it — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #393 (#369 phase 3), which added `get_lsp_capture_state`.**

**Symptom.** To find out which connections existed and which were armed, the only tools were
`arm_lsp_capture` and `clear_lsp_capture` — both of which mutate. Reading the state meant arming something
first.

**Workaround used.** Call `arm_lsp_capture` with the state it already had, and read the reply.

**Fixed** by adding `get_lsp_capture_state`, which is the same reply with nothing changed.

## An export cannot be reached from automation — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #398 (#395), which added `export_lsp_conversation` over `ConversationExporter`, always pseudonymised.**

**Symptom.** `get_lsp_message` returns a body raw, deliberately — it is the developer's own machine and
the live view is not redacted either. But there is no tool that produces the redacted, shareable form, so
an agent asked to attach a conversation to an issue has no safe path: it can read bodies it must not paste,
and cannot produce the form it should paste instead.

**Workaround used.** None needed yet; noted before it is.

**Suggested fix.** A tool over `ConversationExporter`, which already produces the JSON-lines form plus a
manifest and takes a redactor. It is listed as phase-four work in #369 (task 4.5, export and copy), so this
is a note that the automation half of it matters as much as the button — an agent is the likeliest thing to
be asked for an export, and it is currently the only consumer that cannot make one.

**Filed as [#395](https://github.com/hexide-io/HexIDE/issues/395) and closed by
`export_lsp_conversation`.** An entry here is a note to self; the surface ships either way, so an
ergonomics defect gets an issue exactly as a human-facing one does.

**The tool always pseudonymises, and the opt-out is deliberately NOT a parameter on it.** The design
records that a non-pseudonymising mode should exist and that its surface is an open question needing a
prominent warning wherever it lands. A boolean here would have settled that question quietly, in the one
place with nowhere to put a warning. The raw form stays reachable through `get_lsp_message`, so nothing is
inaccessible — only unshareable, which is the distinction the redaction boundary is made of.

Its first real run found a leak no test had: a workspace folder's `name` survives while its `uri` is
redacted, because body redaction is textual by design and a `"name"` beside a URI cannot be recognised
that way — [#397](https://github.com/hexide-io/HexIDE/issues/397).

## get_document_tabs reported three fewer tabs than the user could see — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #402 (#369 phases 4 and 5).**

**Symptom.** A newly built Protocol Inspector tab was visibly open in the tab strip, and:

```
get_document_tabs      -> only the form designer
activate_document_tab  -> "No document tab with title 'Protocol Inspector'"
```

The Object Browser and the language-server connection list were missing too. Three real tabs, in the same
strip, invisible to automation.

**Cause.** Both tools read `IDocumentDockService.OpenDocuments`, which is typed
`IReadOnlyList<BaseEditorWindowViewModel>` and tracks only the editors the service was asked to open. The
Object Browser, the connection list and the inspector are documents the shell adds straight to the dock, so
they were never in that list. Nothing was wrong with the tools' logic; they were answering a narrower
question than the one asked, and the difference was invisible from outside.

**How it bit.** It blocked verification of the very feature being built. Worse, the failure was
*affirmative*: not "I cannot see that kind of tab" but "no document tab with title X", which reads as the
tab not existing. I had to take a screenshot to establish that the thing I had just built was on screen.

**Fixed.** `IDocumentDockService` gained `AllTabs`, `ActiveTab`, `TryActivateAny` and `TryCloseAny`, reading
the dock's own `VisibleDockables`. The three tools now answer about the strip the user sees, `type` gained
a third value `tool` for documents that are not editors, and the description enumerates all three.

**And the error now names what IS open.** A bare "no tab called X" cannot be told from a typo, and cost a
second call to find out. The tabs were already in hand:

```
No document tab with title 'Protocl Inspector'. Open tabs: Object Browser,
Language & Debug Servers, Project1 - Form1 (Form), Protocol Inspector.
```

## A DataGrid row could be read but not selected, which makes a master-detail window undrivable — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #402 (#369 phases 4 and 5).**

**Symptom.** With the protocol inspector's grid on screen and its rows enumerated by
`dump_visual_tree`:

```
interact(".../DataGrid/DataItem[#2]", "select")
-> {"success": false, "mechanism": "peer", "error": "element does not support 'select'"}
```

`inspect_element` on the same row reported `"providers": []` and `"selectionItems": []`.

**Cause.** A `DataGridRow`'s automation peer exposes no `ISelectionItemProvider`, and `DoSelect` refused
when there was no provider. There was no second route either: the reflection actions set a view-model
property by name and coerce the value from a string, and the property that holds a selection is a row
object no string can name. So the grid was fully readable and completely inert.

**How it bit.** Selecting a row is not a detail of this window, it is the window: click a row, read the
body that crossed the wire. Every master-detail surface in the IDE has the same shape, so the gap was one
control wide and the whole pattern deep. It surfaced while verifying the detail pane, which could not be
verified at all until it was fixed.

**Fixed.** `UiAutomationDriver.DoSelect` falls back to selecting through the grid that owns the row —
found by walking up the visual tree, because `DataGridRow.OwningGrid` is internal — and reads
`SelectedItem` back rather than assuming the grid accepted it. `DescribeProviders` now advertises
`selectionItem` on a row, so the verb is discoverable instead of being a thing a caller has to try.
Covered headlessly in `UiAutomationDriverTests`.

## A native file dialog cannot be driven, so every flow that ends in one needed a person — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #402 (#369 phases 4 and 5), which added `answer_next_file_dialog`.**

**Symptom.** The protocol inspector's new *Export conversation…* button opens a save picker.
`dump_visual_tree` sees nothing of it — a native Win32 dialog is not in Avalonia's control tree at all —
and while a modal one is up the server does not answer. So the button could be found, enabled and invoked,
and what happened next could not be observed or completed.

**How far it reaches.** Not one button. Save As, Open Project, Add File, Make EXE, Make Project Group,
every export: each of them ends in `IStorageProvider`, and each has been verified up to the dialog and by
hand after it. This was already noted in passing inside a *closed* entry about carried files, which is
where a general gap goes to be forgotten.

**Fixed, and deliberately not by faking the dialog.** `answer_next_file_dialog(path?)` arms the answer the
picker would have returned; `clear_file_dialog_answers` discards what is armed. `WindowManager` consults
the armed answer before reaching for the storage provider, so everything below the picker — the writing,
the naming, the refusals — is the same code a real click reaches. Only the part a person performs is
skipped.

Three properties are load-bearing:

- **Single-shot.** A standing override would silently redirect the next unrelated save, and that damage
  shows up somewhere other than where it was caused.
- **Cancellation is expressible.** An empty path answers as cancelled, which is a distinct branch through
  most of these flows and the one least likely to have been exercised by hand.
- **DEBUG only.** The queue and both call sites compile out with the server, so a shipped build has no
  bypass rather than an unreachable one.

**It did NOT need a session restart, and that is worth recording because the expectation was wrong.** Two
brand-new tool schemas appeared to the already-attached client as soon as the IDE relaunched carrying them,
and were callable in the same session that added them. That matches the measured entry above about
mid-session relaunch rather than the standing advice, which is written for the case where the server was
not attached when the session began. Verified by using both tools to drive the export they were built for,
including the cancellation branch.

## `get_lsp_message` could not return a single response, and nothing said so — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #431 (#429).**

**Symptom.** Pointing HexIDE at a foreign server and trying to read what it advertised. `list_lsp_messages`
showed the `initialize` request answered in 41 ms; `get_lsp_message` on that sequence returned the
**request**. There was no sequence that returned the reply, no field naming one, and no explanation — the
tool had exactly one address per exchange and it was the half already known.

**How it bit.** The whole investigation was about an `InitializeResult`: which capabilities the server
declared, and therefore which later messages were legal. Getting it meant writing a client outside the IDE
and driving the server over its own transport by hand — reintroducing, inside the tool built to destroy
the ambiguity, the exact work that tool exists to remove. Two other things went with it: an unarmed
connection could say a request was answered and not how large the answer was, and
`export_lsp_conversation` — a format whose claim is that it replays into a client — contained not one
reply, so the requests in it would hang.

**Why it survived.** `ConversationLog.TryComplete` pairs a response with its request, stamps the outcome
and the latency onto that request's envelope, and returned. Its comment explains half the decision
correctly and stops one step short: *"The response is not itself an entry. It is the second half of one,
and a timeline that showed both would double every request."* True about the **entry**, and taken as
license to drop the **body**. Everything downstream then read as working: the timeline was right, the
latencies were right, and the one thing missing was missing uniformly, so it looked like a design rather
than a hole. A caller who has never seen the record cannot tell "responses are not kept" from "this
response was not kept" from "I have asked the wrong question", which is the three-way ambiguity this
surface exists to destroy.

**Fixed** (hexide-io/HexIDE#429). A response keeps the sequence it was already allocated — responses have
always consumed one, which is why a listing has always had gaps and why this tool's description has always
had to explain that those gaps are not dropped frames. The request's row now names it, so the apology
becomes an address: `answerSequence` when a body was kept, and `answerSizeBytes` always, since a size is
metadata and belongs to the tier that runs unarmed. Passing an answer sequence to `get_lsp_message` returns
`isAnswer`, `answerTo`, and the request's method, because a response carries none on the wire. Both tool
descriptions say all of this, including which state a row with an outcome and no `answerSequence` is in.

**The general lesson is the one this file keeps re-learning.** The author knew responses completed their
requests; a first-time caller sees a sequence number that answers with the wrong half. Judge the tool by
what a model that has never seen it would do on first contact — and a reply that is structurally
unreachable must at minimum say so, rather than returning something plausible and adjacent.

## A mark tool answered about the startup project, keyed on the caller's spelling, and said nothing back — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #495 (#273 phase 1).**

**Symptom.** `set_breakpoints("form1", [5])` on a project holding `Form1` replied `{"success":true}`. The
gutter showed nothing, the run broke nowhere, and `get_breakpoints("Form1")` answered with an empty array.
Two tools, two confident replies, no breakpoint. With a project group open, the four mark tools could not
reach the second project's documents at all — a name they did not recognise was `No form or module named
'X' found`, whether or not the IDE had one open in front of the caller.

**How it bit.** `ResolveDocumentUri` matched a name case-insensitively against the **startup** project and
then built the store key by interpolating the **caller's** spelling into `vb6://form/{name}`
(hexide-io/HexIDE#467). Both mark stores were ordinal dictionaries, so `vb6://form/form1` was a second
entry beside `vb6://form/Form1` — one the gutter, the runner and the sidecar all read past. The reply then
echoed that key back in its `uri` field, which reads as confirmation rather than as the symptom it was.

**Why it survived.** Three separate things each looked correct. The match was case-insensitive, which is
what VB6 does. The key was minted the same way the editor mints it, which is what consistency looks like.
And the reply named what had been written, which is what a mutating tool should do. What nobody wrote down
is that the two had to be the *same* string, and nothing in the surface could show they were not: a caller
who has never seen the IDE cannot tell "set, and shown" from "set under a name nothing reads" when both
answer `success`.

**Fixed** (#273 phase 1). A name resolves to a *document* before anything is keyed, and the stores are
keyed by that document rather than by any spelling of its name. Every tool that names a document searches
**every loaded project**, takes an optional `project` to disambiguate, and refuses an ambiguous bare name
with the candidates listed rather than picking one. Replies carry `project` and `document` — the IDE's own
spelling, not the caller's — beside the wire `uri`, and a mutating reply now reports what the document
holds afterwards, including when that is nothing: `Form1 now has no breakpoints.` rather than a bare
success.

**Two descriptions were corrected rather than fixed.** `clear_all_breakpoints` said "Removes every
breakpoint in the project"; it removes every breakpoint in *every loaded project*, and has always done so.
Whether VB6 agrees is unmeasured and is filed as #492. And `set_bookmarks(name, [])` promised to clear a
document's bookmarks: it emptied the store and raised no change event, so the gutter kept its dots and the
sidecar was never rewritten — the cleared bookmarks came back on the next load. That one was a real defect
and is fixed with the rest.

**The lesson this adds.** A reply that echoes an argument back is not evidence the argument was understood.
Where a tool normalises what it was given — a name matched without regard to case is exactly that — the
reply must carry the **normalised** form, because the difference between the two is the whole of what the
caller cannot otherwise see.

**"Every tool" was not yet true, corrected 2026-10-13 (#273).** The fix above says every tool that names a
document searches every loaded project, and that replies carry `project` and `document`. Neither held
everywhere. `set_control_property`, `move_control` and `add_control` still resolved a form their own way (see
the #575 entry for what that did). Of the replies, only `get_bookmarks` and `get_breakpoints` carried the names.
Now every tool that takes a form, module or class by name resolves it through one rule, and its reply carries
`project` and `document` as the IDE spells them whenever the name found one: the content, designer and mark
tools, `run_to_cursor` and `set_next_statement`. So do `add_file`'s reply and each `get_diagnostics` item. On a
diagnostic they sit beside the server's `uri`, and are absent when that names nothing loaded or a file two
loaded projects share. `get_debug_state` and `get_call_stack` carry the running `project` beside their bare
module names. **Still not every reply:** a tool that reports or acts on a document without taking its name does
not name its project yet. That is the run-control replies' "Paused at" note, `get_locals`' and the watches'
`context`, the designer undo, redo and format tools, which act on the active designer, and the tab tools, which
take and report a tab's title. Filed as #886. `uri` is now explained where it is
returned (`get_bookmarks`, `get_breakpoints`, `get_diagnostics`): it is the name a language server knows the
document by, its `file:` URI, or `untitled:<project>/<name>.<ext>` before it has a file. Until now its only
explanation was a code comment, which no caller can read. The delta's scenario, a breakpoint set in `form1` of a
project holding `Form1`, is `BreakpointByNameTests.SettingABreakpointByNameInTheWrongCase`. It checks that the
code window's gutter draws the breakpoint, a run stops on it, and the sidecar records it under `Form1`. Each of
the three fails when the breakpoint is keyed under the caller's spelling instead.

## 2. `set_control_property` only handles string / number / bool — **CLOSED** (#641, 2026-09-22)

> **Fixed.** `set_control_property` reads its value through `PropertyText`, which defers to the Properties
> window's own parser and adds an enum by number, by name or as the dropdown shows it (`1 - Opaque`), with
> numbers read in the invariant culture. A refusal lists what the property takes. Verified live:
> `BackStyle` `"0 - Transparent"` and `BackColor` `"&H00C0FFC0&"` were written to the `.frm` as
> `BackStyle = 0 'Transparent` and `BackColor = &H00C0FFC0&`; `"7"` and `"green"` were refused naming the
> accepted spellings.

**Symptom.** Setting an **enum** property fails — `set_control_property(Label0, "BackStyle", "1")` →
*"Property 'BackStyle' has type 'BackStyles' which is not supported by set_control_property"*. **Colour**
(`VBColor`) properties (`BackColor`/`ForeColor`) are likewise unsettable.

**How it bit.** I couldn't make a label opaque, nor set a control's colour, via the designer tool — so the
Phase-2 colour verification had to be done by *running code* (`Me.BackColor = &HC0FFC0` in `Form_Load`) and
snapshotting the result, rather than a designer property set.

**Fix.** Accept enum values (by member name or ordinal) and `VBColor` values (a hex `OLE_COLOR` string like
`"&H00FF0000&"`, or an `R,G,B` triple). Better: route the incoming string through the **same property-editor
coercion the designer's property grid uses**, so every editable property type is settable through one path.

## take_snapshot renders DIPs while Win32 coordinates are physical pixels — **CLOSED** (#646, 2026-09-22)

> **Fixed.** `take_snapshot` now reports the image's `width` and `height` in pixels and its `scale`, the pixels
> per device-independent unit, and `inspect_element` says `boundingRect` is in the window's device-independent
> units from its top-left, which is the image's top-left. **One detail below was wrong when re-measured:** on a
> 150% display `boundingRect` was in DIPs, not in the image's pixel space. The Start button's `[270, 26, 22, 22]`
> is at `[405, 39, 33, 33]` in the 1500 × 900 snapshot, and a crop of that region is the button. The entry is kept
> as it was written; its Win32 half (`GetClientRect` in physical pixels) was not re-measured.

**Symptom.** Driving a synthetic mouse click from a `boundingRect` needs a scale conversion that nothing in
the tool output mentions. On the machine this was hit on, `GetClientRect` reported 987 × 560 physical pixels
while `take_snapshot` returned a 1481 × 840 image and `inspect_element` reported bounds in that same 1481-wide
space — a factor of 0.666. Clicking at the raw `boundingRect` coordinates lands roughly 50% off, far enough
to hit a different control and look like "the click did nothing".

**Consequence.** Any fallback that leaves the MCP surface for real input — the only route left when a
control has no usable provider — silently targets the wrong place, and the resulting no-op is easy to
misread as the feature being broken.

**Workaround.** Derive the factor before clicking: `GetClientRect` width ÷ snapshot image width, then
multiply the DIP coordinate by it and pass through `ClientToScreen`. Do not assume 1.0, and do not assume
the usual Windows 1.25/1.5 either — measure it.

**Suggested fix.** Report the scale explicitly. `take_snapshot` returning the render scale alongside the
path (and `inspect_element` naming the space its `boundingRect` is in) would remove the guesswork; the
values are already known to the server.

## 8. `type_text` bypasses a read-only editor, and reports `mechanism: "keyboard"` while doing it — **CLOSED** (#649, 2026-09-22)

> **Fixed.** `type_text` refuses a read-only or disabled surface, naming which, and reports its mechanism as
> `"document"`; `"keyboard"` is left to `press_key`. Verified live on a form HexIDE opens read-only (a
> `CommandButton` nested in a `ListBox`): `type_text` answered `TextEditor is read-only, so a person could not
> type there either; nothing was inserted`, and `get_file_content` showed the code unchanged. Worth knowing
> when re-testing: the code window is read-only for a form HexIDE cannot save faithfully, not while a program
> runs, so a running program is not the way to reach this.
>
> **Since #273 (the review of task 3.20, 5541621), a file held read-only as a whole is refused with its reason.**
> The code window now asks first whether its file is one HexIDE will not save, and `type_text` and `press_key`
> answer `… refused: this file will not be saved, because HexIDE cannot yet reproduce it faithfully (…). Its code
> is part of the file, so typing in its code window is refused anywhere, as the window's banner says. Nothing was
> written.` On a code window that reply now comes before the one quoted above (`TextEditor is read-only, …`), which a
> form held read-only as a whole no longer reaches (`UiAutomationDriver.RefusedInReadOnlyRegion`, asked first). That
> review found the window's own Enter writing a line break into such a file, past the section provider that refuses
> typing, because its handler writes the document itself; Enter now refuses, and `press_key`
> says why rather than reporting the key pressed. Seen running on 2026-10-13 (#273 task 3.22), on a form whose
> `.frx` was missing: `type_text` and `press_key` Enter each refused with that text.

**Symptom.** Verifying the read-only editing gate (#22) against the running IDE, `type_text` successfully
inserted `XXX_SHOULD_NOT_APPEAR` into a code editor whose `TextEditor.IsReadOnly` was bound true. The result
reported `"mechanism":"keyboard"`, which reads as "a real key event went in" — so the first conclusion was
that the gate was broken. It was not.

**Cause.** The tool's own description says it inserts "at the caret **via the control's own API**", which is
a document mutation, not input. `IsReadOnly` on AvaloniaEdit guards the *editing UI*, so a direct
`Document.Insert` legitimately sidesteps it. The `mechanism: "keyboard"` label is the misleading part.

**Workaround.** Do not use `type_text` to test whether input is blocked. `press_key` raises real
`KeyDown`/`KeyUp`, but note gap #9 below before trusting a negative result from it either. The reliable
check is behavioural at a level the user cares about — here, invoking Save and confirming the file on disk
is byte-identical afterwards.

**Suggested fix.** Report `mechanism: "api"` (or `"document"`) when inserting programmatically, and reserve
`"keyboard"` for genuine key events. Optionally have `type_text` refuse, or warn, when the target editor is
read-only — silently mutating a read-only document is a surprising default for an automation tool.

## `get_diagnostics` answered `[]` for clean code, for no server running and for an analysis not yet back — **CLOSED** (#664, 2026-09-22)

> **Fixed.** The reply now carries `analysed`, the documents whose diagnostics are in, clean ones
> included. It also carries a `note` saying which case an empty or partial answer is: no language server
> started; a document opened or edited whose analysis has not come back; or every analysed document clean.
> An edit is tracked by the document's own session (`LspDocumentSession.AwaitingDiagnostics`), so a stale
> "clean" for text the caller has just changed is reported as pending, not as an answer. Found and fixed in
> the same pass, so this entry never sat in the live file.

**Symptom.** `set_file_content` then `get_diagnostics`, the obvious way to check an edit, answered
`{"diagnostics":[]}` for code with a syntax error in it. Measured with `get_diagnostics` on `--newproject`:
`[]` with no server started (`get_lsp_capture_state` → `connections: []`); `[]` after `set_file_content`,
because nothing was open and only open documents are analysed; `[]` for about 3 s after `open_file`, while
the server started (`initialize` alone took 1.7 s); and, found while checking the fix, `[]` for about a
second after an edit to an open document, when the diagnostics held were for the text before it.

**Also.** `DiagnosticsCache` keyed documents by raw URI string, where `AddinDiagnosticsService` had moved
to `LspDocumentUri.Comparer`; it now uses the comparer too.

## `open_file` does not give the editor keyboard focus, so a routed menu item refuses until something does — **CLOSED** (#678, 2026-09-22)

> **Fixed.** `open_file`, `view_designer` and `activate_document_tab` now put keyboard focus in the document
> they bring to the front (an editor's text area, or the designer's form surface), as opening it by hand does, and
> say so in their note. Verified live: `open_file {"name":"Form1"}` then `invoke_menu_item {"path":"Tools/Add Procedure"}`
> opened the dialog with no `press_key`, and focus followed `activate_document_tab` between the designer and the
> code window. The entry is otherwise as it was.

**Symptom.** Most built-in menu items are routed commands, which act on the control that has keyboard
focus. `open_file` brings a code window to the front but leaves focus where it was (measured: nowhere), so
`invoke_menu_item {"path":"Tools/Add Procedure"}` straight after `open_file {"name":"Form1"}` is refused.
After `press_key {"target":".../Custom[Root]/None[TextEditor]","key":"Right"}` the same call opens the Add
Procedure dialog. A person who opens a code window has focus in it, so the tool is behind the person here.

**Narrowed by #678's PR.** The refusal used to say only `canExecute returned false`. It now says the item is a
routed command, names what has focus, and says `press_key` on the editor is the way in. What remains is the
focus itself.

**Workaround.** `press_key` on the editor with a key that changes nothing (`Right`) before invoking an item
that belongs to a document.

**Suggested fix.** Have `open_file` and `activate_document_tab` give the opened document keyboard focus, as
opening it by hand does: [#678](https://github.com/hexide-io/HexIDE/issues/678).

## 5. A designer control is reported under its view-model's type name, not its own — **CLOSED** (#543 for #526, #663 for #661; moved from the live file 2026-09-22)

> **Closed.** What remained after the 2026-09-22 narrowing was the node name, and #543 fixed it for #526: a canvas
> control is now `ListItem[Command0]`, measured again the same day with `dump_visual_tree(root: ".../List[ControlsContainer]")`.
> The multi-selection addendum was closed by #663 (#661). The entry is otherwise as it was.

**Was:** *Can't select / delete / reorder a designer control via MCP.* Measured again for #362, and most of
it no longer holds. With default arguments, `dump_visual_tree(root: ".../Pane[#0]")` lists each canvas
control as a `ListItem` (`ControlItem`) with `selectionItem`. `interact select` on it selected it,
`press_key(key: "Delete")` on it deleted it (`get_form_controls` confirmed `Command0` gone; the Edit menu
then offered *Undo Delete: Command0*), and `invoke_designer_undo` / `invoke_designer_redo` both exist. The
`FormEditor.BringToFront` / `SendToBack` toolbar buttons are addressable by `automationId`; reordering was
not driven in this pass.

**What remains.** The node's name is `HexIDE.VisualDesigner.ComponentInstanceViewModel`, the view model's
`ToString()`, so the path is `ListItem[HexIDE.VisualDesigner.ComponentInstanceViewModel]` for every
control. With more than one control on a form, a caller cannot tell from the tree which node is
`Command1`; it has to fall back on position or index. Filed as #526. The rest of this entry is the original record.

**Multi-selection (#661, closed by the PR that adds this note).** `interact select` replaces a selection, so
a caller could hold one control, or all of them through `press_key(key: "A", modifiers: "Ctrl")`, and nothing
in between. `interact add_to_selection` / `remove_from_selection` now reach a chosen group, and each canvas
control reports the `multiSelectItem` token that promises them. Measured with three buttons:
`select` Command0, then `add_to_selection` Command2 → `added 'Command2' to the selection; 2 items are selected
now: Command0, Command2`; `invoke_format_command(command: "AlignLefts")` then moved Command2 to Command0's
Left (120) and left Command1 at 600.


**Symptom.** A control placed with `add_control` is created and auto-selected, but there is no way to (a) select a
*different, existing* control, (b) delete a control, or (c) exercise undo/redo of a designer edit through MCP. The
controls drawn on the designer canvas are **not individual nodes in `dump_visual_tree`** (the canvas paints them; the
tree shows only the Properties-pane `ObjectSelector` combo and dock chrome), so `interact`/`press_key` have no path
to target a specific control, and there is no `select_control` / `delete_control` tool.

**How it bit (bug-hunt batch-2 designer fixes).** Verifying the four `FormEditViewModel` fixes — duplicate-name
avoidance (needs *delete then re-add*), multi-select **Delete** (needs a multi-selection), and undo **z-order**
restore (needs cut + undo) — was not drivable. Only the no-collision naming path was confirmed live (`add_control`
twice → `Command0`, `Command1`). The rest were verified by build + code review against the already-proven
`CutSelectedControls` path and the standard ascending-index restore invariant.

**Root cause.** The designer canvas is a custom-drawn surface; component VMs aren't surfaced as automation nodes, and
the designer's selection/delete/undo aren't exposed as `ICommand`s reachable via `interact invoke_command`.

**Fix consideration.** Small, high-leverage additions: `select_control(formName, controlName)` (drive the designer's
`SelectedComponent`/`SetSelectedComponents`), `delete_selected_controls`, and `designer_redo` to pair with the
existing `invoke_designer_undo` — plus surfacing each canvas control as a `dump_visual_tree` node with its name, so
`interact` can click/rubber-band it. That would make the whole designer edit loop (add → select → move → delete →
undo/redo) MCP-verifiable.

## A save the code page or the serializer refuses held the tool's reply on a dialog — **CLOSED** (#751, by #729, 2026-10-12)

> **Fixed.** `set_file_content`, `set_control_property`, `add_control` and `add_file` now save with
> `interactive: false`. No dialog is shown. A save the code page refuses comes back in the reply, naming
> the document, the code page and the first character it cannot hold, and the change stays in the IDE. A
> save HexIDE refuses because it cannot reproduce the file comes back as the existing "cannot reproduce
> this file faithfully" reply, and is no longer reported again by the next save.
> `AutomationSaveRefusalTests` fails the build if a tool calls a save without `interactive: false`.
>
> Verified live on 2026-10-12, in a Debug build of #729 with `--ansi-code-page 1252`, on a project of
> ASCII files. Replies are shown decoded; the server escapes `'`, `+` and non-ASCII characters in its JSON.
>
> ```
> set_file_content {"name":"Module1","content":"Public Sub One()\n    Debug.Print \"Go → there\"\nEnd Sub\n"}
> → {"success":false,"error":"The new code is in the IDE but was not written: Module1 is kept in code page 1252,
>    which cannot hold '→' (U+2192). Nothing was written. HexIDE keeps a file in the code page it was read or
>    created in rather than convert it, so use characters that code page holds."}
> set_control_property {"formName":"Form1","controlName":"Command1","property":"Caption","value":"Next →"}
> → {"success":false,"error":"'Caption' is set in the IDE but was not written: Form1 is kept in code page 1252,
>    which cannot hold '→' (U+2192). …"}
> add_control {"formName":"Form1","type":"Label","x":20,"y":80,"width":80,"height":20}   (caption still unsaved)
> → {"success":false,"controlName":"Label0","error":"'Label0' was added to the designer but NOT written: Form1 is
>    kept in code page 1252, which cannot hold '→' (U+2192). …"}
> add_file {"name":"Ж1","type":"Module"}
> → {"success":false,"error":"'Ж1' was not added: Ж1 is kept in code page 1252, which cannot hold 'Ж' (U+0416). …"}
> ```
>
> After each call, `dump_visual_tree(maxDepth: 1, interactiveOnly: false)` reported the main window, so no
> dialog was open. The SHA-256 of every file in the project was unchanged, and `add_file` created no file.
> `get_file_content` showed the refused code still in the IDE, with `hasUnsavedChanges: true`.
>
> **The wording above was revised after review, in the same PR, and measured again live the same day.** Each
> reply now quotes the line of the file that holds the character, because the character can be in a property
> no read tool shows. The prefixes no longer name the new input as the cause, since the save writes the whole
> file. `add_control`'s reply ends by naming the call that writes the form once that text is changed. The
> replies are built from `SourceEncodingRefusal.Message`, which `Vb6TextFileTests` pins.
>
> Measured in a Debug build of #729 with main merged (`e999170`), without `--ansi-code-page`, the host's being
> 1252, on a project of ASCII files. Replies decoded as before:
>
> ```
> set_file_content {"name":"Module1","content":"Sub Main()\n    MsgBox \"Привет\"\nEnd Sub\n"}
> → {"success":false,"error":"The new code is in the IDE, but the file was not written. Module1 is kept in code
>    page 1252, which cannot hold 'П' (U+041F), found in the line `MsgBox "Привет"`. Nothing was written.
>    HexIDE keeps a file in the code page it was read or created in rather than convert it, so use characters
>    that code page holds, and where that line names a file or folder, a name it can hold."}
> set_control_property {"formName":"Form1","controlName":"Form1","property":"Caption","value":"Привет"}
> → {"success":false,"error":"'Caption' is set in the IDE, but the file was not written. Form1 is kept in code
>    page 1252, which cannot hold 'П' (U+041F), found in the line `Caption         =   "Привет"`. …"}
> add_control {"formName":"Form1","type":"CommandButton","x":20,"y":20,"width":81,"height":33}   (caption still unsaved)
> → {"success":false,"controlName":"Command0","error":"'Command0' was added to the designer, but the file was
>    NOT written. Form1 is kept in code page 1252, … found in the line `Caption         =   "Привет"`. …
>    Once that text is changed, set_control_property or set_file_content writes the file, 'Command0' included."}
> add_file {"name":"Ж1","type":"Module"}
> → {"success":false,"error":"'Ж1' was not added: Ж1 is kept in code page 1252, which cannot hold 'Ж' (U+0416),
>    found in the line `Attribute VB_Name = "Ж1"`. …"}
> ```
>
> `add_control`'s last sentence holds. `set_control_property` then set the caption back to `Hello` and answered
> success, and `Form1.frm` on disk held `Caption = "Hello"` and `Command0`. `add_file` created no file, and
> `dump_visual_tree(maxDepth: 1, interactiveOnly: false)` after it reported the main window.
>
> The warning a person sees was measured in the same run. With `Module1`'s refused code still in the IDE,
> `invoke_menu_item {"path":"File/Save Project1"}` opened one message box, read with
> `dump_visual_tree(interactiveOnly: false)` and checked in a `take_snapshot`, nothing clipped:
>
> ```
> Nothing was written. Module1 is kept in code page 1252, which cannot hold 'П' (U+041F). It is in this line:
>
> MsgBox "Привет"
>
> HexIDE keeps each file in the code page it was read or created in, rather than converting it. Change that
> text to characters the code page holds, or, where the line names a file or folder, choose a name it can hold.
> Then try again. Your changes are still open in the IDE.
> ```
>
> `Module1.bas` on disk was unchanged afterwards.

**Symptom.** Each of the three write tools saves through `ProjectService.SaveForm` or `SaveModule` on the UI
thread, and awaits the result. A refused save reported itself in a message box inside that save:

- `Str.Dialog.SourceEncoding.SaveFailed` when the code page could not hold a character;
- `Str.Dialog.UnfaithfulSave.Body.One` when HexIDE could not reproduce the file.

Nothing answers a message box for an automation caller (`ScriptedFileDialogs` covers the two file pickers
only), so the request stayed open until a person closed the dialog. When the reply did come, the encoding case
read `The new code is in the IDE but was not written: Source encoding cannot represent the edit.` It named no
document, code page or character.

`add_file` had the encoding case in another form. A name the code page cannot hold reached the encoder
unguarded. The reply was .NET's own message, naming the character's escape and an index into the file, and
by then the project's scratch folder had already been created.

**Not the same as #681.** That one is about tools acting behind a dialog that is already open. Here the tool
itself opened the dialog, and then waited on it.

**Still not covered.** A save started by `invoke_menu_item`, `press_key` or `interact` is a UI command. Its
reply says `Invoked` before the save finishes, and a refusal is shown to a person as a dialog, as it is for
a click.

## add_control calls an armed cancel a failed save, though its description offers the cancel — **CLOSED** (#784 for #755, 2026-10-12)

> **Fixed by #784, verified live on main a816e0d.** On a never-saved Standard EXE from File > New Project,
> with `answer_next_file_dialog {}` armed before each call, `add_control {"formName":"Form1","type":"CommandButton","x":20,"y":20,"width":81,"height":33}`,
> `set_control_property {"formName":"Form1","controlName":"Command0","property":"Caption","value":"x"}` and
> `set_file_content {"name":"Form1","content":"..."}` each answered `success:true`, with a note saying the form
> still has no file and how to write it. `get_form_controls` then listed one control. The cancel is recognised
> from the dialog's state (`ScriptedFileDialogs.WatchForCancel`), not from the exception, which an encoding
> refusal shares (#751). `ScriptedFileDialogsTests` covers that decision.
>
> **Checking it needs a form with no file, and Project > Add Form does not give one** on the project
> `--newproject` creates, because that project already lives in a temp folder. There the save needs no
> picker, the armed cancel is left pending, and the reply does not say so (#810, still open).

**Symptom.** On a new Standard EXE project from File > New Project, so `Form1` has no file:

```
view_designer {"name":"Form1"}
answer_next_file_dialog {}
add_control {"formName":"Form1","type":"CommandButton","x":20,"y":20,"width":81,"height":33}
→ {"success":false,"controlName":"Command0","error":"'Command0' was added to the designer but the save failed: The operation was canceled."}
get_form_controls {"formName":"Form1"}
→ lists Command0 at 20,20, 81 × 33
```

`add_control`'s description says to "arm it with no path to keep the form without a file".
`ScriptedFileDialogs.PickerRefusal`, the refusal `add_control`, `set_file_content` and
`set_control_property` all give for a pathless document, gives the same advice. The call did exactly
that: the control is placed and the form still has no file. The reply says `success:false` and "the
save failed", so a caller cannot tell whether to retry, and a retry adds `Command1` beside `Command0`.

`set_file_content` in the same state answered:

```
{"success":false,"error":"The new code is in the IDE but was not written: The operation was canceled."}
```

That wording is better, because it says where the edit is. It is still `success:false` for the outcome
the caller asked for.

**Which contract is intended.** The description's. The description, `PickerRefusal`, the comment over
`add_control`'s refusal and `WouldShowPicker`'s remarks all present an armed cancel as the way to keep a
document without a file, and the spec's *Driving a cancellation* scenario has the flow proceed as though
the dialog was cancelled. The reply comes from the write tools' catch-all, which predates that advice and
reports any exception as a failure, including the bare `OperationCanceledException` the save throws when
its picker returns no path. `set_control_property` has the same catch, read in code and not measured.

**Workaround.** After arming a cancel for a pathless document, read `success:false` ending "The operation
was canceled." as the requested outcome: the edit is in the IDE and the document still has no file.
Confirm with `get_form_controls` or `get_file_content`, and do not retry.

**Suggested fix.** In those three tools, answer `success:true` with a note giving the state, for
example that the control is in the designer and the form still has no file because the armed answer
cancelled the picker. Decide that it is the cancel before the save, from the document being pathless and
the next armed answer being a cancel. Do not decide it from the exception: #729 adds an encoding refusal
that is also an `OperationCanceledException`, and that one must stay a failure.
[#755](https://github.com/hexide-io/HexIDE/issues/755).

## take_snapshot writes every capture to the same file — **CLOSED** (#784 for #756, 2026-10-12)

> **Fixed by #784, verified live on main a816e0d.** Two `take_snapshot {}` calls, with the Immediate window
> shown between them, returned `hexide_snapshot_20261012-142646-333_1.png` and
> `hexide_snapshot_20261012-142648-443_2.png`. Both files remained, with different sizes and hashes, and the
> second showed the change. The naming is tested in `SnapshotCapturePathTests`. `export_lsp_conversation`'s
> fixed name was not part of the fix.

**Symptom.** With a program running:

```
take_snapshot {}                → "path": "<user temp>\\hexide_snapshot.png"   (the running program's form)
take_snapshot {"window":"ide"}  → "path": "<user temp>\\hexide_snapshot.png"   (the IDE)
```

The file name is fixed in `TakeSnapshotAsync`, so the second capture replaced the first. A caller taking
a before/after pair, or the form and then the IDE, loses the first image, and neither the description nor
the reply says the next call reuses the path. `export_lsp_conversation` writes a fixed name too, read in
code and not measured.

**Workaround.** Read or copy each snapshot away before the next `take_snapshot` call.

**Suggested fix.** Write each capture to its own file, named from a timestamp and a per-process counter,
and say in the description that earlier captures are left in place.
[#756](https://github.com/hexide-io/HexIDE/issues/756).

## A mid-session relaunch DOES pick up new tools, if the server was attached when the session started — **CLOSED** (superseded by #763, 2026-10-12)

> **Corrected 2026-10-12 (#763): the mechanism was not the attachment, and it no longer applies.**
>
> - **How new tools arrived.** A relaunched stateful server answered the session's next call with 404.
>   Claude Code re-initialised on that 404, and re-listed the tools as part of it.
> - **"Attached at session start" was incidental.** What mattered was that the session survived the
>   shutdown at all, which on Claude Code 2.1.282 and later depends on the age of its GET stream (entry 3).
> - **Now.** The server is stateless, so there is no 404, no re-initialise and no re-list. A tool added in
>   a rebuild needs a `/mcp` reconnect to appear.
>
> The measurements below stand as measurements.

**Measured, and it refines a rule this file and `CLAUDE.md` both state more strongly than is true.** The
documented rule is that MCP tools are discovered at session start and a schema change needs a session
restart. Both of these happened in one afternoon:

- Four new tools were added while **no IDE was running**. Building and launching did not surface them, and
  a session restart was needed. Consistent with the rule.
- A fifth was added later, with the IDE **running and attached since session start**. Shutting it down,
  rebuilding and relaunching surfaced the new tool immediately, as a deferred-tool notification, and it
  worked on the first call.

**What it concluded, now superseded.** "The distinguishing condition is whether the server was attached
when the session began", with the advice to try the relaunch before asking. Since #763 a relaunch brings in
no new tool, attached or not. After a rebuild that adds one, ask for a `/mcp` reconnect straight away.

## A relaunch picks up a NEW tool but not a CHANGED one (measured 2026-09-20) — **CLOSED** (superseded by #763, 2026-10-12)

> **Since #763 a relaunch picks up neither.** The server is stateless, so the re-initialise that used to
> bring a new tool in no longer happens. Both now need `/mcp`. The transferable part below, a nullable
> parameter with no default being `required` on the wire, is unaffected.

**Symptom.** Four mark tools gained an optional `project` argument. The documented rebuild cycle ran —
`shutdown_ide`, build, relaunch, `/health` — and the first call to `set_breakpoints` failed with
`An error occurred invoking 'set_breakpoints'.` and nothing else. The session's cached schema still carried
the old parameter list, so the request it built no longer matched what the server accepted.

**What this narrows.** The entry above records that a mid-session relaunch **does** pick up a tool that did
not exist before, measured both ways. This is the other half: a tool whose *signature* changed keeps the
schema the session already has. Added tools yes; changed tools no. The failure is a bare invocation error,
which reads as a broken tool rather than as a stale schema.

**The actual defect underneath it, which is the transferable part.** `string? project` with **no default
value** is `required` in the generated schema — a nullable C# parameter is not an optional JSON one. So the
new argument was mandatory on the wire and every existing caller broke, cached schema or not. Giving all six
`= null` made them genuinely optional, and the same call then worked on the first try after a rebuild.

**How to avoid paying for it again.** Read the generated schema's `required` array, not the C# signature —
this file and CLAUDE.md both say so, and it still cost a cycle. And when a *signature* changes rather than a
tool being added, expect the first call to fail against a stale schema and do not diagnose the server.

**Since #603.** A throw inside a tool is now reported with its exception type and message rather than the bare
line. Not re-measured for this case: a call that does not match the tool's parameters may fail in the SDK's
own argument binding, which can report through `McpException`, and #603 leaves that as the SDK reports it.

## Placing the caret needed a buffer offset that no read tool returned — **CLOSED** (#273 task 3.18, 2026-10-13)

> **Closed by changing the read, not the offsets.** `get_file_content` returns a form's, module's or class's
> whole file, whether or not it is open: a form's `VERSION` line and designer block, a class's header and the
> leading `Attribute` lines, then the code. That is the text an open code window holds, so every offset a
> caller is given (a `type_text` or `press_key` refusal's `CaretOffset=N`, `inspect_element`'s `CaretOffset`,
> `SelectionStart` and `SelectionLength`) indexes the reply directly, and line N of the reply is line N to
> `set_breakpoints`, `get_diagnostics` and the debugger. `set_file_content` takes the same text back, so the
> two changed together: the whole file with its header unchanged, or the code alone, and only a header that
> differs is refused. Its reply counts the lines the document holds from the top of the file. The tool
> descriptions, and the refusal that gives an offset, now say what offsets and lines count from.
>
> **What is not measured yet.** This is closed on headless tests of the rule both tools relay
> (`DocumentContentTests`, including a read and write-back of every document in `demo/` and
> `corpus/designer/`). The live check, a refusal's `CaretOffset` indexing a `get_file_content` reply in the
> running IDE, is #273 task 3.22's, and its result belongs here when it is taken.
>
> **Taken in part on 2026-10-13 (#273 task 3.22)**, a Debug build of 5541621, on four files VB6 wrote. In the
> form whose code starts at line 74 (`frmLogin`), with the caret at offset 0, `type_text`, `press_key` Delete and
> `press_key` Enter were each refused, pointing at offset 2165 as the nearest place that can be edited. With the
> caret put there the status bar read `Ln 74, Col 1`. `get_file_content {"name":"clsDataSource"}`, not open,
> returned the file header first. What was not done is the comparison this entry asked for: reading the reply's
> text at the offset a refusal gives. That the reply is the open window's text rests on the code, where
> `DocumentContent.TextOf` returns the window's own `CodeWindowText`, and on `DocumentContentTests`.
>
> **Still open, and not a gap:** a way to put the caret at a line and column, or at a text match, would save
> every caller the offset arithmetic. That is [#700](https://github.com/hexide-io/HexIDE/issues/700). An offset
> counts every character of the text, so a client that turns `\r\n` into `\n` before counting comes up one
> short per line above the target.

**Symptom (2026-09-25, during #273 task 3.7's live check).** With a class module open,
`type_text {"target":"…/Custom[Root]/None[TextEditor]","text":"X"}` after `press_key {"key":"Home","modifiers":"Ctrl"}`
was refused with a good reply: the offset is in the header, and *"put the caret there with interact
set_property CaretOffset=371"*. That offset is into the editor buffer, which holds the whole file. But
`get_file_content {"name":"Order"}` answers with the code section only, header stripped, so no read tool
returns the text those offsets count into. To reach a given line the caller has to navigate with keys:
`press_key` Ctrl+End, then Up three times, then Home, to land on a member's `Attribute` line.

**Workaround, before the fix.** Take offsets from a refusal's reply, or navigate with `press_key`.
`inspect_element` on the code view reports `CaretOffset` and `BufferBody`, which is still the code section too.

**Do not compute offsets from the file on disk.** Tried on 2026-10-02 (#273 task 3.13): offsets taken from
`BillOfFare.frm`'s bytes landed four characters early in the editor, because the file is UTF-8 and its two
em dashes above that line are three bytes each and one character each in the buffer. A copy and a paste
then took different text from what was intended, which read like corruption until it was traced. This still
holds after the fix: count in the text `get_file_content` returns, never in the file's bytes.

**Suggested fix, as recorded.** #273 task 3.18 makes `get_file_content` return the whole file, which closes the
mismatch. A way to put the caret at a line and column, or at a text match, would still save every caller
the offset arithmetic. Filed as [#700](https://github.com/hexide-io/HexIDE/issues/700).

## No call pressed or released a pointer, so a running form's MouseDown and MouseUp could not be driven — **CLOSED** (#1033, 2026-10-14)

> **Fixed.** `interact` has a `click` verb. It presses and releases at a point on the target (its centre when no point
> is given), with the left, right or middle button and any of Shift, Ctrl and Alt held. It raises PointerPressed and
> PointerReleased on the element a real press there reaches, found by Avalonia's own hit test. So a Button raises
> Click from its release, and a running form raises MouseDown, Click and MouseUp in VB6's order (#974). The reply
> gives:
> - the button and the keys held;
> - the point, in the target's coordinates and the window's;
> - the control the press landed on, when that is not the target: one of its parts, or another control covering it.
>
> A point where nothing takes the pointer is refused, and nothing is pressed. Pinned in `InteractClickTests`, with the
> wording of the description guarded against the refusals in `InteractClickDescriptionTests`.
>
> **Corrected the same day: the release is input of its own.** As first merged, `click` raised the press and the release
> in one dispatcher job, which a person cannot do. On a running form that put MouseUp before MouseDown, every time:
> the MouseDown handler yielded its time slice (#766), and the release ran before the rest of it. The release now comes
> 50 ms after the press, at input priority, so whatever the press set off has run first.
>
> **What it does not do.** It presses and releases at one point. A drag, which is a press, a move with the button
> held and a release elsewhere, is out of its reach, and so is a MouseMove with a button held, since `hover` moves
> with none. The events are raised on the element, as `hover`'s are, not through the input manager, so the press is
> not captured implicitly and `IsPointerOver` is not set.

**Symptom.** (Windows 11, #1032's Debug build at 06b2bf0, 2026-10-14.) A running form's Command1 and Form handle
MouseDown, MouseUp, MouseMove and Click, and each prints to the Immediate window:

| Call, exactly as made | Reply | Immediate |
|---|---|---|
| `interact {"target":"Window/Button[c1]","action":"invoke"}` | `{"success":true,"mechanism":"peer","detail":"invoked Button 'c1'"}` | `Command1 Click` |
| `interact {"target":"Window/Button[c1]","action":"double_click"}` | `{"success":true,"mechanism":"peer","detail":"double-clicked 'c1'"}` | nothing |
| `hover {"target":"Window/Button[c1]","dwellMs":500}`, x and y omitted, so the centre | `{"success":true,"mechanism":"pointer","detail":"hovered (40.7, 16.7); no tip opened within 500ms"}` | `Command1 MouseMove 0 0 600 240` |

Each call did what it says:
- `invoke` clicks through the Button's automation peer, which raises Click and no pointer event;
- `double_click` raises DoubleTapped;
- `hover` raises PointerEntered and PointerMoved, which is why MouseMove arrived.

Nothing raised PointerPressed or PointerReleased, so these were out of reach:
- **Presses and releases:** MouseDown and MouseUp on any control, a right or middle click, and a click with a key
  held.
- **Their order:** MouseDown, Click and MouseUp, which a VB6 program relies on.
- **Controls whose peer does not invoke:** a click on a Label, an Image, or the form's own area.

**Workaround, then.** None through MCP. Headless tests can drive real pointer input, and `MouseEventTests` does, but a
running IDE cannot be driven that way. Raising input from outside the IDE, such as `SendInput`, is a bypass, and it
proves nothing about the surface a caller uses.

**Which form host a desktop check reaches.** F5 on the desktop always runs a form in `VBFormRuntime`.
`ProjectRunnerService` uses `VBMDIFormRuntime` only when `Static.SingleView` is set. That happens under
`ISingleViewApplicationLifetime`, which is the Browser host, or through `Static.ForceSingleView`, which is a
compile-time `false`. No setting or flag reaches it, so a live check on the desktop covers `VBFormRuntime` only.

## No call reached a form behind another form of the running program — **CLOSED** (#1057, 2026-10-14)

> **Fixed.** `window` also takes a top-level window's title, or a running form's name. The name is the form's
> `VBProps` Name, which outlives a caption the program changes, and case is ignored. It is the one parameter that
> every path-resolving tool shares:
> - `dump_visual_tree`, `interact`, `inspect_element` and `take_snapshot`;
> - `type_text`, `hover` and `press_key`.
>
> The replies:
> - **One visible match:** that window.
> - **Two visible matches**, as two instances of one form give: refused with "2 open windows answer to 'Form1'",
>   and their titles.
> - **A hidden match:** refused with "'Form2' is hidden, as a form after Hide is, and a hidden window can't be
>   reached: a person can't click it either."
> - **No match:** refused as before, now listing the open windows, or saying that none is open beside the IDE. A form
>   is listed by its name, then its title, `Form1 ("Score: 0")`, since the name is the stable handle and a caption
>   can be empty or change.
>
> The parameter stays optional, so no schema's `required` changed. Pinned in `ForegroundWindowTests`.
>
> **Verified live,** on #1056's two-form project. With Form2 shown over Form1,
> `interact {"target":"Window/Button[Ping]","action":"invoke","window":"Form1"}` ran Form1's Click, and
> `take_snapshot {"window":"Form1"}` captured Form1 with its label reading "ping 1". Asked for Form2 after
> `Form2.Hide`, `interact {"target":"Window/Button[Hide Form2]","action":"invoke","window":"Form2"}` gave the hidden
> refusal.

**Symptom.** (Windows 11, #1056's Debug build at fbc4fb4, 2026-10-14.) A two-form project, with Form2 shown
modeless over Form1 from a button on Form1. The case to drive was a click on Form1 while Form2 is up.
- **`"auto"`** gave the frontmost window, which was Form2:

  ```
  dump_visual_tree {}
  → {"window":"Form2","root":{"path":"Window", ... "Window/Edit", "Window/Button[Unload Me]"}}
  ```
- **Form1, by name,** was refused:

  ```
  interact {"target":"Window/Button[Ping]","action":"invoke","window":"Form1"}
  → {"success":false,"mechanism":"peer","error":"Unknown window 'Form1'. Use \"auto\" (the frontmost window, the
     default) or \"ide\" (the IDE window, even while a program is running)."}
  ```

`ForegroundWindow`'s remarks had foreseen it. A `"form"` value was left out, because it "would suggest an ability
to pick *which* form, which an enum cannot do once a project shows more than one". #822 made a project show more
than one.

**What was tried from outside the surface, and failed.**
- **Making Form1 the active window:** `SetForegroundWindow` on Form1's handle, found by title and checked against
  the IDE's process, returned `False`. The IDE ran in the background, where Windows' foreground lock applies, so
  `"auto"` could not be steered that way.
- **Clicking it with posted mouse messages:** `PostMessage` of `WM_MOUSEMOVE`, `WM_LBUTTONDOWN` and `WM_LBUTTONUP`
  at the Ping button's client position (102, 270). The position was confirmed afterwards against a snapshot. The
  posts succeeded, and no Click was raised: posted mouse messages do not reach Avalonia's input on this host. The
  `WM_SYSCOMMAND`/`SC_CLOSE` post that #829's workaround uses does work, since it is not input.

## `interact` `click` on a window that has just opened says nothing takes the pointer — **CLOSED** (#1065, 2026-10-14)

> **Fixed.** `click` now waits, up to a second, for the target's window to draw its next frame before it hit-tests. It
> asks the window's compositor for its next batch and awaits that batch's `Rendered`. The other verbs don't wait.
>
> **The cause was the frame, not the layout.** The entry below guessed "before the new window's first layout and
> render", and its suggested fix led with a layout pass. Its own evidence ruled layout out: the button had its bounds,
> and the point fell inside them. A press is hit-tested as a real one is, against what the compositor last *drew*, and a
> window opened a moment ago has drawn nothing. A layout pass alone would have fixed nothing.
>
> Waiting for the frame also covers a case nobody had hit. A control the program has just moved would have been pressed
> where the last frame showed it, not where its layout now puts it.
>
> **If the window draws nothing within the wait,** as a minimised one may not, and the hit test then finds nothing, the
> refusal adds the cause that applied: "or its window, which drew no frame in the 1000 ms the click waited, has nothing
> drawn there yet, since a press is hit-tested against what was drawn: try again once it shows".
>
> Pinned in `InteractClickTests`. A window shown and laid out in the same dispatcher job is refused by the synchronous
> path, as #1065 saw, and clicked by the waiting one. A zero wait gives the new refusal, and `invoke` doesn't wait at all.


**Symptom.** (Windows 11, Debug build of #1063's branch at c69ad5b, 2026-10-14.) A running program's Form1 button
shows Form2 modally. The click on Form2 came straight after the one that opened it, in the same turn:

```
interact {"target":"Window/Button[Show Form2 modal]","action":"click","window":"Form1"}
→ {"success":true,"mechanism":"pointer","detail":"clicked the left button at (60.7, 12.7) … landing on its part 'AccessText'"}
interact {"target":"Window/Button[End]","action":"click","window":"Form2"}
→ {"success":false,"mechanism":"pointer","error":"nothing takes the pointer at (40.7, 12.7) on 'VBCommandButton', which is
   (48.7, 84.7) in the window, so nothing was pressed: the point is outside the target, or on a part of it that is not hit-tested"}
take_snapshot {"window":"Form2"}                      → the End button drawn at that point
inspect_element {"target":"Window/Button[End]","window":"Form2"}
→ {… "isEnabled":true, "boundingRect":[8,72,81.33,25.33] …}
interact {"target":"Window/Button[End]","action":"click","window":"Form2"}
→ {"success":true,"mechanism":"pointer","detail":"clicked the left button at (40.7, 12.7) … landing on its part 'AccessText'"}
```

The point was inside the button by the tool's own reckoning, and the same call worked the second time. The likely cause
is that the hit test ran before the new window's first layout and render, when nothing in it can take a hit yet. The
error gives the two causes a caller would rule out by looking, and not the one that applied, so a caller that had never
seen this would conclude the button was covered or broken.

**Workaround.** Take a `take_snapshot` or `inspect_element` of the new window first, then click. Retrying the click also
works.

**Suggested fix.** Before `InputHitTest`, have `click` make sure the target's top level has had a layout pass, and wait
for its first render if it has had none. Where the hit test still finds nothing, say whether the window has been laid out
and rendered yet.
[#1065](https://github.com/hexide-io/HexIDE/issues/1065).

## `interact` acts on a control that is not on screen, and reports success — **CLOSED** (#1069, 2026-10-20)

> **Fixed.** Every `interact` verb except `invoke_command` and `set_property` is refused on a control that is
> not showing, before its peer is asked to act, and so are `press_key` and `type_text`, which reached the same
> button by other routes (Enter on a hidden Button clicked it). `select` with a text matches among the rows that
> show, so a hidden row is neither selected nor counted against a showing one. The refusal names the verb, says
> the control is not showing, and names the `"isHidden": true` flag the dump shows; for `scroll` it points at the
> container instead. The two reflection actions act on the view model and stay exempt.
>
> "Not showing" gained a case on the way: a control under a container that paints nothing and takes no pointer.
> A running VB6 form hides a Frame or PictureBox that way (`VBVisibility`), so that its controls' code keeps
> running, and those controls had read as visible to both the dump and the tools. Guarded by
> `UiAutomationDriverTests`, including a path read from the dump and resolved, as a caller addresses it.

**Symptom.** Measured on 2026-10-15 in AI Chat's transcript (#273 task 3.22). Each message is templated with an
Approve and a Reject button, shown only while that message is an approval prompt. So `dump_visual_tree` lists a
pair per message, each one `"isHidden": true` except the pair of the prompt awaiting a decision. Invoking a
hidden one:

```
interact {"target":".../TitleBar[AI Chat]/Pane[AI Chat]/Custom/Custom/Pane[Scroller]/List/Button[#0]","action":"invoke"}
→ {"success":true,"mechanism":"peer","detail":"invoked Button 'Approve'"}
```

Nothing happened: that message was never a prompt, and its view model ignores the decision. No person can press a
button that is not shown, and a caller who takes the first `Approve` in the tree reads that it approved the
pending call. A control whose handler has no such guard would act out of sight.

**Workaround.** Pick the node without `isHidden`. In AI Chat's transcript, the pending prompt's pair is the only
one without it.

**Suggested fix.** Refuse a provider or pointer action on a control that is not effectively visible, saying why
and naming the `isHidden` flag. [#1069](https://github.com/hexide-io/HexIDE/issues/1069).

## `dump_visual_tree` shows a node cut off by `maxDepth` as a leaf, and under `interactiveOnly` drops it and its ancestors — **CLOSED** (#953, 2026-10-20)

> **Fixed.** A node at the cut-off still has its children counted, without walking them, and carries
> `"truncated": true` when it has any. Under `interactiveOnly` such a node is kept rather than pruned, since what
> lies below it was not looked at, so the cascade that removed its non-interactive ancestors stops there. The
> `maxDepth` and `interactiveOnly` parameter descriptions say so and give the next step: dump again with the
> node's path as `root`. Guarded by `UiAutomationDriverTests` (a node cut off at the cap is kept and marked, a
> real leaf at the cap is not, and a deep enough walk marks nothing).

**Symptom.** With a form open in the designer (Linux, openSUSE Leap 16 under XWayland, Debug build of main at
b15c7cf, 2026-10-13):

```
dump_visual_tree {"maxDepth":8}
dump_visual_tree {"root":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/Pane[#0]","maxDepth":2}
→ both: Pane[#0] → Tab[PART_TabStrip] with "children":[], and no Custom[Root]
dump_visual_tree {"root":"<the same path>","maxDepth":6,"interactiveOnly":false}
→ Custom[Root] → … FormContainer → ControlsContainer → ListItem[Picture0] …
```

The designer pane appears to hold only a tab strip. A node at the cap is emitted with `"children":[]`, the same
as a real leaf, and nothing records that the walk stopped. Under the default `interactiveOnly: true` that empty
list prunes a non-interactive node at the cap, and the prune cascades up through every non-interactive ancestor:
here the designer's ScrollViewer, then `Custom[Root]`, the designer itself. That breaks `interactiveOnly`'s
promise to keep a node with an interactive descendant. It is the #362 trap from this document's header, and a
smaller `maxDepth`, the natural answer to an overflowing whole-window dump (#699), walks into it. Cross-platform by
code reading.

**Workaround.** Do not read `"children":[]` as a leaf, or a missing node as absent. Dump again from the deepest
node shown, with its path as `root`, a larger `maxDepth` and `interactiveOnly: false`.

**Suggested fix.** At the cap, check for children without recursing and mark a node whose children were not
walked `truncated: true`; under `interactiveOnly`, keep such a node instead of pruning it. Say so in the
`maxDepth` and `interactiveOnly` parameter descriptions. [#953](https://github.com/hexide-io/HexIDE/issues/953).

## `select` on a tree item reports success and leaves the tree's selection where it was — **CLOSED** (#714, 2026-10-20)

> **Fixed.** The cause was not the one first recorded below. Avalonia 12's `TreeViewItem` peer has no
> `ISelectionItemProvider`, so `select` always went through `TrySelectThroughOwningTree`, which set the item's
> `IsSelected` and read that back. Avalonia turns that into `SelectedItems.Add`, and moves `SelectedItem` only
> when nothing was selected. With a selection already there, both items were highlighted, the bound property
> stayed put, and the read-back said yes. Any tree with a selection was affected, and `double_click` pre-selects
> the same way, so on the Project Explorer it would have opened the node selected beforehand.
>
> The driver now sets the owning tree's `SelectedItem` and reads that back, as it does for a grid row. `select`
> with a `value` now considers tree items, which it had dropped for lacking the provider, and an empty tree's
> refusal no longer talks about dropdowns. The Options, Locals and Watches trees name their items after their
> text, as the Project Explorer's already did, so a reply names what it selected and a path can say
> `TreeItem[Theme]`. Guarded by `UiAutomationDriverTests`, which start from a selection already made.
>
> Review found that once tree items could be selected by text, an item under a collapsed node had no way to be shown:
> the node's peer offers no ExpandCollapse provider either, so `expand` refused. `expand` and `collapse` now set a
> node's `IsExpanded` and read it back, and the dump advertises `expandCollapse` on a node with children. A text that
> matches only an item under a collapsed node names that node to expand. One under a node never expanded matches
> nothing, since its item is not created until then, so the refusal lists the collapsed nodes instead of telling the
> caller to check the spelling. The read-back confirms the tree took the item, not that a bound view model accepted
> it. Avalonia cannot tell those apart, and the one tree whose view model refuses, Watches for its child nodes, is
> filed as #1229.

**Symptom (2026-10-02, during #273 task 3.15's live check).** In Tools → Options, the page tree's items each
advertise `selectionItem`. `interact {"target":"Window/Custom/Tree/Pane/TreeItem[#0]/TreeItem[#1]","action":"select"}`
(the Theme item) replies `{"success":true,"mechanism":"peer","detail":"selected 'TreeItem'"}`, and nothing has
changed: `inspect_element {"target":"Window"}` still has the same `SelectedNode`, and the page beside the tree
is still `EnvironmentGeneralPageView`. Seen on three launches. Selecting by text fails differently:
`interact {"target":"Window/Custom/Tree","action":"select","value":"Theme"}` replies `'Tree' has no selectable
items realized`, although `dump_visual_tree {"root":"Window/Custom/Tree/Pane/TreeItem[#0]","interactiveOnly":false,"maxDepth":4}`
shows every item realized, with its text in a child `Text[Theme]`. The items have no automation name of their
own, which is also why the success reply calls the item `'TreeItem'`.

**Workaround.** Keyboard: `press_key {"target":"Window/Custom/Tree/Pane/TreeItem[#0]/TreeItem[#0]","key":"Down"}`
moves from General to Theme and the page follows. A theme is then switched by `interact expand` on
`Window/Custom/Custom/ComboBox`, `interact select` with the theme's name as `value`, and `interact invoke` on
`Window/Custom/Button[OK]`.

**Suggested fix.** `DoSelect` takes the item peer's `Select()` as done. It should read the selection back, from
the item and from its owning tree, fall back to `TrySelectThroughOwningTree` when it did not take, and say so
when neither did, rather than reporting a selection it never checked. Naming a tree item after its header text
would let `select` with a `value` find it, and would let the reply name what it selected. Filed as
[#714](https://github.com/hexide-io/HexIDE/issues/714).

## `interact` `select` on a dropdown item leaves the dropdown open — **CLOSED** (#907, 2026-10-20)

> **Fixed.** `select` on an item in an open dropdown now closes the dropdown, as clicking the item does, and its reply
> says so: `selected 'VBA' and closed the dropdown, as a click does`. That holds for `select` on the item itself and
> for `select` with a `value` on the ComboBox. `double_click` on such an item does the same and raises no
> double-click, since a real one's second click lands on what lay beneath. The item's peer sets the selection and
> nothing else, and Avalonia's ComboBox closes its popup only from pointer, key and visibility handling. The
> workaround under "A collapsed ComboBox reports only its selected item", which said the popup closes between calls,
> is corrected: that described the IDE window losing focus to the caller, which closes a light-dismiss popup.
> Guarded by `UiAutomationDriverTests`.

**Symptom.** On the Object Browser's library box (Linux, openSUSE Leap 16 under XWayland, Debug build of main at
b15c7cf, 2026-10-13):

```
interact {"target":".../List[#0]/Pane[#0]/Custom/ComboBox","action":"expand"}
interact {"target":".../ComboBox/Pane/ComboBoxItem[VBA]","action":"select"}
→ selected 'VBA'
```

The selection changed, but the popup stayed open over the Classes list until
`interact {"target":".../ComboBox","action":"collapse"}`. Clicking the item closes it, and the reply does not say
it is still open. `select` sets the selection through the item's peer, and Avalonia's ComboBox closes its popup on
pointer, key and visibility changes, never on a selection change. The mechanism is cross-platform by code
reading; whether a caller sees the popup linger depends on whether the IDE window keeps focus. Same family as the
menu entry still open in `docs/mcp-server-gaps.md` ([#947](https://github.com/hexide-io/HexIDE/issues/947)): the
driver acts on a control without the close a real gesture performs.

**Workaround.** After `select` on a dropdown item, `collapse` the ComboBox. Do not rely on the popup closing
between calls.

**Suggested fix.** After selecting in an open dropdown, `select` closes it as a click does, and says so in its
reply. [#907](https://github.com/hexide-io/HexIDE/issues/907).

## `interact` on a menu skips what a real gesture does: `collapse` leaves the menu bar active, `invoke` leaves the menu open — **CLOSED** (#947, 2026-10-20)

> **Fixed.** `interact` on a menu now does what a click does, in the order Avalonia 12's own menu handling uses.
> `invoke` on an item moves a checkable item's tick, raises Click, and closes the whole menu, bar included, unless
> the item stays open on click. Its reply says so: `invoked MenuItem 'Minimap' (now unticked) and closed the menu, as
> a click does`. Context menus and flyouts close the same way. `collapse` on a menu bar's header closes the bar with
> it: `closed menu 'Format' and left the menu bar`, and a `collapse` with nothing open says there is nothing to
> close. A disabled item is refused rather than clicked, with a focus hint when its command is routed. `toggle` on a
> menu item says that `invoke` clicks it and reports whether the tick moved. `invoke_menu_item` moves the tick of a
> checkable item that has no command, such as View/Minimap, where it used to refuse. Guarded by `MenuPopupTests`, and
> measured on Windows by an agent driving Format, View/Minimap and the Project Explorer's flyout.


**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
interact {"target":"Window/Custom[MainView]/None[#0]/Menu[MenuBar]/MenuItem[Format]","action":"collapse"}
→ closed menu 'Format'
```

The Format header stayed highlighted in every later snapshot until
`press_key {"target":"Window/Custom[MainView]/None[#0]/Menu[MenuBar]","key":"Escape"}`. `interact` `invoke` on the
Project menu's `MenuItem[Remove Form2]` left the Project dropdown open, still drawn over the Object Browser after
`activate_document_tab`. `collapse` closes only the submenu, not the menu bar, and `invoke` only raises Click,
where a real click also toggles a checkable item and closes the menu. So, read in code and not run, `invoke` on
the eight checkable items (the toolbar band's flyout, View > Toolbars and Minimap) changes nothing, and `toggle`
and `invoke_menu_item` refuse them. Cross-platform by code reading.

**Workaround.** `press_key` Escape on the MenuBar clears a header that `collapse` left active. What closes a
dropdown that `invoke` left open was not measured, so check a snapshot after any menu `invoke`.

**Suggested fix.** Do what Avalonia 12.1.4's own peer does: `collapse` on a top-level item closes the menu bar,
and `invoke` toggles a checkable item, raises Click, then closes the menu unless `StaysOpenOnClick`. Have
`MenuPopupTests` assert that the menu is closed after each. [#947](https://github.com/hexide-io/HexIDE/issues/947).

## Invoking a runtime menu item can leave a checkmark it did not request — **CLOSED** (#947, 2026-10-20)

> **Fixed.** Avalonia's menu flips a CheckBox item's `IsChecked` before it raises Click, and every run-time menu item
> is a CheckBox item so that code can tick it. `VBMenu` read `IsChecked` inside its Click handling to keep the
> program's value, so it kept the flipped one: a pointer click, Enter or an access key on a running form's menu item
> ticked or unticked it. `VBMenu` now holds the program's `Checked` and puts it back before the item's Click runs, so
> a Click that sets Checked still has the last word. The `interact` calls recorded below do not reach that path on
> the build they ran on: `invoke` then only raised Click, which never moves a tick. So the ticks seen on 2026-10-18
> most likely came from a click or key that was not recorded, and they were not reproduced through those calls.
> Since #947 `invoke` clicks as the menu does, tick included, and meets the same guard. Guarded by
> `RuntimeMenuTests`, by pointer and through `interact` `invoke`, and measured on the running Minesweeper demo:
> invoking New moved no tick, and invoking Intermediate moved the tick there from Beginner, as its Click sets it.

**Symptom (2026-10-18, #1156 live verification).** On the running Notepad form, these calls succeeded:

```text
interact {"window":"frmNotepad","target":"Window/Menu/MenuItem[&Edit]","action":"expand"}
interact {"window":"frmNotepad","target":"Window/Menu/MenuItem[&Edit]/Pane/MenuItem[&Copy]","action":"invoke"}
```

After invoking Copy and Select All, `take_snapshot {"window":"frmNotepad"}` showed checkmarks beside both items.
Neither handler sets Checked and neither menu is declared checked. The VBMenu click override normally preserves
Checked, so the automation peer's invocation path needs checking against the ordinary click path. A breakpoint
confirmed a subsequent Copy invocation did reach `mnuEditCopy_Click`; the unexpected tick is separate from dispatch.

**Workaround.** Drive the runtime menu shortcut with `press_key`, for example
`{"window":"frmNotepad","target":"Window/Edit[#1]","key":"C","modifiers":"Ctrl"}`. Select text first and
use a breakpoint to distinguish the VB6 handler from a TextBox's own shortcut handling. For opening a menu,
`expand` only opens it; explicitly invoke its parent when testing that parent's VB6 Click handler.

**Suggested fix.** Make the runtime menu's automation invocation preserve the program's Checked value, as its
pointer click path does, and cover both paths with a regression test.

## A context-menu action bound to a method cannot be driven — **CLOSED** (#947, 2026-10-20)

> **Fixed.** It can be driven through the UI, as a person does it: `select` the form's node, `expand` the Project
> Explorer's tree, which opens its flyout, then `invoke` **Remove Form1**. Measured on Windows on #947's branch, by an
> agent that was told nothing about the route's history: `invoke` replied `invoked MenuItem 'Remove Form1' and closed
> the menu, as a click does`, the flyout was gone, and Form1 had left the project with no prompt. Before #947 the same
> `invoke` left the flyout open. `invoke_command` still cannot reach a public method, which
> [#836](https://github.com/hexide-io/HexIDE/issues/836) keeps open as a convenience; it is no longer the only route.


**Symptom.** Live-checking #831 needed a project with no startup form, so Form1 had to be removed. Both of the UI's
routes are out of reach:

```
invoke_menu_item {"path":"Project/Remove Form1"}
→ {"success":false,"error":"'Project/Remove Form1' is a submenu or has no command"}
interact {"target":".../Pane[Project Group - Group1]/Custom/Tree/Pane/TreeItem[Project1 (Project1.vbp)]/TreeItem[Form1 (Form1.frm)]","action":"select"}
→ {"success":true, ... "selected 'Form1 (Form1.frm)'"}
interact {"target":".../Pane[Project Group - Group1]/Custom","action":"invoke_command","value":"DeleteForm"}
→ {"success":false,"mechanism":"reflection","error":"no public property 'DeleteForm' on ProjectToolViewModel"}
```

The Project menu's item is inert, which is a product defect (#835). The Project Explorer's context menu binds
**Remove Form1** to a method, `ProjectToolViewModel.DeleteForm()`, rather than to an `ICommand`. So `inspect_element`
does not list it and `invoke_command` cannot reach it. This entry first said that no tool opens a context menu; that
was wrong (below). The refusal also names a *property* when a command was asked for.

**Corrected 2026-10-13: `expand` opens the context menu.** `interact` `expand` on a control that owns a ContextMenu
or flyout opens it, and this tree's flyout was measured that way when #343 closed (`interact expand …/Custom/Tree →
"opened flyout on 'Tree'"`, in the archive). No tool description says so, which is how this entry missed it
([#949](https://github.com/hexide-io/HexIDE/issues/949)). Remove binds `Command="{CompiledBinding DeleteForm}"`,
which Avalonia turns into a command, so `invoke` on that item with the flyout open should run it and, since #947, close
the flyout as a click does. That is read in code, not run. Remove acts on the selected form
and asks nothing, so check that `select`'s reply names that form, and that the item's header reads **Remove** and its
name, before invoking it. Until the route is checked live, the workaround below stands.

**Workaround.** Load a `.vbp` whose `Startup` names no form in the project, by passing it as the positional argument
at launch.

**Suggested fix.** `invoke_command` falling back to a public parameterless method, listed by `inspect_element`, and
a refusal that names what it looked for rather than a property. An action to open a context menu is not needed:
`expand` is it. [#836](https://github.com/hexide-io/HexIDE/issues/836).

## An action on a running form replies before the program's handler has finished — **CLOSED** (#1232, 2026-10-20)

> **Fixed.** `interact`, `press_key` and `type_text` on a running program's form, on a window it owns, or on its
> MsgBox or InputBox, now wait until the program has finished what the action set off and is waiting for input again,
> up to 3 seconds. A handler parked in `Show vbModal`, or at a `DoEvents` in a loop that was already running before
> the action (or in a Timer's), counts as waiting for input; a `DoEvents` loop the action itself started is waited for,
> up to the same limit (corrected by #1254, below). The program is found before the action, as a MsgBox that the
> action answers has closed and lost its owner by the time the action returns. A reply that comes back sooner ends
> with a note saying why:
>
> - the program is paused in the debugger: `(the program is paused at Form1 line 23 (Breakpoint), with Command1_Click
>   not finished; it runs on, and the form may change, once the program continues)`;
> - it has a window that is not one of its forms open over the form, a MsgBox most often: `(the program is still
>   running Command1_Click, and has a window open over its form, 'Question'; a MsgBox holds its handler until it is
>   answered, so the form may change after that)`;
> - it was still running at the limit: `(the program was still running Command1_Click 3s later, so the form may still
>   change)`.
>
> A form the program opened is named: `(the program opened 'Form2')`. Guarded by `ProgramWaitTests`.

**Symptom.** Found during #947's live check (Windows, Debug, 2026-10-20). On the running Minesweeper demo:

```
interact {"window":"frmMines","target":"Window/Menu/MenuItem[&Game]/Pane/MenuItem[&Intermediate]","action":"invoke"}
→ invoked MenuItem '&Intermediate' and closed the menu, as a click does
take_snapshot {"window":"frmMines"}
→ the 16×16 field, with the mine counter still reading 010
```

A later snapshot read `040`. `mnuLevel_Click` sets the counter last, after rebuilding the field, and the reply had
gone out while it was part way through. Nothing in the reply said so.

**Cause.** A handler is a walk on the run's `EventPump`. `ExecuteSub` starts it and returns, and the walk gives up
the UI thread at the debugger's 15 ms time slice (#766), so the tool's reply was sent mid-walk.

## A running form's controls carry no VB6 name, and a `&` caption is named with `_` — **CLOSED** (#954, 2026-10-20)

> **Fixed.** Every control on a running form carries the name its code uses as its AutomationId: `Text1`, or
> `Command1(0)` for an element of a control array, including one `Load` adds at run time. A tree shows it as the
> path's discriminator, `Window/Edit[Text1]`, and a tool reaches it as `Window/#Text1` at any depth. A CommandButton,
> CheckBox, OptionButton or Frame is named by its caption as it reads: `&OK` is "OK", `E&xit` is "Exit", `Tom && Jerry`
> is "Tom & Jerry", so `Button[Exit]` reaches it. A Label is too, its `&` dropped only where UseMnemonic marks a key.
> The rule is Windows' documented one for prefix characters, not measured on the oracle (#920 owes that); a trailing
> `&`, which Microsoft does not document, is kept.
>
> The id makes a node of a container the tree used to fold away, so the tree now follows the form's containment: a
> control in a Frame is `Window/Group[Frame1]/Button[cmdOK]`, in a PictureBox `Window/Pane[Picture1]/...`, where both
> used to sit directly under `Window`. A Label is `Text[Label1]` and an Image `Image[Image1]`; a Timer, Shape, Line or
> CommonDialog has no type of its own and shows as `None[Timer1]`. Designer controls are left as they were (#526). Not
> covered: the constituent controls of a UserControl, which are built without the loader. Guarded by
> `RunningFormNamesTests`.

**Symptom.** Found on Linux and read in code on Windows (#954):

```
dump_visual_tree {"window":"auto"}
→ Window/Button[_OK]          (Command1, caption "&OK")
  Window/Edit, Window/List    (Text0, List0: no name)
```

No node carried `Command1` or `Text0`, so a caller holding names from the code or from `get_form_controls` had no
bridge to the running form. Seen again in #1232's live check: the agent had to guess that `Window/Edit` was `txtResult`.

**Cause.** The VB6 name was kept only in HexIDE's attached `VBProps.Name`, and the button's raw caption reached the
automation peer through the `AccessText` the template draws, with `&` turned into Avalonia's `_` marker.

## A running form's menu items are named with VB6's `&` — **CLOSED** (#1128, 2026-10-20)

> **Fixed.** A run-time menu item is named by its caption as it reads, `&` removed and `&&` read as one `&`, and carries
> its VB6 name as its AutomationId, so the Game menu dumps as `MenuItem[mnuGame]` named "Game" and is reached as
> `#mnuGame` or `MenuItem[Game]`. A path written with the `&`, `MenuItem[&Game]`, no longer matches. Fixed with #954;
> guarded by `RunningFormNamesTests`.

**Symptom.** Measured on 2026-10-17, driving the Minesweeper demo's menued version (#1123, caab5bdd):

```
dump_visual_tree {}
→ {"path":"Window/Menu/MenuItem[&Game]","controlType":"MenuItem","name":"&Game",...}
interact {"target":"Window/Menu/MenuItem[&Game]","action":"expand"}
→ ... {"path":"Window/Menu/MenuItem[&Game]/Pane/MenuItem[E&xit]","name":"E&xit",...}
```

The menu draws "Game" and "Exit", with the access key underlined. The name and the path carry the raw caption, so a
caller must know VB6's caption syntax to address what it can see.

**Workaround.** Put the `&` where the caption has it: `MenuItem[&Game]`, `MenuItem[E&xit]`.

**Suggested fix.** Name each run-time menu item by its caption as drawn, `&` removed and `&&` read as one `&`. The IDE's
own menus had the same fault with `_` (#578). [#1128](https://github.com/hexide-io/HexIDE/issues/1128).

## No reply mentions a modal dialog, whether it was already open or the action opened it — **CLOSED** (#681, 2026-10-20)

> **Fixed.** A tool's reply names each modal dialog open once the call is done, in a `modalDialog` field (a block of
> its own when the reply is not a JSON object). The call still acts; refusing would also stop a caller answering the
> dialog through the same tools. Measured with a cold agent on a Debug build, in the wording since tightened at its
> request:
>
> - `invoke_menu_item Tools/Add Procedure...` → `"modalDialog":"'Add Procedure', a modal dialog over 'Ask - HexIDE
>   [design]', opened during this call and waits for an answer. dump_visual_tree with 'window' set to its title shows
>   it, and interact answers it."`
> - `get_file_content`, `set_breakpoints` and `dump_visual_tree {"window":"ide"}` under it → `"… is still open and
>   waits for an answer; this call reached past it, as a person could not. …"`
> - `continue_program` into a division by zero → `"note":"Running; no pause within 2 s. It raised Run-time error '11'
>   …","modalDialog":"'HexIDE', a modal dialog over the running form 'Ask', opened during this call …"`, and
>   `get_debug_state` after it, still `Running`, names the same dialog.
>
> Which dialogs are named follows the window the call acts in, resolved as the tools resolve it: the IDE's, or for a
> window tool the one its `window` argument picks. The dialog the call acted in, and any under it, are not named, so
> `dump_visual_tree {}` and `interact` left to the frontmost window, which is the dialog, get no note. "Reached past
> it" is said only when the dialog blocks that window; a program's MsgBox over its form is named as open, since it
> does not stop a person using the IDE. A dialog the wait for a program (#1232) named in the reply is not named again;
> every other is, the program's included (#1253). A program's MsgBox or InputBox over a hidden form, as from
> `Form_Load`, waits in a window of its own that is not shown as a dialog, and is counted too; a tool window such as
> Find is not. Each look at the windows is bounded at 500 ms, and one that fails, as after `shutdown_ide`, leaves the
> reply as the tool gave it. A tool that throws gets no note. The `take_snapshot` half of the suggested fix, preferring
> a modal with `window: "ide"`, is not done: the note now names it, and `"ide"` asks for the IDE's own window. Guarded
> by `OpenDialogsTests` and `OpenDialogNoteTests`.


**Symptom.** (Windows 11, Debug build of #838's branch, merged with main at 2047857, 2026-10-13.) Paused at a
breakpoint on line 3 of a module, with the caret moved to line 2, an `Attribute` line:

```
invoke_menu_item {"path":"Debug/Set Next Statement"}
→ {"success":true,"note":"Invoked 'Set Next Statement'."}
take_snapshot {"window":"ide"}
→ {"path":"…_3.png","width":1920,"height":974,"scale":1.5}
dump_visual_tree {"maxDepth":3,"interactiveOnly":false}
→ {"window":"HexIDE","root":{… "className":"MessageBox" … "Can't set the next statement here — only a
   top-level statement of the currently paused procedure is supported …" … "Button[OK]"}}
take_snapshot {}
→ {"path":"…_4.png","activeDialog":"HexIDE","width":642,"height":172,"scale":1.5}
```

The command refused the move and opened a message box saying so. The first reply reads as if the move had
happened. The second is the main window with no `activeDialog`, and the box is not in the image, although
`window: "ide"` is the documented way to reach the IDE while a program is paused, and the tool's description
says an open modal is captured in preference to the main window. Only the default window finds it. #681
measured the first half with Tools/Add Procedure left open; this is a modal raised by the action itself, which
[its Linux comment](https://github.com/hexide-io/HexIDE/issues/681#issuecomment-6146516046) describes.

**Workaround.** After any action that might open a dialog, `dump_visual_tree {}` and read its `window`. A
`take_snapshot {}` with no `window` captures the dialog.

**Suggested fix.** #681's decision, for the replies. For `take_snapshot` with `window: "ide"`, prefer a modal
the IDE owns, as `auto` does, or at least name it in `activeDialog`.
[#681](https://github.com/hexide-io/HexIDE/issues/681).

**Seen again with #1011's halt** (2026-10-15, Debug build of `fix/1011-halt-on-error`). A click divides by zero, and the
program halts at the run-time error dialog. After Debug, a Continue without a fix retries the statement, which halts again:

```
continue_program {}
→ {"success":true,"note":"Running; no pause within 2 s. It raised Run-time error '11': Division by zero at 10 \ d;
   get_last_runtime_error has it."}
get_debug_state {}
→ {"running":true,"state":"Running"}
```

Both replies say Running. The program is held at a dialog that waits for End or Debug, and nothing of it runs. Naming
the error helps, but neither reply says the program is waiting for an answer. `dump_visual_tree {}` finds the dialog, as
above.

## A whole-window `dump_visual_tree` overflows the caller's output limit — **CLOSED** (#699, 2026-10-20)

> **Fixed.** `dump_visual_tree` takes `match`: text to find instead of the tree. It returns the nodes whose Name,
> AutomationId, ClassName, ControlType or `typeNameAsName` contains it, ignoring case, or whose DataContext type does
> where that context starts, each with its path and without its subtree, up to 50, and a `note` that counts them and
> the branches `maxDepth` cut off unsearched. Every node in the tree is searched, interactive or not; a document in a
> tab that is not selected is not in the tree, so is not found. Measured with a cold agent on a Debug build, an IDE
> with one code window and one designer open:
>
> - `dump_visual_tree {"match":"TextEditor"}` → `1 node matches 'TextEditor'.`, the editor's path, with its class and
>   `CodeEditorViewModel`, in a reply of a few hundred characters. The Immediate window's editor matches too when it
>   is on screen.
> - `{"match":"PropertiesTool"}` → `1 node matches 'PropertiesTool'.`, the Properties window's view.
> - `{"match":"Properties"}` → 9 nodes: two toolbar buttons and the Properties window's parts. Before a node answered
>   by its DataContext type only where that context starts, it was 22, most of them scroll bar parts that inherited
>   `PropertiesToolViewModel`. Where a context starts is decided by identity, so a nested tree item's own context of
>   its parent's type, as in Locals, still answers.
>
> The whole-window dump itself is unchanged, and can still overflow; the tool's description now offers `match` beside
> `root`. Not done: a bound on the walk's cost, which grows with the square of a parent's same-type children (5,000
> sibling buttons took 8.7 s, measured by the reviewer), and a count of subtrees a failing control dropped from the
> walk; both were so before. Guarded by `FindTests`.


**Symptom (2026-09-25).** `dump_visual_tree {"maxDepth":12}` with an IDE showing three document tabs and
the usual tool windows returned 58,264 characters. That is over the client's limit, so it was written to a
file instead of being returned. The only way to find the code editor's path was to parse that file outside
the tool. The description does say to scope a large dump with `root`, but a first-time caller does not yet
know any path to scope it with.

**Workaround.** Since #953, walk down instead: dump the whole window with a small `maxDepth` (3 or 4), then dump
again with a node's path as `root` wherever it carries `"truncated": true`. Each step stays small, and a cut-off
subtree is marked rather than silently dropped. Dumping once to a file and extracting paths with a script still
works when a question needs the whole tree at once.

**Suggested fix.** A filter on class name, view-model type or name, returning matching nodes with their
paths and no subtrees. This is the question a first call usually asks: where is the code editor? Filed as
[#699](https://github.com/hexide-io/HexIDE/issues/699).

## `get_last_runtime_error` does not say which module or line raised the error — **CLOSED** (#955, 2026-10-20)

> **Fixed.** An error carries the module and line it was raised at, the line counted as the code window numbers it,
> header included, and the one the debugger halts on: the innermost statement's, or a condition's own, as an ElseIf's
> or a Loop Until's. The innermost statement it passes out of notes them; a condition, a halt, and an error a statement
> loop raises itself (Resume without an error, a label that is not defined) note them where they are.
> `get_last_runtime_error` returns them as `module` and `line`, and a run tool's note names them. Run-time errors,
> compile errors met mid-run and constructs HexIDE does not implement carry them alike, from a form's event, a halt, or
> Sub Main. Measured with a cold agent on a Debug build, a click that divides by zero:
>
> ```
> continue_program {}
> → {"note":"Running; no pause within 2 s. It raised Run-time error '11': Division by zero at 10 \ d (in frmAsk,
>    line 53); get_last_runtime_error has it.", …}
> get_last_runtime_error {}
> → {"raised":true,"message":"Run-time error '11':\n\nDivision by zero\n\nat 10 \ d","at":"…","sequence":1,
>    "module":"frmAsk","line":53}
> ```
>
> Line 53 of the file is `txtResult.Text = CStr(10 \ d)`. Not covered: an error no statement raised, such as a syntax
> error found before the run began, which names its module in the text already; and the dialog's own text, left as it
> was (#833). Guarded by `ErrorLocationTests`.


**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
get_last_runtime_error {}
→ {"raised":true,"message":"Compile error:...","at":"2026-10-13T03:05:25.1331302+01:00","sequence":1}
```

The dialog read "Compile error: Method or data member not found (ListCount in Right(Control))", with Debug
disabled (#9), so once it closed neither a person nor a caller could reach the line. The reply carries text,
time and sequence only. A run-time error's text ends with the statement but no module or line, and a compile
error met mid-run carries neither, although the interpreter tracks both for the debugger. Cross-platform by code
reading.

**Workaround.** None beyond searching the project for an identifier the message names.

**Suggested fix.** Note the module and line on the exception on the way out of the statement walk, for run-time
and compile errors alike, and carry them through to `module` and `line` in the reply. Leave the dialog's text
alone: what VB6 shows for a compile error met mid-run is #833's measurement.
[#955](https://github.com/hexide-io/HexIDE/issues/955).

## A Label on a running form is left out of the default tree — **CLOSED** (#1076, 2026-10-20)

**Symptom.** Measured on 2026-10-15, driving the Battleship demo (#1071), whose board is 25 Labels and a status Label:

```
dump_visual_tree {"interactiveOnly":false,"maxDepth":20}
→ {"window":"Battleship - Admiral","root":{"path":"Window",...,"children":[{"path":"Window/Menu",...,"isHidden":true,
   "children":[]}]}}
```

None of the 26 Labels is there, with `interactiveOnly` false (the default is true). A pointer reaches them, so they
are on screen: `interact {"target":"Window","action":"click","value":"20,20"}` replies "landing on its part 'VBLabel'".
Nothing in the reply says controls were left out.

**Narrowed by #954.** A Label now carries its VB6 name and is named by its caption, so `Window/#lblStatus` reaches it,
`inspect_element` reads its caption as the node's name, and a dump with `interactiveOnly` false shows it as
`Text[lblStatus]`. What remains: the default dump, `interactiveOnly` true, still leaves it out, because a Label has no
automation provider and cannot take focus, though a program may handle its Click.

**Workaround.** Pass `interactiveOnly: false`, or target `Window/#lblStatus` by the name in the code.

**Suggested fix.** Count a run-time Label as interactive in the default dump, or every node with a run-time id: the
caption is often what a program reports through. [#1076](https://github.com/hexide-io/HexIDE/issues/1076).

> **Fixed.** The default dump keeps every control a running program names, a Label among them, though it has no
> automation provider and cannot take focus: the program may handle its Click, and its caption is often what the program
> reports through. It shows as `Text[lblStatus]`, named by its caption, with the containers above it. Belonging to a
> running program is decided as its event dispatch decides it, by logical ancestry, so a control in an open menu counts;
> the form designer's controls, named the same way, do not, and a Timer and a CommonDialog, which never show, are left
> out. On a form of many Labels the default now keeps nearly everything, so `root` scopes it. Guarded by
> `RunningFormNamesTests`.

## Breakpoint gutter hover target — **CLOSED** (#1176, 2026-10-18)

`dump_visual_tree {"maxDepth":24,"interactiveOnly":false}` did not expose the breakpoint margin.
`hover {"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/Pane[#0]/Custom[Root]","x":19,"y":229,"dwellMs":500}`
resolved to TextEditor and returned "no tip opened within 500ms": its synthetic move never reached
an unaddressable child margin's pointer handler. Clicking through the editor still hit-tests the gutter.
The fix in #1176 exposes the margin with a control automation peer and stable `BreakpointMargin` ID,
so callers can target its own hover handler. Declarative tooltip visibility remains subject to the
existing synthetic-pointer limitation; assert the reported declared text.

The same acceptance run found a product hit-testing defect: `interact` with the margin target,
`action:"click", value:"7,111"` reported that the press landed on `DockPanel`, not the margin.
Only painted marker shapes were hit-testable. F9 was a working workaround. The fix in #1176 paints
a transparent rectangle across the whole gutter so empty lines accept clicks and real pointer hover.

## Clipped Call Stack selection reports success without changing the frame — **CLOSED** (#1182, 2026-10-20)

During #1180 acceptance on Windows, this call reported success but Watches remained in Worker and refused
`callerValue`; the intended caller frame was not selected:

```json
{"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/TitleBar[Call Stack]/Pane[Call Stack]/Custom/List/Pane[PART_ScrollViewer]/ListItem[CallStackRow { Frame =    Main, Line = 6, Stop = DapStop { SessionId = 0734e6a8-bc5a-48fe-a5d4-b685c2a2aa05, Generation = 3, ThreadId = 1, Reason = breakpoint }, FrameId = 1 }]","action":"select"}
```

The path came from `dump_visual_tree` with `interactiveOnly:false`. Tool panes were clipped in a small window.
Workaround: hide unused panels, resize, then use
`press_key {"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/TitleBar[Call Stack]/Pane[Call Stack]/Custom/List","key":"End"}`.
This selected Main and its watch returned 42. Investigate provider dispatch and virtualization; selection
replies should verify/report the resulting model selection. The underlying cause is not yet established.

> **Fixed.** `select` reads the selection back before it reports one: an item its list did not take is refused as "'Main'
> was not selected: its list did not take the selection; 1 item is selected now: Worker", where it used to be reported as
> selected. Three ways a select could come to nothing are covered: an item its list cannot place, a list that puts its
> selection back, and, on the Call Stack, a row of a stop the debugger has already left, which the debugger declines and
> the list now gives back. A list its selection handler replaces lets the item go, so a selection the list announced
> there still counts; one put back on a later dispatcher pass is not seen. `double_click`'s selection is read back too. A
> Call Stack row is named as it reads, `Caller, line 12`, where its name was the row record's text, which carried the
> debug session's id and stop generation and so went stale at every stop; and each row is its own, so a recursive call's
> deeper row can be selected. Not reproduced: with the fixture adapter, a select on a caller row by its path moves the
> debugger, and a row clipped out of view has no container, so no path reaches it. The closest match found is the
> declined row, which needs a select made while the session has moved on. Guarded by `SelectVerifiedTests`.

## `type_text` writes beside a standing selection instead of over it — **CLOSED** (#1040, 2026-10-20)

**Symptom.** Found by the review of #273 task 4.5, which reproduced it. The calls below are #1040's reproduction,
read in code and not run live. In a module with no `Attribute` lines, with `E` the code editor's path from
`dump_visual_tree` and the caret after `Debug.Print 1` on the line `    Debug.Print 1, 2`:

```
press_key {"target":E,"key":"End","modifiers":"Shift"}      selects ", 2"
type_text {"target":E,"text":", 3"}
→ success; the line now reads "    Debug.Print 1, 2, 3"
```

A person's typing replaces the selection. `UiAutomationDriver.TypeText` inserts at the caret on every surface (a
`TextEditor`, a `TextArea`, and a `TextBox` at its `CaretIndex`), never reads the selection, and, away from a
described line's end, replies only with how many characters it typed. Its description says it inserts "at the
caret", so it does what it says; what it does not do is what a person's typing does. `press_key` already judges a
key with a selection standing as a write over it, so the two tools disagree. The code window's own Enter has the
same shape, which is a product defect in the same issue.

**Workaround.** Delete the selection first with `press_key {"target":E,"key":"Delete"}`, then call `type_text`.

**Suggested fix.** Write over the selection, as a person's typing does and as `press_key` already assumes, and say
in the reply how much was replaced. [#1040](https://github.com/hexide-io/HexIDE/issues/1040).

> **Fixed.** `type_text` writes over a standing selection, as a person's typing does, as one change, and says so:
> "typed 3 char(s) into TextEditor, replacing the 3 selected char(s)". The selection is the span judged, as `press_key`
> judges a key over one, so a selection reaching the header or a member's Attribute lines is refused, with a reply that
> names how far it reaches rather than where it begins. That is stricter than a typed key, which writes over what the
> read-only sections let it; aligning the two is [#1055](https://github.com/hexide-io/HexIDE/issues/1055). At the end of
> a described declaration the text is placed past its run with the selection it replaces in hand, as Shift+Enter places
> a line break, and no selection is left on what replaced it. Typing is not a deletion, so a selected declaration's run
> is not taken with it. Over a box selection the text is written on each of the box's lines, as typing there writes it,
> and nothing outside the box is taken. A TextBox's selection is replaced in one assignment. The code window's own Enter
> had the same shape and now replaces the selection too: a selected declaration's text as Shift+Enter does, the tail of
> a declaration's line as Enter at its end does, re-cased and closed, and a box's text before the line break. Writing
> over a box whose lines a folded section starts on, such as a declaration's folded Attribute run, hung the IDE in
> AvaloniaEdit's height tree, for a person's key as for these; such folds are opened for the write and folded again.
> Ctrl+V was missed, and still pasted over the box with the library's own replacement until
> [#1251](https://github.com/hexide-io/HexIDE/issues/1251) routed it the same way. A plain Enter still writes a bare
> line feed into a CRLF file, which is [#530](https://github.com/hexide-io/HexIDE/issues/530). Guarded by
> `MemberRunFollowsItsLineTests`, whose typing and Ctrl+V cases over a folded declaration each hang with their guard
> removed. Until #1251 the typing case wrote `()`, which does not hang, so removing the guard turned nothing red.

## No tool can close a window as a person does — **CLOSED** (#829, 2026-10-20)

**Symptom.** Verifying #820, a program's Yes/No message box had to be closed the way a person closes it, to show
that it refuses. Nothing on the surface does that:

```
dump_visual_tree {}
→ {"window":"YesNoBox","root":{"path":"Window", ... "children":[{"path":"Window/Custom", ... Button[Yes], Button[No]}]}}
interact {"target":"Window","action":"close"}      no such action
press_key {"target":"Window/Custom/Button[No]","key":"F4","modifiers":"Alt"}
```

The title bar is the platform's chrome, outside the control tree, and Alt+F4 is handled by Windows before
Avalonia raises a key, so a key event on a control closes nothing. `stop_project` and `shutdown_ide` close a dialog
programmatically, which is the close a guard deliberately lets through, so they cannot stand in for a person's.

**Workaround.** From PowerShell, find the window by title with `FindWindow` (passing `[NullString]::Value` as the
class: PowerShell turns `$null` into `""`, which matches nothing), check with `GetWindowThreadProcessId` that it
belongs to the IDE's process, and post `WM_SYSCOMMAND` with `SC_CLOSE`, which is what the title-bar button sends.
Windows only, and outside the surface a caller has.

**Suggested fix.** An `interact` action, `close`, on a top-level window, raising the platform's close for a person
(`WindowCloseReason.WindowClosing`, not programmatic), and replying whether the window is still open.
[#829](https://github.com/hexide-io/HexIDE/issues/829).

> **Fixed.** `interact` has a `close` action, which closes the target's window, or the window holding the target, as its
> close box does: through the window's own handling of its platform's close, a close no program asked for, which a
> Yes/No or Abort/Retry/Ignore MsgBox refuses and a form's QueryUnload (UnloadMode 0, vbFormControlMenu) may refuse.
> The reply says what became of the window, read once the program has run what the close set off, since a form's
> close box closes it only once the program
> has unloaded it: closed; hidden, not closed, when QueryUnload hid it; still open while the program decides, when it is
> still running (a QueryUnload asking a MsgBox); or still open because it refused. A window with a modal dialog open over
> it is refused, as its close box does nothing for a person until the dialog is answered. The IDE's main window is
> refused, since closing it takes this server down with it; `shutdown_ide` closes the IDE. The handling is internal to
> Avalonia and is reached by reflection, so an Avalonia that hides it fails `CloseWindowTests` rather than closing from
> code. Guarded by `CloseWindowTests` and `ProgramWaitTests`.

## No tool types as a person does, so anything driven by TextInput is out of reach — **CLOSED** (#943, 2026-10-20)

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
type_text {"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/Pane[#0]/Custom[Root]/None[TextEditor]","text":"Ms"}
→ didChange seq 151, and no completion request
```

A person typing `M` triggers one. `type_text` inserts through the document API by design (#649), and `press_key`
raises only KeyDown and KeyUp, which AvaloniaEdit does not turn into text. Nothing on the surface raises
TextInput, so these are out of reach: the list that opens as you type, parameter info on `(` and `,` and its
closing on `)`, the list closing on a character that cannot be part of a name, and the edit-while-running reset
prompt for a printable character. Both tools do what they say; this is a missing capability, as #829's was.
Cross-platform by code reading.

**Workaround.** For completion and parameter info, the explicit commands: `press_key` with Ctrl+Space, Ctrl+J or
Ctrl+Shift+I on the TextArea. A list opened that way cannot be seen either (next entry). There is no workaround
for the typed-character paths.

**Suggested fix.** A keyboard typing mode, a `type_keys` tool or `mode:"keyboard"` on `type_text`, that raises
KeyDown, TextInput and KeyUp per character on the focused TextArea and reports mechanism "keyboard".
[#943](https://github.com/hexide-io/HexIDE/issues/943).

> **Fixed.** `type_text` takes `mode:"keyboard"`, which types as a person does, a key at a time: KeyDown, TextInput and
> KeyUp per character, with a line break as Enter and a tab as Tab. Each key goes where the keyboard sends it, to the
> focused control, with key bindings tried first as Avalonia's keyboard device tries them, so a Tab that moves focus
> sends the rest to where it went and an Enter bound to a command runs it; `press_key` now delivers its key the same
> way. After each key the code window's completion and parameter-info requests are waited for, up to 2 s, so the list a
> letter asks for is open before the next key, as a person sees it before typing on; a slower server is named in the
> reply. So what typing drives runs: the completion request on a letter, parameter info on `(` and `,`, auto-indent and
> the block a line break closes. The typing stops, and the reply says after how many keys, at a key the header or a
> member's Attribute lines refuse, when a dialog opens over the window, when focus leaves it, or on cancellation. The
> default `document` mode is unchanged. Guarded by `TypeKeysTests`.

## No tool reads back a control property beyond eight, though `set_control_property` sends callers to `get_form_controls` for them

**Symptom.** (Linux, openSUSE Leap 16, Debug build of main at b15c7cf, 2026-10-13.)

```
get_form_controls {"formName":"Form1"}
→ Label0: name, type VB.Label, left 120, top 10, width 100, height 20, caption "Centred &label", visible, enabled
get_file_content {"name":"Form1"}
→ the code section only, not the form's property block
```

No Alignment, AutoSize, WordWrap, BorderStyle, UseMnemonic or Font, so none of them can be read back after
`set_control_property` sets them; the label's Alignment had to be inferred from its caption. `get_locals {}` while
paused listed Label0 without them too. `get_form_controls` describes itself as returning "key design-time
properties", which is accurate; what is not is `set_control_property`'s "Use get_form_controls to see available
controls and properties".

**Since #273 task 3.18 the `get_file_content` line reads differently, and the gap is narrower.** That run was on
main, where `get_file_content` returned a form's code alone. With #273 it returns the form's whole file, its
designer block included, and the block follows the designer: #273's live check on 2026-10-13 saw
`move_control {"formName":"Dialog","controlName":"OKButton","left":300}` reach the open code window's header as
`Left = 4500`, and after a save, which `set_control_property` makes, the block is the one written. So a property
the `.frm` records can now be read back, in the file's own spelling: twips for a position, a Font as its
`BeginProperty Font` block, a `Byte[]` property as a reference into the `.frx`. That is not the Properties
window's text, which is what `set_control_property` takes, and it is only what the file records.

**Workaround.** With #273, read the control's `Begin` block in `get_file_content {"name":"Form1"}`, as the file
spells it. For the Properties window's own spelling, not exercised in that run: select the control in the designer
and read the Properties window's rows with `dump_visual_tree` and `interactiveOnly: false`, or `inspect_element` on
a row.

**Suggested fix.** An optional `controlName` on `get_form_controls` that returns every property the Properties
window shows for that control, under its name there and in the text form `set_control_property` accepts, and a
corrected `set_control_property` description. [#923](https://github.com/hexide-io/HexIDE/issues/923).

> **Fixed.** `get_form_controls` takes `controlName`, a control's name, the form's own, or a control array's element
> as `name(index)`. The reply lists that control alone, with `properties`: every property the Properties window
> shows for it, by the name there, with its value as text (an enum as `1 - Opaque`, a colour as its literal, a
> number with `.` in any culture; a font as its face, size, weight and style, a picture as its size or `(None)`)
> and `settable`, whether `set_control_property` takes that text back. Every row marked settable, on every built-in
> control, the Line and the Menu, sets the value it shows; a font and a picture are set through their dialogs.
> What the window's rows alone would hide is in the reply too, each not settable and with a `note`: a value the
> file holds that HexIDE could not read (a palette colour, #786), given as the file's text rather than the default
> the window shows; a property HexIDE does not model (a TextBox's MultiLine, a control array's Index), as a row of
> its own; and a Timer's Width, Height and Visible, which the file does not keep. A control array named without an
> index answers with its first element, and the reply's `note` says so. A Menu no longer has Left, Top, Width or
> Height, which a caller could set and the save wrote under names VB6 gives no menu. `set_control_property`'s
> description now sends callers to `controlName`. Guarded by `PropertyTextTests`.

## The bookmark tools count lines from 0; every other line-taking tool, and the gutter, count from 1

**Symptom.** `set_bookmarks {"name":"Module1","lines":[1]}` (`project` left at null) answers `Carried/Module1
now has bookmarks on 1.`, and the bookmark is drawn on gutter line **2**. `get_breakpoints`,
`set_breakpoints`, `run_to_cursor`, `set_next_statement`, `get_debug_state` and `get_call_stack` all number
lines from 1, and so does the gutter. Both bookmark descriptions do say "0-based". A caller who has just used
any other line tool, or read a line number off a snapshot, is still one line out.

**Workaround.** Subtract one before calling `set_bookmarks`, and add one to what `get_bookmarks` returns.

**Since #273 both numberings count from the top of the file, header included** (task 3.12), as every line tool
now counts, and a line of the header or of a member's Attribute lines is refused, with where the code starts given
in the tool's own numbering. So being one out near the top is sometimes refused rather than placed. Measured in
task 3.16's live check (2026-10-01), on a class added through the IDE whose header is lines 1 to 13:
`set_breakpoints` with `[13]` was refused with "its code starts at 14", and `set_bookmarks` with `[13]` was
accepted, which counting from 0 is the gutter's line 14. One number, refused by one tool and accepted by the
other. Below the header the difference is as silent as it was.

**Suggested fix.** Undecided, and filed as needs-decision:
[#571](https://github.com/hexide-io/HexIDE/issues/571). Either convert at the tool boundary, which changes
the contract, or keep 0-based and have the descriptions say the gutter shows N+1.

> **Fixed.** Decided and done under [#571](https://github.com/hexide-io/HexIDE/issues/571): the bookmark tools count
> from 1, as the gutter and every other line tool do. `get_bookmarks` returns, and `set_bookmarks` takes and
> refuses, lines numbered 1..N from the top of the file, so one number means one line on every tool. The store
> and the `.user.hexproj` it writes still count bookmarks from 0; `MarkLineRules.BookmarkRequest` judges a request
> and converts it, the held set included, so a bookmark read off a header line can be written back. Both
> descriptions say 1-based, `get_file_content` no longer says the bookmark tools count from 0, and the reply's note
> says "counting from 1 at the top of the file", which reaches a caller still holding an old description. Guarded
> by `MarkLineRulesTests` and `LineNumberingDescriptionTests`, which refuses any tool description saying lines are
> counted from 0, in any of its usual spellings, and checks both tool bodies still convert.

## `take_snapshot` and `dump_visual_tree` cannot see the code editor's completion list or parameter-info popup — **CLOSED** (#942, 2026-10-20)

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
press_key {"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/Pane[#0]/Custom[Root]/None[TextEditor]","key":"Space","modifiers":"Ctrl"}
take_snapshot {}                     → the code window, with no list
dump_visual_tree {"window":"auto"}   → MainWindow only
```

The run concluded that no list opens, and that was wrong: a Tab sent straight after was swallowed with no
didChange, which needs an open list, and a headless reproduction confirms the list is open there (#941). Both
tools answer the same whether or not a list is open. AvaloniaEdit's completion and insight windows are Popups
parented only logically to the TopLevel, and the routes the tools use find visual-child popups plus open
ContextMenus and flyouts. Cross-platform by code reading.

**Workaround.** Do not read an empty snapshot as "no list". Infer the list from its effect: `press_key` Down then
Tab, then read `get_file_content`, or the next `textDocument/didChange` in `list_lsp_messages`. Until #941 is
fixed, Tab appends the item instead of replacing the prefix.

**Suggested fix.** Expose CodeEditorView's completion and insight windows through an internal accessor, and return
the open one from `AttachedPopupOf` for a CodeEditorView owner, so the snapshot and the tree both reach it.
[#942](https://github.com/hexide-io/HexIDE/issues/942).

> **Fixed.** The code window's open completion list and parameter info are the editor's attached popups:
> `dump_visual_tree` and `find` list them under the `TextEditor` that opened them, both at once when typing an
> argument has opened a list under the call's parameter info, and `take_snapshot` composes them into the image, by
> the same route that reaches a context menu or a flyout. A list's items are named by their text, so a path reads
> `ListItem[MsgBox]` and `interact` `select` with a value picks one by name. A closed list is not reported. Guarded
> by `CodeEditorPopupTests`. The snapshot's composition follows from that route and is not exercised headless, where
> a popup is drawn in the window's own overlay; there the walk also meets each popup a second time under `Window`,
> as it does a context menu. Still out of reach: the description tooltip beside a list item, and committing an item
> by `double_click`, which the list takes on a press; select it, then `press_key` Tab or Enter, which is #941's.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools,
> after keyboard `type_text` of `Private Sub Probe()` and a line break into Form1's code (auto-indent and the automatic
> `End Sub` followed), `open_file` at line 19, column 5, and keyboard `type_text` of `MsgBox(`:
>
> - `take_snapshot {}` showed the parameter info beside the caret: `MsgBox([Prompt], [Buttons As VbMsgBoxStyle =
>   vbOKOnly], [Title], [HelpFile], [Context]) As VbMsgBoxResult`.
> - **The signature text needs `interactiveOnly: false`.** Rooted at the `TextEditor`, that dump lists it as
>   `Text[#1]` and `Text[Prompt]`. The default lists only the popup's hidden overload arrows, `Button[PART_UP]` and
>   `Button[PART_DOWN]`.
> - After `press_key` Ctrl+Space, `take_snapshot {}` showed the list at the caret, with the parameter info over its top
>   rows. The default dump rooted at the `TextEditor` listed its rows as
>   `…/None[TextEditor]/List[PART_ListBox]/Pane[PART_ScrollViewer]/ListItem[Dim]` and on, each with `selectionItem`.
> - `interact` `select` with value `MsgBox` on `List[PART_ListBox]` replied "selected 'MsgBox' (it was scrolled out
>   of view, so the list was scrolled to it first)", and inserted nothing.
> - **Not met through the client: the default whole-window dump.** `dump_visual_tree {}` was 75,575 to 83,944
>   characters each time, which Claude Code would not take inline and saved to a file, so the dumps above were rooted
>   at the `TextEditor`: [#1982](https://github.com/hexide-io/HexIDE/issues/1982).

## `set_control_property` cannot set Font, and its refusal implies a text form that does not exist

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
set_control_property {"formName":"Form1","controlName":"Label0","property":"Font","value":"DejaVu Sans"}
→ {"success":false,"error":"Cannot set 'Font' to 'DejaVu Sans': it takes a value the Properties window accepts for a VBFont"}
```

The Properties window's Font row read "DejaVu Sans", the string refused, and that row is read-only beside a "..."
button, so the set of values it accepts is empty. The description promises that a refused value's reply lists
what the property takes, and here it does not. By code reading every `byte[]` property (Picture, Icon,
MouseIcon, DragIcon, OleObjectBlob, List, ItemData) falls through the same way, refused as "...accepts for a
Byte[]". Cross-platform by code reading.

**Workaround.** For Font, the Properties window: select the control, `interact` `invoke` on the Font row's
`PART_OpenFontWindowButton`, then drive the Font dialog, which the run did successfully. For the `byte[]`
properties none is known: the grid gives them only a generic text box, and what it does with a typed value was
not checked.

**Suggested fix.** Accept Font as the grid shows it (a family, optionally a size and style words), merged with the
current font so what is not given is kept. Never let a refusal promise a text form the parser refuses: name the
Font dialog for Font, and say that no automation route exists yet for the `byte[]` properties.
[#952](https://github.com/hexide-io/HexIDE/issues/952).

> **Fixed.** `set_control_property` sets Font from the text `get_form_controls` shows for it: parts separated by
> commas, the face first, such as `Tahoma, 9.75 pt, bold, italic, charset 0`, with `regular`, `underline`,
> `strikethrough` and `weight N` too, each at most once. A part not given keeps the current font's, so `12 pt`
> changes the size alone; a new face without a charset takes charset 0, as the Font dialog gives it, so `DejaVu Sans`,
> as the run sent it, is that face in the ANSI charset; and when any style word is given, the words given are the
> whole style, so `regular` clears it. The text `get_form_controls` shows names every part, so it sets another
> control to exactly that font, and its row is `settable` only when it does. Anything that does not say one font is
> refused rather than read as some font: a part given twice (`9,75 pt` is two sizes), a size or a style mistyped as
> the face (`Arial 12`, `bold italic`), a size past VB6's 2160 points or finer than four decimals, a face longer than
> 31 characters or holding a quote. The reply's note gives the whole font, not its face. A refusal names this form
> for a Font, and for a `byte[]` property says it has no text form and no automation route yet. A control with no
> font of its own merges with HexIDE's default, which the designer draws it in, not its form's: HexIDE has no ambient
> font. Guarded by `PropertyTextTests`.
>
> **Two corrections, later.** A new face's charset of 0 was wrong for a symbol or OEM face: `Wingdings` saved
> Charset 0, where VB6 gives 2, and the control drew letters. A new face given without a charset now takes the one
> VB6's FontName gives it, and a face equal to the current one but for case keeps its charset
> ([#1310](https://github.com/hexide-io/HexIDE/issues/1310)). And the guess against a mistyped size or style also
> refused real faces, `Wingdings 2` and `MS Shell Dlg 2` among them, so a control in one showed a font that could not
> be set back. The control's own face, and one FontName takes here, are now faces whatever their names end in, and a
> refusal of the guess says why ([#1311](https://github.com/hexide-io/HexIDE/issues/1311)).
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools,
> with two Labels added to a new project's Form1:
>
> ```
> set_control_property {"formName":"Form1","controlName":"Label1","property":"Font","value":"Tahoma, 12 pt, bold"}
> → Label1.Font is now Tahoma, 12 pt, bold, charset 0, and the file is saved.
> get_form_controls {"formName":"Form1","controlName":"Label1"}
> → {"name":"Font","value":"Tahoma, 12 pt, bold, charset 0","settable":true}
> set_control_property {… "controlName":"Label2","property":"Font","value":"Tahoma, 12 pt, bold, charset 0"}
> → Label2.Font is now Tahoma, 12 pt, bold, charset 0, and the file is saved.
> ```
>
> `take_snapshot` drew Label1 in it: the ink of its text is 76 pixels wide at 150% scaling, where Tahoma's ink for
> `Label1` at that size is 65 regular and 76 bold. The saved `Form1.frm` gives both Labels `Name = "Tahoma"`, `Size = 12`, `Charset = 0` and
> `Weight = 700`.

## Polling `list_lsp_messages` with the highest sequence seen never shows a waiting request answered

**Symptom.** While re-recording [`mcp-first-contact.md`](../mcp-first-contact.md) for #273 on 2026-10-13, in a fresh
`--newproject` session, a request seen waiting never came back answered when polled as the description advises:

```
open_file {"name":"Form1"}
list_lsp_messages {}
→ six rows; sequence 4 is initialize, with no outcome; 6 is the highest
list_lsp_messages {"afterSequence":6,"limit":3}
→ rows 12, 13, 15 ("matched":7); initialize is not among them
list_lsp_messages {"afterSequence":3}
→ row 4 comes back "outcome":"Answered", "answerSequence":8
```

The description says to pass "the highest one already seen to poll for what is new". A reply does not add a row:
it completes its request's row and uses up a sequence. That row's sequence is below the one a poller passes, so
the filter (`CaptureQueries`, `envelope.Sequence <= after`) drops it. The poller sees what followed the request
and never sees the request answered.

**Workaround.** Pass a sequence below the oldest request still waiting, and read each request's row again.

**Suggested fix.** Match a row that was completed after `afterSequence` as well as one recorded after it, keyed
on the sequence its reply consumed, which exists whether or not the body was kept. Say in the description that
a completed request comes back on its own row. [#961](https://github.com/hexide-io/HexIDE/issues/961).

> **Fixed.** A request recorded before `afterSequence` and answered, cancelled or abandoned after it now comes back,
> on its own row with its outcome and the `completedSequence` that outcome was recorded under: the sequence its
> reply used up, kept or not, or one an ending takes. The reply carries `latestSequence`, the cursor to pass next,
> rather than leaving the poller to take the highest row seen, which for a request answered since is older than its
> answer and would return it again on every poll. The cursor is the pump's watermark at the drain, and the listing
> is read as it stood there: the snapshot reads one ring at a time while the pump carries on, so a cursor taken from
> what was read skipped rows another connection recorded meanwhile, hundreds in two hundred thousand. A poll cut
> short by `limit` keeps the earliest changes, and its cursor continues from them, so following it pages through
> without loss; a listing that is not a poll still keeps the newest rows. A cursor beyond anything recorded, from an
> earlier run of the IDE, lists this run from its start and says why. `get_lsp_message` on a sequence a request was
> answered or ended under says so rather than calling it missing. Both descriptions and the walkthrough in
> `mcp-first-contact.md` say to poll from `latestSequence`. Guarded by `CaptureQueriesTests`, including a
> three-connection stress poll that the old cursor fails. That an outcome recorded after the watermark is held back
> to the next poll, rather than shown now and again then, is by reading.

## A write tool that needed no picker leaves an armed file-dialog answer pending, and says nothing

**Symptom.** On the project `--newproject` creates, Project > Add Form adds `Form2` with a file already, because
that project lives in a temp folder:

```
answer_next_file_dialog {}
add_control {"formName":"Form2","type":"CommandButton","x":20,"y":20,"width":81,"height":33}
→ {"success":true,"controlName":"Command0","note":"Command0 is at Left 20, Top 20, Width 81, Height 33, in pixels."}
clear_file_dialog_answers {}
→ {"success":true,"pending":0,"note":"Discarded 1 armed answer(s); the next file dialog will be shown for real."}
```

The save wrote the form's file and never opened a picker, so the cancel armed for it is still pending. The next
dialog to open takes it, however much later and whatever it is for. Only `clear_file_dialog_answers` showed it.

**Workaround.** After any write that was armed for, call `clear_file_dialog_answers`. A count above zero means
the write did not use the answer.

**Suggested fix.** When a write tool finishes with an answer armed before the call still pending, say so in its
note, naming why (the document already had a file) and that `clear_file_dialog_answers` discards it.
[#810](https://github.com/hexide-io/HexIDE/issues/810).

> **Fixed.** `set_file_content`, `set_control_property` and `add_control` say so when answers armed before the call
> are still pending after their save and it took none: in the note on success, and in the error when the save
> failed. The sentence states what happened, not what the answer was for, since a caller may have armed it for a
> later step: how many are pending, that the save opened no picker because the document already has a file, or
> stopped before its picker opened, that the next dialogs will take them, and that `clear_file_dialog_answers`
> discards them if they were armed for this save. Answers are numbered as they are armed, so one armed while the
> save ran is not put down to it. A refusal before the save, such as a name not found, leaves the answer for the
> retry and says nothing. Not covered: a save reached through a menu, by `invoke_menu_item` or `interact`, which
> does not wait for the save to finish; `answer_next_file_dialog`'s description says so and points to
> `clear_file_dialog_answers`. For a document without a file, an answer a dialog elsewhere takes while the save runs,
> as a running program's can, reads as the save's. Guarded by `ScriptedFileDialogsTests`, which also checks that
> every reply a saving tool builds after its save goes through the sentence.

## `take_snapshot` returns only a path on the IDE's machine, so a client attached from elsewhere cannot see the image

**Symptom.** Driving a Debug build of b15c7cf in an openSUSE Leap 16 VM from a Windows host, through an SSH tunnel
to the VM's MCP port (2026-10-13):

```
take_snapshot {}
→ {"path":"/tmp/hexide_snapshot_20261013-004254-281_1.png","width":1920,"height":1011,"scale":1}
```

The path is in the VM's `/tmp`, which the client cannot open. The description says the tool "returns the file
path so the caller can read the image", which holds only for a caller on the same machine. The server answers
loopback only, so a tunnel is how another machine reaches it. Not Linux-specific: any remote attach has it.

**Workaround.** Copy the file across by hand; every snapshot in that run was fetched with `scp`.

**Suggested fix.** An optional `inline` parameter, default false, that returns the image as MCP image content
(`image/png`) beside the same JSON. Keep path-only as the default: an inline image costs every local caller that
does not need it. [#957](https://github.com/hexide-io/HexIDE/issues/957).

> **Fixed.** `take_snapshot` takes `inline`, default false. Passed true, the reply carries the image itself as MCP
> image content (`image/png`) after its JSON, which keeps its shape and its path. The copy is encoded by the tool from
> the image in hand and attached by a call-tool filter, as `OpenDialogNote`'s note is, so nothing is read back from a
> file another local user could swap. It is fitted to 1568 pixels on its longest side, since a model sees an image at
> about that size, a request holding many images refuses one over 2000 pixels, and a 4K PNG is several times the bytes;
> `inlineWidth`, `inlineHeight` and `inlineScale` describe it, so `boundingRect × inlineScale` finds a control in it.
> Path-only stays the default: an image costs a few thousand tokens. Measured on Windows through the server's HTTP
> endpoint: a 3840×2054 window came back as a 1568×838 PNG of 75 KB, and a call without `inline` has no image block.
> Guarded by `InlineSnapshotTests`. Run through Claude Code since, over a tunnel and locally (#1330, below).
>
> **Live (#1330, 2026-10-25), over a tunnel.** From a Claude Code session on Windows, through an SSH tunnel to a
> Debug build of main at beada72 on an openSUSE Leap 16 VM:
>
> - `take_snapshot {"inline": true}` returned its JSON, with `inlineWidth` 1440, `inlineHeight` 756 and
>   `inlineScale` 1, and one `image/png` block, which the client showed.
> - That block, read from the client's own stream, is a PNG of 1440×756.
> - `take_snapshot {}` returned the JSON alone, with no `inline*` fields and no image.
>
> **Live (#1330, 2026-10-25), on the IDE's machine.** From a Claude Code session on Windows, against a Debug build of
> main at beada72 on the same machine:
>
> - A 1418×671 window: `take_snapshot {"inline": true}` returned `inlineWidth` 1418, `inlineHeight` 671 and
>   `inlineScale` 1.5, and one `image/png` block the client showed. The block is a PNG of 1418×671.
> - A 1920×974 window, in another session where the call was sent as `{"inline":"true"}` and the client turned it into
>   the boolean: it returned `inlineWidth` 1568, `inlineHeight` 795 and `inlineScale` 1.225, fitted to 1568 on the
>   longest side as above, followed by an image block. That run did not measure the block.
> - `take_snapshot {}` again had no image.

## `list_lsp_messages` `failuresOnly` omits a message that could not be serialized

**Symptom.** With a serialization failure at seq 183 in the record (#944) (Linux, openSUSE Leap 16 under XWayland,
main at b15c7cf, 2026-10-13):

```
list_lsp_messages {"failuresOnly":true,"limit":20}
→ {"messages":[],"matched":0,"note":"118 envelope(s) are recorded, and none match this filter. Drop the filters to see what is there."}
```

The record held:

```
{"sequence":183,"direction":"Local","kind":"NeverSent","method":"$/cancelRequest","detail":"could not be serialized: ..."}
```

The Protocol Inspector's Failures only filter and its failure count miss it the same
way: both count only error responses and the Failed, Cancelled and Abandoned outcomes. So a filter for finding
failures hides the client-side defect the capture was built to expose. Not every NeverSent row is a failure: a
request withheld because the server did not advertise the capability is recorded as a Sent NeverSent row,
deliberately. Cross-platform by code reading.

**Workaround.** Call `list_lsp_messages {}` with no filter and look for `kind:"NeverSent"` with
`direction:"Local"`.

**Suggested fix.** Count a Local NeverSent row as a failure in `CaptureQueries.IsFailure` and
`ProtocolInspectorRowViewModel.IsFailure`, and say so in `failuresOnly`'s description.
[#945](https://github.com/hexide-io/HexIDE/issues/945).

> **Fixed.** A failure is now one rule, `ConversationFailures.IsFailure`, read by both `list_lsp_messages`
> `failuresOnly` and the Protocol Inspector's Failures only filter and count, so they cannot disagree again. Besides
> error responses and the Failed, Cancelled and Abandoned outcomes, it counts an envelope its recorder marked
> `Fault`. The marked cases are:
>
> - a message this client could not serialize;
> - a frame it could not decode, though not the note saying a frame was repaired;
> - a connection it could not make;
> - a server that disconnected while the client was not stopping it;
> - a process that exited non-zero, unless this client was stopping it;
> - a debug adapter that could not start, or sent a frame this client could not read.
>
> The review found that keying the rule on direction was fragile: a withheld-capability row is filed as Local in two
> test fixtures and as Sent in production. It also found the same blind spot for undecodable frames and crashes. So
> the flag is set where each fault is known, and the row's direction and wording decide nothing.
>
> A row now carries `fault: true`, and so does an export's envelope. The inspector draws a failed local row in full
> rather than faint. `failuresOnly`'s description and the protocol-inspector spec list what counts. A debug adapter's
> connection that closed on an error is not marked, because a deliberate stop can close it the same way. Guarded by `ConversationFailuresTests`,
> `TheRecordSeesTheProcessTests` (each fault site through a real client, including stopping), `CaptureQueriesTests`,
> `ProtocolInspectorToolViewModelTests`, `ConversationExportTests`, `DapCaptureTests` and the process-notice tests.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools.
> The setup: a session profile whose `lsp-servers.json` adds a `.txt` server whose `command` does not exist, and a
> project carrying `notes.txt`.
>
> ```
> open_file {"name":"notes.txt"}
> list_lsp_messages {"failuresOnly":true}
> → {"messages":[{"sequence":2,"connectionId":"broken-txt",…,"direction":"Local","kind":"Lifecycle",
>    "detail":"could not connect: could not start 'C:/nonexistent/no-such-server.exe' … The system cannot find the
>    file specified.","hasBody":false,"fault":true}],"matched":1,…}
> ```
>
> - **Without the filter**, the list adds sequence 1, `connecting`, with no `fault`.
> - **In Tools ▸ Protocol Inspector**, `interact` `toggle` on `CheckBox[Failures only]` replied "it is now on". The
>   counts read "1 shown" and "1 failed".
> - **`take_snapshot`** drew the row in full: its time, `•` and `broken-txt` in normal text, and the failure in bold
>   red in the Method column.
> - **The filtered dump** also lists a recycled grid row holding the same failure, marked `isHidden`:
>   [#1977](https://github.com/hexide-io/HexIDE/issues/1977). Fixed by excluding recycled rows in the
>   DataGrid rows presenter, including their stale cells. A headless regression filters two rows to one
>   and checks that the remaining row appears once and its path resolves. Live recheck: repeat the
>   filter and dump above; the one shown failure should have one DataItem, with no recycled duplicate.
>
> **Corrected later.** Two exits stayed out of the rule ([#1314](https://github.com/hexide-io/HexIDE/issues/1314)).
> A language server's non-zero exit while this client stopped it was excused as a kill, but no transport reports a kill
> it makes, so the excuse hid only the server's own exit code, which LSP makes its account of the stop. And a debug
> adapter's exit was never marked, though the IDE tells the user of a non-zero code, a kill and a failed cleanup.
> Both now count, by the rule the IDE tells the user by.

## `get_last_runtime_error` stamps local time, where every other timestamp on the surface is UTC

**Symptom.** On a machine at +01:00 (Linux, openSUSE Leap 16, Debug build of main at b15c7cf, 2026-10-13):

```
get_last_runtime_error {}
→ "at":"2026-10-13T03:05:25.1331302+01:00"
/health
→ "timestamp":"2026-10-13T04:07:50.0894348+00:00"
```

`list_lsp_messages`, the conversation export, the connection list and `/health` all stamp UTC, so lining an error
up against the LSP record means converting offsets by hand. Invisible on a machine whose zone is UTC, CI
included. Cross-platform by code reading, and the archived #342 entry quotes the same `+01:00`.

**Workaround.** Compare the stamps as instants, not as text: each carries its offset, so the comparison still
holds. This is an inconsistency, not wrong data.

**Suggested fix.** `DateTimeOffset.UtcNow` in `RuntimeErrorLog`, and a test asserting a zero offset.
[#956](https://github.com/hexide-io/HexIDE/issues/956).

> **Fixed.** `RuntimeErrorLog` stamps an error in UTC, from a `TimeProvider`, so `get_last_runtime_error`'s `at`
> carries `+00:00` as every other stamp on the surface does, and the tool's description says so. The review found two
> more local times a caller meets beside it. `take_snapshot`'s file names are now UTC, with a `Z`
> (`hexide_snapshot_20261012-093015-123Z_1.png`). The IDE log, which a tool failure points to, now writes each
> line's offset. Guarded by `RuntimeErrorLogTests`, whose clock is UTC, so a local reading fails on any machine
> that is not set to UTC, and by `SnapshotCapturePathTests`.

## `invoke_command` reaches only an `ICommand` property, and refuses a method by saying there is no such property

**Symptom.** Recorded on 2026-10-12 under the entry now archived as "A context-menu action bound to a method cannot be
driven", and unchanged at 4789f9a by code reading. With Form1 selected in the Project Explorer:

```
interact {"target":".../Pane[Project Group - Group1]/Custom","action":"invoke_command","value":"DeleteForm"}
→ {"success":false,"mechanism":"reflection","error":"no public property 'DeleteForm' on ProjectToolViewModel"}
```

`DeleteForm` is a public method of `ProjectToolViewModel`, which the Project Explorer's flyout binds as
`Command="{CompiledBinding DeleteForm}"`. `invoke_command` looks only for a public property holding an `ICommand`
(`UiAutomationDriver.ReflectInvokeCommand`), and `inspect_element` lists only commands and properties, so the method
is not listed either. The refusal names a property, which reads as a misspelt name rather than as "a method cannot
be reached this way".

**Workaround.** Drive the control the method is bound to, as a person does. For Remove: `select` the form's node,
`expand` the Project Explorer's tree to open its flyout, then `invoke` **Remove Form1** (measured with #947).

**Suggested fix.** `invoke_command` falling back to a public parameterless method, listed by `inspect_element`, and
a refusal that names what it looked for. [#836](https://github.com/hexide-io/HexIDE/issues/836).

> **Fixed.** `invoke_command` reaches a method a Command is bound to as well as an `ICommand` property. Which methods
> are bound comes from the XAML compiler's own record: Avalonia compiles each `Command="{CompiledBinding X}"` on a
> method into a trampoline named for the type, the method and its arity, and `XamlCommandBindings` reads those. So
> `invoke_command` calls, and `inspect_element` lists as `kind: "method"`, exactly the methods a person can click.
>
> The first version accepted any public parameterless method. Its review found that this reached about thirty more
> that no UI exposes, such as `OnInitialized` and every Options page's `SaveToSettings`. It also reached a
> `MakeProject` that skips the guard on the `MakeProjectCommand` the menu binds.
>
> As Avalonia does, the call is refused when a `Can<name>(object)` method of any visibility returns false. A reply
> with no such guard says so, so the caller checks the effect. The refusals and failures name their cause:
>
> - a method bound with a CommandParameter, which the caller cannot supply;
> - a method no Command is bound to;
> - a name that is neither;
> - an exception from the method or its Can method;
> - a faulted or cancelled task.
>
> A task still running is logged if it fails later. A designer context menu that binds through another control's
> DataContext is still not reached from the menu item, as with a command property.
>
> Measured on Windows through the server's HTTP endpoint:
>
> - The Project Explorer lists six methods, and the main window lists 32 of its 91 members.
> - `invoke_command DeleteForm`, with Form1 selected, removed the form.
> - `OnInitialized` was refused as unbound.
>
> Guarded by `InvokeCommandMethodTests`, and by `XamlCommandBindingsTests`, which read HexIDE's own assembly so an
> Avalonia change to the names fails there.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools, on
> a new project.
>
> - **`inspect_element` on the Project Explorer.** On `…/Pane[Project Group - Group1]/Custom`, its `ProjectToolView`,
>   it listed six methods, `DeleteForm` among them with `kind: "method"`. The `Tree` under it inherits that
>   DataContext and lists no members of its own.
> - **`invoke_command` `OnInitialized` on that node** replied "no ICommand property or Command-bound method
>   'OnInitialized' on ProjectToolViewModel; …".
> - **`invoke_command` `DeleteForm`, with `TreeItem[Form1 (Form1.frm)]` selected,** replied "called method
>   'DeleteForm' on ProjectToolViewModel; no Can method guards it, so check that it did what was meant". No prompt
>   opened, and `get_project_info` then listed no forms.

## A missing or mistyped tool argument is reported as a defect in the tool, asking to be reported

**Symptom.** (Windows, 2026-10-20.) The SDK binds a call's arguments without checking them against the schema, and
`ToolFailures` reported what that threw as an unhandled defect:

```
inspect_element {"path":"Window"}           (the parameter is 'target')
→ "'inspect_element' failed with an unhandled System.ArgumentException: The arguments dictionary is missing a value
   for the required parameter 'target'. ... This is a defect in the tool, not a refusal of your arguments: please
   report it."
take_snapshot {"inline":"true"}
→ "... System.Text.Json.JsonException: The JSON value could not be converted to System.Boolean ... This is a defect
   in the tool ..."
```

The reply said the opposite of what happened and asked for a bug report, for every tool and every parameter.
[#1270](https://github.com/hexide-io/HexIDE/issues/1270).

> **Fixed.** Before a tool runs, `ArgumentCheck` checks the call's arguments against the tool's input schema, which the
> SDK never does, and refuses with every problem it finds:
>
> - each required parameter that is missing;
> - JSON of the wrong type, including null where the parameter cannot hold it, and an array item, named by position;
> - an argument the tool does not take, named only beside a real problem, as the likely misspelling, because the SDK
>   ignores it otherwise.
>
> The check is no stricter than the binding: a number in a string passes, as the SDK's serializer reads it. What the
> schema cannot see, such as an integer too big for an `int`, still fails in the SDK's binding. `ToolFailures` turns
> that into a refusal too, naming the argument out of range. In both cases the reply says nothing was done and does
> not ask to be reported. An `ArgumentException` a tool throws itself is still a defect.
>
> The first version classified only what the binding threw. Its review found that a null for a non-null parameter
> bound and failed inside the tool, still asking to be reported. It also found that a numeric string was flagged though
> it binds, and that array items and overflow fell back to the serializer's raw message.
>
> Measured on Windows through the server's HTTP endpoint:
>
> ```
> inspect_element {"path":"Window"}
> → "'inspect_element' refused its arguments, and nothing was done: 'target' is required; it takes no 'path'
>    parameter. ..."
> set_bookmarks {"name":"Form1","lines":null}
> → "... 'lines' must be array, not null. ..."
> ```
>
> Guarded by `McpRestartTests`, through a real server and the SDK's client.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools:
>
> ```
> inspect_element {"path":"Window"}
> → 'inspect_element' refused its arguments, and nothing was done: 'target' is required; it takes no 'path'
>    parameter. Its input schema names each parameter and its type.
> set_bookmarks {"name":"Form1","lines":null}
> → 'set_bookmarks' refused its arguments, and nothing was done: 'lines' must be array, not null. …
> hover {"target":"…/Custom/List/Button[Mouse]","dwellMs":3000000000,"window":"ide"}
> → 'hover' refused its arguments, and nothing was done: 'dwellMs': The JSON value could not be converted to
>    System.Int32. Path: $ | LineNumber: 0 | BytePositionInLine: 10. …
> ```
>
> - **The third fell short of the fix above (#1965), now corrected.** Integral overflow names the value,
>   whether it is too large or too small, and the whole-number range; parameter guidance adds the tool
>   limit where declared (hover: 0 to 10000 ms). Linux SDK-client tests cover int, long and array items;
>   a direct binding test checks hover guidance. A live repeat of the call above remains to be checked.
> - **`take_snapshot {"inline":"true"}` never reached the server as a string.** Claude Code converted it to the
>   boolean the schema asks for, and the call succeeded. That refusal is checked only by the SDK-client tests in
>   `McpRestartTests`.

## Replies escaped every apostrophe and non-ASCII character, as if bound for a web page (#753)

A tool's reply was written with `System.Text.Json`'s default encoder. That encoder escapes `'`, `+`, `<`, `>`, `&` and
anything outside ASCII, so JSON can sit safely in a web page. The text a caller read was full of `\u0027`, which the
replies use to quote every name. A module named `Ж1` arrived as `\u04161`. #729's code-page refusal names the one
character the code page cannot hold, and that character arrived escaped. The in-IDE AI chat wrote its tool replies the
same way.

> **Fixed.** Tool results, the reply `OpenDialogNote` rewrites to add an open dialog, and the AI chat's tool replies are
> written with `ReadableJsonEncoder`. It escapes a quote and a backslash, as JSON requires, and anything a reader would
> not see: control, format, line and paragraph separator characters. Everything else is written as itself, outside
> the Basic Multilingual Plane too. The relaxed encoder was not enough: it still escapes an emoji or a CJK Extension B
> character, and it writes a right-to-left override raw, where it would silently reorder the reply around it.
>
> Measured on Windows through the server's HTTP endpoint, with the JSON-RPC envelope decoded:
>
> ```
> add_file {"name":"Ж1","type":"Module"}
> → {"success":false,"error":"'Ж1' was not added: Ж1 is kept in code page 1252, which cannot hold 'Ж' (U+0416), …"}
> ```
>
> The envelope itself is still escaped on the wire. The SDK writes it with its own options, which a server cannot set,
> and a client decodes it before reading the text. `/health` is left as it was, since it is read by code.
>
> Guarded by `ReadableJsonEncoderTests`, which include a round trip of random text. `McpRestartTests` run through a real
> server and the SDK's client, and `OpenDialogNoteTests` cover the rewritten reply. A source test holds the IDE's
> server to the same options.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools:
> `add_file {"name":"Ж1","type":"Module"}` was refused as above. The text
> the client showed has `'Ж1'` and `'Ж' (U+0416)` as characters, and its apostrophes as themselves, with no `\u`
> escape anywhere. The only backslashes are JSON's own, before the quotes in `\"Ж1\"`.

## `expand` also opens a control's context menu or flyout, and no description says so

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
dump_visual_tree {"maxDepth":3}
→ Window/Custom[MainView]/None[#0]   (the menu band's StackPanel)   providers ["expandCollapse"]
  TabItem[…]/None[PART_TabBody]      (a Border)                     providers ["expandCollapse"]
dump_visual_tree {"root":"Window/Custom/Custom","interactiveOnly":false,"maxDepth":2}   (Options > Editor > Formatting)
→ the Tab width TextBox                                             providers ["value","expandCollapse"]
```

The run read these as phantom tokens. They are not: each control owns a popup that `expand` opens, by design since
#343. The StackPanel owns the Toolbars context flyout, `PART_TabBody` Dock's document context menu, and every
TextBox the theme's Cut/Copy/Paste flyout. But `interact`'s `action` description defines expand and collapse as
"a dropdown, tree node or expander", and `dump_visual_tree` says only "expandCollapse → expand/collapse", which
is how #836 and its entry (now archived) came to say that no tool opens a context menu. `collapse` with the popup closed
answers "element does not support 'collapse'" while the token is still shown. Cross-platform by code reading.

**Workaround.** On a control with no expander of its own, read `expandCollapse` as "owns a context menu or
flyout": `expand` opens it, and its items then appear under it in `dump_visual_tree`.

**Suggested fix.** Say so in `interact`'s `action` description and, in a few words, in `dump_visual_tree`'s token
list, and have `collapse` with nothing open say there is nothing to close.
[#949](https://github.com/hexide-io/HexIDE/issues/949).

> **Fixed.** `interact`'s `action` description lists, under expand and collapse, a menu, or else the context menu or
> flyout the control owns. `dump_visual_tree`'s token list says the same of `expandCollapse`, and CLAUDE.md adds that
> the popup's items then appear under the control. Room in the two descriptions came from shorter wording of `scroll`'s
> reply and of the opening of `dump_visual_tree`'s. Tests pin the new text, so a later trim cannot drop it unseen.
>
> Every reply says what was already the case:
>
> - `collapse` with the popup closed says it was not open and there is nothing to close, as a closed menu's did,
>   rather than "element does not support 'collapse'";
> - `expand` on an open popup, menu or dropdown says it was already open;
> - `collapse` on a closed dropdown says it was already collapsed.
>
> Its review found three more problems, now fixed:
>
> - `collapse` on the code editor with the completion list open said its flyout had nothing to close, while the dump
>   showed the list under it. That is now refused, pointing to `press_key Escape`.
> - The theme's TextBox context flyout is one instance shared by every box. An open one was closed, and listed in the
>   dump, under every box. Both now go by the flyout's target.
> - `expand` prefers a ContextMenu to a flyout, which `FlyoutsOf` now records.
>
> Guarded by `LogicallyParentedPopupTests`, which cover the four routes to a popup, `CodeEditorPopupTests`,
> `MenuPopupTests` and `InteractClickDescriptionTests`.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools, in
> Tools ▸ Options ▸ Form Designer ▸ Grid, a page with two TextBoxes:
>
> ```
> interact {"target":"Window/Custom/Custom/Edit[Grid Width:]","action":"expand","window":"Options"}
> → opened flyout on 'Grid Width:'
> dump_visual_tree {"window":"Options","root":"Window/Custom/Custom","interactiveOnly":false,"maxDepth":10}
> → MenuItem[Cut], MenuItem[Copy] (both disabled) and MenuItem[Paste] under Edit[Grid Width:], none under Edit[Grid Height:]
> expand again   → the flyout on 'Grid Width:' was already open
> collapse       → closed flyout on 'Grid Width:'
> collapse again → the flyout on 'Grid Width:' was not open; there is nothing to close
> ```
>
> With the code window's completion list open, `collapse` on its `TextEditor` was refused: "'TextEditor' has a popup
> open that collapse does not close, such as the completion list; press_key Escape closes it".

## `set_control_property` could not reach a control-array element, and wrote to the first when given the array's name (#1252)

`get_form_controls` takes an element as `name(index)` and marks its rows settable, but `set_control_property` matched
the name alone. `lblTitle(1)` was refused as no such control. `lblTitle` wrote to whichever element came first, saved
the file, and replied as though the caller's element had been set.

> **Fixed.** `set_control_property` resolves its control with `PropertyText.NamedOne`, as `get_form_controls` reads
> one. `name(index)` reaches that element. A bare name that matches several controls changes nothing, since the save
> would write a guess to disk, and the refusal lists the array's indexes. An index the array lacks is refused the same
> way, rather than as no such control. The reply names the element, as in `optSize(1).Caption is now 'Seven'`. An index
> too big for an int, or written in non-ASCII digits, names no element where it used to throw. Measured on Windows
> through the server's HTTP endpoint, on a copy of the battleship demo: `optSize` was refused with "Index 0, 1, 2", and
> `optSize(1)` set the second option's caption, as the saved `.frm` shows. Guarded by `PropertyTextTests`.
> `move_control` took the first match too, so no element after the first could be moved, and the next save wrote the
> guess to disk; it now resolves the same way. `get_form_controls` gives each element its `index`, so a caller can tell
> the rows apart before naming one. A name with an index on a control that is no array says so. Renaming one element
> still takes it out of its array with no way back, as the Properties window does; the reply says so, and #1276 holds the
> fix. A source test checks that both tools resolve with `NamedOne`.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools, on
> a copy of the battleship demo:
>
> ```
> set_control_property {"formName":"frmSetup","controlName":"optSize","property":"Caption","value":"Seven"}
> → Nothing was changed: 'optSize' names a control array of 3, Index 0, 1, 2; name one element, such as optSize(0)
> move_control {"formName":"frmSetup","controlName":"optSize","left":20,"top":72}
> → Nothing was moved: 'optSize' names a control array of 3, Index 0, 1, 2; name one element, such as optSize(0)
> set_control_property {… "controlName":"optSize(1)","property":"Caption","value":"Seven"}
> → optSize(1).Caption is now 'Seven', and the file is saved.
> move_control {… "controlName":"optSize(2)","left":216,"top":72}
> → optSize(2) is at Left 216, Top 72, Width 73, Height 17, … It lies at least partly outside frmSetup, …
> set_control_property {… "controlName":"cmdOK(0)","property":"Caption","value":"Go"}
> → Nothing was changed: 'cmdOK' is not a control array, so it takes no index; name it as cmdOK
> ```
>
> `get_form_controls` then gave optSize(1) the caption Seven and optSize(2) left 216, and `take_snapshot` showed
> both. The saved `frmSetup.frm` has `Caption = "Seven"` in Index 1's block and `Left = 3240` in Index 2's. The
> move was saved by the next property save, since `move_control` does not save.

## Putting the caret on a line still took offset arithmetic (#700)

Once `get_file_content` returned the whole file (#273 task 3.18), a caller could at least count out the buffer
offset `interact set_property CaretOffset=N` takes, but it still had to count: there was no way to name a line and
column, as every tool that takes a line does. Reaching a member's `Attribute` line meant `press_key` Ctrl+End, Up three
times, then Home.

> **Fixed.** `open_file` takes `line` and `column`, 1-based and counted as the code window numbers them, header
> included, so a line from `get_diagnostics`, a breakpoint or `get_file_content` can be passed straight on. Columns
> count characters, as the status bar does, so a tab is one; one past a line's last character is its end. A place off
> the text, or between the halves of one character, is refused before anything opens, rather than clamped, since a
> caret quietly put elsewhere would take the next `type_text` there; so are `column` without `line` and a line for a
> carried file. The note reads the caret back once focus and layout have run, rather than echoing the request, and says
> so if the editor moved it.
>
> The review found three ways the caret was not where it could be seen, and the first two reached any navigation, as
> go-to-definition into a file not yet open places the caret the same way. A view made for a caret already placed did
> not scroll to it, as `ScrollToLine` does nothing before layout; it now scrolls once laid out. A caret placed in a
> header before its fold existed was folded away under it; a new header fold is now left open over the caret. A caret
> put at a folded member run's end was moved to the declaration's end, while the reply said it was where asked; a caret
> navigated into a fold now opens it. Measured on Windows through the server's HTTP endpoint, on a copy of the
> battleship demo with an Attribute run added to `Game.Init`: a line past the end opened nothing; `frmBoard` opened at
> line 150 showed it; `Game` at the run's end opened the run with the caret there; and `type_text` wrote where
> `open_file(frmSetup, line 94, column 5)` put the caret. Guarded by `CaretPlacementTests` and `OpenFileCaretTests`.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools, on
> a copy of the battleship demo with
> `Attribute Init.VB_Description` and `Attribute Init.VB_HelpID` added under `Game.Init`:
>
> ```
> open_file {"name":"frmBoard","line":999}
> → Nothing was opened: in BattleshipForms/frmBoard, line 999 is not one of its lines, numbered 1 to 153
> open_file {"name":"frmBoard","line":150}
> → … The caret is at line 150, column 1: offset 4942 in the text get_file_content returns.
> open_file {"name":"Game","line":25,"column":29}
> → … The caret is at line 25, column 29: offset 707 in the text get_file_content returns.
> open_file {"name":"frmSetup","line":94,"column":5}, then type_text "Rem " into its TextEditor
> → get_file_content: line 94 reads "    Rem If optSize(2).Value Then"
> ```
>
> `get_document_tabs` after the first call listed no frmBoard tab. `take_snapshot` after the second showed line 150
> in view, with "Ln 150, Col 1" in the status bar. After the third, it showed the two Attribute lines unfolded under
> `Init`, with the caret after the second's `1` and "Ln 25, Col 29".

## Two parameter descriptions told a caller something other than what was meant (#1258)

`type_text`'s `mode` was written as a regular C# literal, so its `\n` and `\t` reached a caller as a line feed and a tab.
The sentence that tells a caller how keyboard mode types several lines read "per character, (a line break) as Enter and
(a gap) as Tab". `interact`'s `close` said a MsgBox with no Cancel may refuse. An OK-only box has no Cancel, yet its
close box answers vbOK; only Yes/No and Abort/Retry/Ignore boxes refuse, as the oracle measured.

> **Fixed.** `mode` names the two characters. `close` names the boxes that refuse, Yes/No and Abort/Retry/Ignore, and
> that a form's QueryUnload may, in fewer characters than before, since the `action` description sits just under the
> 2048 a client delivers. CLAUDE.md gives the whole list, a modal form over the window included.
>
> `ToolDescriptionTextTests` holds every tool and parameter description to no control, format, line or paragraph
> separator character, except a blank line between paragraphs, which five descriptions use. It fails on the text this
> replaces, and pins both corrected sentences.
>
> Its review found that `ToolSource` stopped reading a description at the first piece that was not a literal. Three
> tools end with `+ ProgramWaitNote`, so 361 characters of each were neither measured against the length cap nor
> checked. It now reads a `const string` in the chain, and reports a chain that goes on past what it can read.

## `select` reported a selection it could not read back as made (#1259)

`select` reads the selection back before reporting it (#1182), but the read fell back to *selected* when the provider's
`IsSelected` threw. A throwing peer was replied to as `selected 'X'`, and `double_click` counted its first click as a
selection, though nothing had been read: a fail-open default in the step that exists to verify.

> **Fixed.** `IsSelected` is read as selected, not selected, or unread. An unread row its list still holds is read
> from the list's own mark on it, which is all Avalonia's peer reads, so a list that puts the selection back, or picks
> another row of the same value, is refused as before, and a row already selected is taken. A row its list has let go
> is taken if the list announced it, as before. With neither, the reply says select was sent but whether it took is not
> known, and says so of an item with no list rather than blaming one; `double_click` says it is not known whether it
> selected first, rather than that it did not. No provider in the tree throws there; HexIDE implements no
> `ISelectionItemProvider` of its own. Guarded by `SelectUnreadTests`, with a substitute provider that throws over a
> real `ListBox`. The review listed the driver's other reads that report success unread, which #1280 holds.

## A dialog over an idle running form was named by neither the reply's note nor the wait (#1253)

`interact`, `type_text` and `press_key` on a running form leave the program's dialogs to the wait for the program
(#1232), and the dialog note (#681) skipped them, as the wait names them. But the wait names a dialog only while the
program is busy, and returns at idle naming only forms. A compile error reached from a click shows its dialog over the
form without holding the program, so `invoke` on the button replied `invoked Button 'Command1'`, clean, with the error
dialog open over the form, and the caller's next action landed behind it. Whether the note named it depended on which
window had focus, as that decides the dialog's owner.

> **Fixed.** The wait records the dialog it names, and the note leaves out exactly that one, every other dialog
> included, whatever the program is doing by the time the note looks. A first fix that left a program's dialogs to
> the wait only while the program was held still lost the dialog when a Timer's tick made the program busy as the note
> looked: the reviewer saw 5 of 15 replies with no note. It also hid a second dialog, as the wait names one at most.
>
> The wait now picks the window it names better:
> - a dialog open before the action is left out when the program was idle then, as it held nothing, so an error
>   dialog left open by an earlier call is not named in place of the MsgBox the action raised;
> - among windows the form already owned, the program's own box comes first, as that is what holds a handler;
> - a window that is not the program's box is said to *may* hold the program, not to be a MsgBox holding it.
>
> Measured on Windows through the server's HTTP endpoint, with a 40 ms Timer looping 3000 times:
> - a click reaching `NoSuchSub` was named in the note in 10 of 10 calls;
> - with that error dialog left open, a click raising a MsgBox named the MsgBox in the wait's text and the error dialog
>   in the note, as "still open".
>
> Guarded by `OpenDialogsTests`, `OpenDialogNoteTests` (through the filter) and `ProgramWaitTests` (the compile error
> shown over the form unawaited as the IDE shows it, a MsgBox beside a dialog left open, twice, and a window that is not
> a box).
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools.
> The form is the one above: a 40 ms Timer looping 3000 times, Command1
> calling `NoSuchSub`, and Command2 showing `MsgBox "Hello"`.
>
> - **Ten runs, each one click on Command1, answered with End.** The call was
>   `interact {"target":"Window/Button[Command1]","action":"click","window":"auto"}`.
>   - **The note named it in 8 of 10, against the 10 of 10 measured above:**
>     `'HexIDE' (the run-time error dialog: "Compile error:…", Continue/End/Debug/Help), … opened during this call`.
>   - In the other 2 only the wait gave it, and the note left it out, as above. The wait gave the title alone:
>     `(the program is still running Timer1_Timer, and has a window open over its form, 'HexIDE'; it may hold the
>     program until it is answered, …)`. So those two replies never said what it was, and the reset prompt has the
>     same title: [#1975](https://github.com/hexide-io/HexIDE/issues/1975).
> - **With that dialog left open, a click on Command2:**
>   - the wait's text named the MsgBox, as "a MsgBox holds its handler", by its title `'DialogProbe'`;
>   - the note named the error dialog as "still open and waits for an answer; this call reached past it, as a
>     person could not".
> - **Answering the MsgBox** gave a "still running" list of Command2_Click, Timer1_Timer, Command2_GotFocus and
>   Command2_LostFocus. The form has no handler for the last two:
>   [#1976](https://github.com/hexide-io/HexIDE/issues/1976).

## `interact` reported actions done without reading them back, and acted where visibility could not be read (#1280)

`select` read its result back (#1182, #1259), but these replied that they had acted without reading anything:
- `toggle`;
- `expand` and `collapse` through a provider;
- opening and closing a context menu or flyout;
- `set_value`.

A TextBox that reformatted the value was replied to as set to what was sent. The guard that refuses to act on a hidden
control (#1069) read visibility with a fallback of "shown", so a read that threw let a hidden control be acted on.

> **Fixed.** Each of these now reads its state back and replies with what it found:
> - `toggle` says the state it moved to. One that did not move is still replied to as done, saying why: a click does
>   not turn a radio button off, or a handler or binding put it back. The control's Click ran, and a caller told
>   otherwise would click again and run it twice.
> - `expand` and `collapse` are refused when the state read back is not the one asked for.
> - A context menu or flyout cancelled as it opened, or one that did not close, is refused. One light-dismissed a
>   moment later is not seen.
> - `set_value` on an unbound control is refused with what the control holds, when that is not the value sent.
>   - A bound box is not judged here. Its source may convert the value and push the result back at once, so `" 90"`
>     reads `"90"`, taken. `RefusedCommit` says whether the source kept it, as before.
>
> On a running form, the program's events run later, through its queue. There `toggle` and `set_value` are read once
> the wait for the program finds it idle, as a close is: "set value of 'Edit' to 'abc', and the program then made it
> 'ABC'". When the wait comes back early, its own note says the form may still change.
>
> Where nothing can be read, the reply says the outcome is not known, as `select` does. Visibility that cannot be read
> refuses the action, naming the control, in `interact`, `type_text` and `press_key`. A dump, which only reports, marks
> only what was read, and `IsEnabled` in a dump still reads as enabled when it cannot be read; actions are not affected.
>
> Measured on Windows through the server's HTTP endpoint: toggling a checkbox in Options replied `it is now off`, then
> `it is now on`. The review's live probe confirmed the IDE's own surfaces read back as done:
> - toolbar and Project Explorer flyouts;
> - the shared TextBox flyout;
> - the ObjectSelector ComboBox;
> - tree chevrons;
> - the Options checkboxes.
>
> Guarded by `ReadBackTests` and `ProgramWaitTests`.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools: in
> Tools ▸ Options, `interact` `toggle` on
> `Window/Custom/Custom/CheckBox[Reload files changed outside the IDE]` with `window` "Options" replied "toggled 'Reload
> files changed outside the IDE': it is now off", then "… it is now on".

## take_snapshot's default capture picks a modeless dialog (#1264)

**Was:** (both halves closed now; see the note at the end) *take_snapshot of the IDE fails while a menu is open, and the default capture picks a modeless
dialog.* The first half is closed. Measured for #362: `interact(target: ".../MenuItem[Edit]", action:
"expand", window: "ide")` then `take_snapshot(window: "ide")` returned an image with the open Edit menu
composed in. The second half, the Find dialog being preferred over the main window, was not re-tested and
stays open. The rest of this entry is the original record.


**Symptom.** Two halves of one gap, both hit while verifying that Edit ▸ Find greys out on a document with
nothing to search (hexide-io/HexIDE#363). With the Edit menu expanded, `take_snapshot(window: "ide")`
returns `An error occurred invoking 'take_snapshot'` — no path, no detail. Plain `take_snapshot()` succeeds
but captures the modeless **Find** dialog instead, because a dialog is preferred over the main window and a
menu popup is apparently neither.

**Consequence.** A greyed-out menu item is the whole of the feedback for a disabled command, and it is the
one thing that cannot be photographed. Both routes fail in a way that looks like a broken tool rather than
an unsupported surface.

**Workaround used.** `inspect_element` on the menu item reports `isEnabled: false`, and
`dump_visual_tree(root: ...Edit/Pane, interactiveOnly: false)` gives the whole menu's enabled states in one
call. That is a stronger assertion than a pixel diff, so nothing was lost here — but it means the *rendering*
of the disabled state (does it actually look greyed?) is unverified.

**Suggested fix.** Treat an open menu popup as a capturable window, and let `window` take a popup or
overlay selector. Failing that, return a real error message saying the surface cannot be captured while a
popup is open, rather than a bare "an error occurred".

**Since #603.** A tool that throws now answers with the tool name, the exception type and its message instead
of the bare line, and says it is a defect to report. Re-measured 2026-09-22: with the Edit menu expanded,
`take_snapshot {"window":"ide"}` no longer throws at all and returns a path, which agrees with the narrowing
note above. The half still open, the Find dialog being preferred, was not re-tested.

**The open half stands, read in code on 2026-10-20 at 4789f9a and not run.** Find is shown modeless with no owner
(`WindowManager.ShowWindow`, `window.Show()`), and `ForegroundWindow.Pick`, under `auto`, lets any visible window
but the IDE's own win. So while Find is open, every tool that takes `window` resolves against it, whether or not the
IDE's window is the active one, though `take_snapshot`'s and `dump_visual_tree`'s descriptions say only a modal
dialog is preferred. A path that then fails to resolve says `no child matching … under 'Window'` and names no
window. **Workaround:** pass `window: "ide"` while Find is open. **Suggested fix:** say what `auto` does, a modeless
window included, and have a failed path under `auto` name the window it was resolved against.
[#1264](https://github.com/hexide-io/HexIDE/issues/1264).

> **Fixed.** Both halves are now closed. The rule is kept and stated: under `auto`, any visible window other than the
> IDE's own wins, whether or not it is on top, among them a dialog, the Find window, or a running form. Among several, a
> dialog wins, then the one with focus, then the one opened last. The `window` parameter's description, `take_snapshot`'s
> and `dump_visual_tree`'s now say so, in place of "a modal dialog" and "the frontmost".
>
> A well-formed path that fails in a window `auto` picked over the IDE's own now names that window. If the path resolves
> in the IDE's own window, the reply also says `window: "ide"` reaches it. The IDE's own window is probed only on failure,
> and only read, so a typo, or a path into a running form while Find was picked, is not sent the wrong way. A path that
> is not one at all is refused as before, without naming a window.
>
> Letting the main window win while it is active was not done. `IsActive` is false for every window while the app is in
> the background, and never set headlessly, so a call's window would depend on the caller's desktop. The review found
> that this dependence already exists among other windows: with a program running and Find open, the one with focus
> wins. The descriptions now give that order and point to a form's name as the stable handle.
>
> Measured on Windows through the server's HTTP endpoint, on a new project with Edit ▸ Find open:
> - `inspect_element(Window/Custom[MainView])` named Find and pointed to `"ide"`;
> - `Custom[MainVeiw]` named Find without the pointer;
> - `Find/x` was refused as not a path.
>
> Guarded by `ForegroundWindowTests`, which resolve real paths in two windows through `ForegroundWindow.Resolve`. The
> server's six path-taking call sites go through that method.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools, on
> a new project with Edit ▸ Find open:
>
> ```
> inspect_element {"target":"Window/Custom[MainView]"}
> → no child matching 'Custom[MainView]' under 'Window'. The path was looked for in 'Find', the window "auto" picked
>    over the IDE's own; it resolves in the IDE's own window, which window "ide" reaches
> inspect_element {"target":"Window/Custom[MainVeiw]"}
> → no child matching 'Custom[MainVeiw]' under 'Window'. The path was looked for in 'Find', the window "auto" picked
>    over the IDE's own
> inspect_element {"target":"Find/x"}
> → path must start with 'Window' (got 'Find')
> ```

## A collapsed ComboBox reports only its selected item, which reads as "the item is missing" (#1263)

**Symptom.** `inspect_element` on a closed dropdown returns a `selectionItems` array containing just the
current selection:

```
"selectionItems": ["Form1"],
"value": "Form1"
```

The control had two items. `expand` then `dump_visual_tree` shows both:

```
ComboBoxItem[Sub Main]
ComboBoxItem[Form1]
```

**How it bit.** Verifying that `Sub Main` had been added to the Project Properties → Startup Object list
(#210). The inspection said the list held only `Form1`, which is exactly what a change that had failed to
take effect would look like — and the build had just been rebuilt and relaunched, so "the binary is stale"
was the obvious next hypothesis. The feature was working the whole time.

`interact(action: "select")` is honest about this — it fails with *"has no selectable items realized — if
it's a dropdown, 'expand' it first"* — and `dump_visual_tree`'s own description warns that virtualized
dropdown items are not addressable until realized. `inspect_element` carries no such warning and reports a
plausible-looking array instead of an empty one, which is the part that misleads.

**Workaround.** Never read `selectionItems` from a collapsed dropdown as the item list. `expand` first,
then `dump_visual_tree(root=<combo>)` and read the `ComboBoxItem` children. Expand again immediately before
`select` if the popup has closed. Since #907, `select` closes the dropdown as a click does, and its reply says so.

**Corrected 2026-10-20: `selectionItems` is the selection, not a partial list of items.** Read in code at 4789f9a,
not run. `inspect_element` fills it from the selection provider's `GetSelection()` (`UiAutomationDriver.Inspect`), as
it has since the first commit, so it holds what is selected whether the dropdown is open or closed: Avalonia
12.0.4's ComboBox peer reports a closed box's selected item through a stand-in peer (`UnrealizedSelectionPeer`, read
by reflection). `["Form1"]` was the right answer to another question. What misled is that the name reads as the
list's items and nothing in the reply says how many items there are, so the fix first suggested below aims at a
mechanism that is not there.

**Suggested fix, as first written.** Either omit `selectionItems` when the items are unrealized, or mark it — an
`itemsRealized: false` alongside it would be enough. A partial list that looks complete is worse than no
list, because it supports a confident wrong conclusion; an empty array with a flag supports none.

**Suggested fix.** Say in `inspect_element`'s description that `selectionItems` is what is selected, not the list,
and report an `itemCount` for a list or dropdown, which counts its items whether or not they are realized.
[#1263](https://github.com/hexide-io/HexIDE/issues/1263).

> **Fixed.** `inspect_element` now reports `itemCount` for a list, dropdown or tab control, from its `ItemCount`, which
> counts its items whether or not their containers exist, hidden ones too. It is absent for anything else: a menu or the
> dock's lists count separators and splitters that a dump does not show. Its description says `selectionItems` is what is
> selected, not the list's items, and that a dropdown's items are read by expanding it and dumping it. A ListBox's and a
> TabControl's peers offer no selection provider, so `selectionItems` was `[]` on every one of them, which "what is
> selected" would have read as nothing selected. It is now read from the list itself, by the names the dump gives its
> rows. Guarded by `InspectItemCountTests` (a ComboBox closed and open, a ListBox, a TabControl, a menu and a button) and
> `InspectElementDescriptionTests`.

> **Live, through the hexide tools (2026-10-27, Windows at 150%, a Debug build of main at f9b5649c, headless
> `claude -p`, #1330).** The review of #1287 said it was checked live without saying how, so it was checked again. A project
> with Form1 and a module holding `Sub Main`, in Project ▸ Properties: `inspect_element` on the Startup Object
> dropdown, `Window/Custom/Tab/ComboBox[#1]`, closed, gave `"selectionItems":["Form1"],"value":"Form1","itemCount":2`.
> On the dialog's tab control, `Window/Custom/Tab`, it gave `"selectionItems":["General"]` and `"itemCount":5`.

## `get_bookmarks` did not say its lines count from 1 (#1261)

**Symptom.** Read in code at 4789f9a, not run. Since #1245, the bookmark tools count lines from 1 rather than 0.
`set_bookmarks`' note says so ("counting from 1 at the top of the file, as the gutter does"). `get_bookmarks`
returns `project`, `document`, `uri` and `lines` and no note, and its description says "1-based" without saying that
this changed; `LineNumberingDescriptionTests` refuses "0-based" or "from 0" in any description. A session that
loaded the tool's schema before the rebuild keeps "Lines are 0-based" (the schema entry in the live list), so it
reads `[5]` as the gutter's line 6, and nothing in the reply says otherwise.

**Workaround.** After a rebuild onto #1245, use the bookmark tools from a new session, as the schema entry advises.

**Suggested fix.** A `note` on a `get_bookmarks` reply that lists lines, stating the numbering in the words
`set_bookmarks` uses. [#1261](https://github.com/hexide-io/HexIDE/issues/1261).

> **Fixed.** `get_bookmarks` adds a `note` to a reply that lists lines, in `set_bookmarks`' words: "Project1/Form1 has
> bookmarks on lines 10, 11, counting from 1 at the top of the file, as the gutter does." An empty reply has none, and
> one line is "a bookmark on line 10", in both tools. The refusals that give lines, a line the file does not have or
> one in its header, now say the same, for every mark tool. The wording is `MarkLineRules`', where tests reach it.
> Measured through the server's HTTP endpoint on a new project, and guarded by `MarkLineRulesTests` and
> `LineNumberingDescriptionTests`.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools:
>
> ```
> set_bookmarks {"name":"frmSetup","lines":[95,96]}          (a copy of the battleship demo)
> → BattleshipForms/frmSetup now has bookmarks on lines 95, 96, counting from 1 at the top of the file, as the gutter does.
> get_bookmarks {"name":"frmSetup"}
> → {…,"lines":[95,96],"note":"BattleshipForms/frmSetup has bookmarks on lines 95, 96, counting from 1 at the top of
>    the file, as the gutter does."}
> set_bookmarks {"name":"Form1","lines":[10,11]}             (a new project, an earlier run)
> → 10, 11 are in a read-only part of Project1/Form1: its header (1..17) or the Attribute lines that describe a
>    member. Neither ever runs, so no bookmark can go there; its code starts at 18, counting from 1 at the top of the
>    file, as the gutter does. Nothing was changed.
> set_bookmarks {"name":"Form1","lines":[18]}
> → Project1/Form1 now has a bookmark on line 18, counting from 1 at the top of the file, as the gutter does.
> ```
>
> **The `[10, 11]` measured above could not be reproduced on a new project.** Those lines were refused as header
> lines, and the header rule predates the fix. The two-line note was shown on `frmSetup` instead. A `get_bookmarks`
> reply with no lines has no note, as above.

## `type_text` in keyboard mode stopped only for a dialog over its own window (#1257)

Keyboard mode stops when a dialog opens, since a person's next key would go to it. It looked only at the dialogs owned
by the window it typed into. The one dialog typing itself raises, the Edit-and-Continue reset prompt, is shown over
whichever window is active, often the running form. Headless, with the dialog over a second window, a second key went
into the text under it, and the typing stopped a key late on the focus check, with a reason that did not name the
dialog.

> **Fixed.** The typing records the modal dialogs open over every window before the first key, as the dialog note
> does, and stops after any key that opens a new one, wherever it opens. A program's own MsgBox that opens as the keys
> go in counts too. A MsgBox that moves to a window of its own when its form is hidden is the same box, not a new one.
> The reply names where the dialog opened: "'Reset?' opened over the running form 'Form1'", "over the window" for its
> own, or "in a window of its own". It records the dialog as named, so the reply's note of open dialogs does not name it
> a second time. `type_text`'s `mode` description says so. Guarded by `TypeKeysTests`.

> **Live, through the hexide tools (2026-10-27, Windows at 150%, a Debug build of main at f9b5649c, headless
> `claude -p`, #1330).** The review of #1291 said it was checked live without saying how, so it was checked again, with Form1
> running and the caret on a blank line of its code. `type_text {"text":"abcdef","mode":"keyboard"}` into the code
> window answered `key 1 of 6 was sent to TextEditor and raised 'HexIDE' (a message box: "This action will reset your
> project, proceed anyway?", OK/Cancel) over the window; that key may not have landed. Nothing after it was sent.`
> The IDE had the focus, so the prompt opened over its own window. Cancel left the line blank.

## `invoke` on a running form's menu item read the tick before the program's Click ran (#1255)

`interact invoke` on a menu item adds "(now ticked)" or "(now unticked)" when the tick moved, read straight after
raising Click. On a running form, `VBMenu` puts the tick back to the program's `Checked` before Click, and the
program's handler runs later, through its queue, yielding at the debugger's time slice before its first statement.
So the reply was built before `mnuLevel(i).Checked = (i = Index)` ran: Game > Intermediate replied with no tick note,
and Intermediate ended up ticked. The `toggle` refusal on a menu item tells a caller that invoke's reply says whether
the tick moved, so a reply with none read as "it did not move".

> **Fixed.** A running form's menu item names no tick when it is clicked. The ticks of every item in its menu are read
> once the wait for the program finds it idle, through the `Settled` read #1280 added, so a level menu's change is named
> whole: "invoked MenuItem 'Intermediate' …; the program ticked 'Intermediate' and unticked 'Beginner'". When the wait
> comes back early, at a breakpoint, a dialog or the time limit, the reply says the form may still change and names no
> tick. `inspect_element` now reads any checkable menu item's tick as `toggleState`, which it did not before, so the tick
> can be read back then too. The IDE's own checkable items, such as View > Minimap, keep the immediate read, which is
> right for them. On a running form's item, `toggle` no longer points a caller at `invoke` for the tick: an item such as
> Exit would run. It says the program sets the tick in the item's Click, and where to read it. Guarded by
> `ProgramWaitTests`, with handlers that tick, untick, do both and do neither, and the click alone on an item whose Click
> ticks it at once.

> **Live, through the hexide tools (2026-10-27, Windows at 150%, a Debug build of main at f9b5649c, headless
> `claude -p`, #1330).** The review of #1289 said it was checked live without saying how, so it was checked again, on a running
> form whose Game menu holds a control array, Beginner (ticked), Intermediate and Expert, and whose Click ticks the one
> clicked. After `expand` on `Window/Menu/MenuItem[mnuGame]`, `invoke` on `…/MenuItem[mnuLevel(1)]` answered `invoked
> MenuItem 'Intermediate' and closed the menu, as a click does; the program ticked 'Intermediate' and unticked
> 'Beginner'`. `inspect_element` then gave Intermediate `"toggleState":true` and Beginner `"toggleState":false`.

## An action that started a DoEvents loop was replied to at its first DoEvents, with no note (#1254)

The wait for a running program (#1232) counts a handler parked at `DoEvents` as the program waiting for input, which
#1233 settled for an action taken under a DoEvents loop already running. It counted the same for a loop the action itself
started: a Click that loops with a `DoEvents` per pass got `invoked Button 'Command1'` back at its first `DoEvents`, with
no note, while the form was half way through. The same loop without `DoEvents` was waited for.

> **Fixed.** The pump marks each walk that parks at `DoEvents` as a loop, and keeps every walk started and not finished.
> A reply records the loops running before its action, and counts the program idle only while every walk parked at
> `DoEvents` was one of them. A loop the action found running is still the program's message loop, wherever the action
> finds it, between two `DoEvents` included. A loop the action started is waited for as one with no `DoEvents` is, up to
> the limit. So is one set going by answering a MsgBox, or closing a modal form, its handler waited at: waiting on a
> window now clears its earlier loop mark (#1318). The original fix covered only handlers with no DoEvents before
> the window wait. A Timer's tick is no action's work, so its loop does not hold a reply. A loop outlasting
> the limit gets the usual note, naming only what the action set off: "(the program was still running Command1_Click 3s
> later, so the form may still change)". Walks, not handler names, so a click that re-enters a handler parked under the
> same name is told apart from it. A `Show vbModal` park is unchanged, so an action that opens a modal form still
> replies at once. The wait note in the `interact`, `type_text` and `press_key` descriptions says a loop running before
> the action, or a Timer's, is not waited for. Guarded by `ProgramWaitTests`, with no time slice so that `DoEvents` is
> the loop's only yield.

> **Live, through the hexide tools (2026-10-27, Windows at 150%, a Debug build of main at f9b5649c, headless
> `claude -p`, #1330).** The review of #1294 said it was checked live without saying how, so it was checked again, on a running
> form whose Command1_Click counts Text1 from 1 to 200 with a `DoEvents` each pass, then sets it to "done". `interact
> {"target":"Window/Button[Command1]","action":"invoke","window":"Form1"}` answered `invoked Button 'Loop'`, with no
> note, and `inspect_element` on `Window/Edit[Text1]` straight after gave `"value":"done"`.

## `select` and `type_text` applied #1069's not-showing rule unevenly (#1256)

Three paths still acted on, or gave advice from, rows and template parts that do not show:

- **`select` with a value on a list** looked among unrealized rows whenever the item count exceeded the rows that showed, which a hidden row (the Properties window's collapsed category) made true even with every row realized. It scrolled nothing, and the miss said the rows not on screen had been brought into view. The rows it did scroll in were matched with no hidden filter.
- **`select` under nested collapsed tree nodes** named only the nearest collapsed node to expand, and `expand` on it was refused as not showing while the one above it was collapsed too.
- **`type_text`** picked a non-editable ComboBox's hidden template TextBox ahead of a TextBox after it, as in the Object Browser's toolbar, and was refused as aimed at a hidden control, where `press_key` passed it over.

> **Fixed.** `select` looks among unrealized rows only when a row has no container. It matches the rows it scrolls in
> among those that show. One that only a hidden row matches is refused as not showing, worded as it is found, since
> the container is recycled for another row once the list scrolls on. A miss counts the rows that show and says how
> many more do not, those scrolled in included. A row container a virtualizing panel has let go of, which keeps the
> last item it showed and, with its visibility bound to that item, shows, no longer stands in for the item. Under nested
> collapsed nodes, the refusal names each one to expand, outermost first ("'expand' 'Outer', then 'Inner', which hold
> it"), when the outermost shows. `type_text`, in both modes:
> - passes over a template part hidden within its own control's template, whatever that control's own state;
> - prefers a surface that shows, and still refuses one hidden by anything else as not showing;
> - refuses a target holding more than one surface of the kind it would pick, naming them, rather than typing into the
>   first (aimed at the Properties pane, that renamed the form);
> - refuses a non-editable ComboBox with nothing else to type into as having no text surface, saying to expand it and
>   select its item.
>
> Guarded in `UiAutomationDriverTests` and `SelectScrolledOutRowTests`.

> **Live, through the hexide tools (2026-10-27, Windows at 150%, a Debug build of main at f9b5649c, headless
> `claude -p`, #1330).** The review of #1293 said it was checked live without saying how, so it was checked again, with Form1
> selected in the designer. `type_text {"text":"x"}` on the Properties window's
> `…/Tab[TabProperties]/List[AlphabeticProperties]` answered `'ListBox[AlphabeticProperties]' holds 10 text boxes that
> show, so which one to type into is not clear; nothing was typed. Address the one you mean: …`, naming each, and the
> form kept its name. Keyboard `type_text` into the Immediate window still typed: `typed 5 key(s) into TextEditor`.

## inspect_element named a templated dropdown's selection by its view model's type (#1288)

On the Properties window's object selector, `inspect_element` gave `selectionItems` and `value` as
`["HexIDE.VisualDesigner.ComponentInstanceViewModel"]`: the selected item's `ToString()`, which the closed dropdown's
peer names it by, not the text the dropdown shows. A caller could not tell which control was selected without expanding
it and dumping it.

> **Fixed.** `ComponentInstanceViewModel` names itself by the control's name, as `ConflictedFileViewModel` and others do,
> so the object selector reads "Command1" closed and open, its rows are named in the dump, and `select` by value finds
> them; its template shows the name and the class, "Command1 CommandButton", which names no control. For any dropdown,
> a selection or value that is only the text an item gives with no `ToString` of its own, its type, generic arguments
> and all, or a record's members, is read as the closed box shows it: the visible text of the presenter of its
> selection box item. An item's own text is kept, whatever the template shows. Guarded by `InspectItemCountTests`, one
> on the real `PropertiesToolView`. A list box's unrealized rows, and rows a multi-block template leaves unnamed (the
> Object Browser's lists), are #1299.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools,
> with Command1 selected on a new project's Form1:
>
> - **Closed and expanded.** `inspect_element` on `…/Pane[Properties]/Custom/ComboBox[ObjectSelector]` gave
>   `"selectionItems":["Command1"]` and `"value":"Command1"` both times.
> - **The expanded dump** named its rows `ComboBoxItem[Form1]` and `ComboBoxItem[Command1]`, each holding the name
>   and the class as two `Text` nodes.
> - **`interact` `select` with value `Form1`** replied "selected 'Form1' and closed the dropdown, as a click does",
>   and `inspect_element` then gave `Form1`.
> - **The first `select` was refused**, with "'ObjectSelector' has no selectable items realized — 'expand' it first,
>   then select". It was sent after an `inspect_element` and a dump made with the dropdown open, and the dropdown
>   had closed between the calls: the next `expand` replied "expanded", not "already open". Why it closed was not
>   established, and it happened once in this run. The refusal said what to do, and the retry worked. Not filed
>   without a reproduction.

## Keyboard typing went into code under a dialog left open over the window (#1290)

With the Edit-and-Continue reset prompt open and unanswered, a second `type_text` in keyboard mode into the code window
returned `typed 3 key(s)`, and all three keys went into the code under the prompt, to be reverted with it if the prompt
was answered No. The prompt was shown over the active window, often the running form when a tool typed the key, so the
code window stayed usable under it; and its focus manager still reported the code as focused, so the typing's check on
focus passed. #1257 stops the typing for a dialog that opens during it, not one already open.

> **Fixed.** The reset prompt is shown over the code editor's own window, which it holds, as VB6's prompt holds the IDE
> (`IWindowManager.MessageBox` takes an owner), unless that window is minimized, which would hide it. Keyboard
> `type_text` and `press_key` are refused before a key is sent, once they have found the control the keys would reach
> and found it showing, while a modal dialog is open
> over the window they act in, directly or through dialogs between; a control in a native popup counts as in the window
> the popup opened from. The refusal names the dialog on top, which takes the keys: "'Reset?', a modal dialog
> over 'Project1 - HexIDE', is open and waits for an answer, so a person's keys would go to it, not to TextArea; nothing
> was typed. Answer it first: …". The reply's note of open dialogs leaves it out. A dialog over another window is left
> to that note, as a person could click past it. The `type_text` and `press_key` descriptions say so. Guarded by
> `TypeKeysTests` and `ResetPromptTests`; the prompt's owner in a running IDE, and the native popup case, were read from
> the code, not run.

> **Measured since (#1329).** "As VB6's prompt holds the IDE" was a claim, not a measurement. VB6's prompt is owned by
> the running form, and it holds the IDE and the program together. HexIDE's holds the one window that owns it. The
> oracle's section "The reset prompt: which edits raise it, and which windows it holds" has both.
>
> **Live (#1330, 2026-10-25), the keyboard half.** Windows, a Debug build of main at beada72, from a Claude Code
> session's hexide tools, with Form1 running and its code window in front
> (`TE` is its `TextEditor`):
>
> ```
> type_text {"target":TE,"text":"Rem ","mode":"keyboard","window":"ide"}
> → typed 1 of 4 key(s) into TextEditor, then stopped: 'HexIDE' opened over the window, and a person's next key would
>    go to it. Answer it, then send the rest
> type_text {"target":TE,"text":"XYZ","mode":"keyboard","window":"ide"}
> → 'HexIDE', a modal dialog over 'DialogProbe - HexIDE [run]', is open and waits for an answer, so a person's keys
>    would go to it, not to TextEditor; nothing was typed. …
> ```
>
> - **After the refusal**, `get_file_content` held no `XYZ`. The first key, `R`, had landed, as the edit that raises
>   the prompt does, and Cancel took it out.
> - **The native popup branch is reachable this way.** A completion list opened with Ctrl+Space stays open when a
>   keyboard `M` raises the prompt. Then
>   `press_key {"target":"TE/List[PART_ListBox]/Pane[PART_ScrollViewer]/ListItem[Dim]","key":"Enter","window":"ide"}`
>   was refused the same way: "… not to ListBoxItem; nothing was pressed."
> - **Both refusals name the prompt by its title alone:** [#1975](https://github.com/hexide-io/HexIDE/issues/1975).
> - **The list closed** when the prompt was cancelled.

## dump_visual_tree listed a row its list had let go of (#1292)

A list whose rows bind their visibility to the item, as the Properties window's Categorized list does, keeps a recycled
`ListBoxItem` in the visual tree once its virtualizing panel has scrolled: no owner, no index, still holding the last
item it showed, and showing by its binding. #1256 stopped `select` matching one; the dump still listed it as a row, a
second copy of an item or a row for one out of view, and a path to it resolved.

> **Fixed.** The control view's collector, which the dump and path resolution share, and the `#AutomationId` search
> leave out a released row and everything in it, by #1256's `Released` test, now held to an items panel's own children,
> so a container written into a template or set as content is still listed. Rows declared in `Items` and scrolled out of
> view are left out with the rest, as generated rows out of view always were. Guarded by `SelectScrolledOutRowTests`:
> after a `select` scrolls and puts the list back, and the released row's item shows again, the dump lists the rows the
> list holds, each once, and neither a path nor an id reaches the released row.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools,
> with Form1 selected and the Categorized tab showing.
>
> - **`interact` `select` with value `Width`** on `…/List[CategorizedProperties]` replied "selected 'Width' (it was
>   scrolled out of view, so the list was scrolled to it first)".
> - **The dump that followed** (`interactiveOnly` false, `maxDepth` 6) listed the four rows in view, Width, Top,
>   StartUpPosition and Left. Each appeared once, and nothing out of view was listed.
> - **The dump's order is the reverse of the screen's:** [#1966](https://github.com/hexide-io/HexIDE/issues/1966).
>   Fixed in #1966: the shared collector orders items-panel containers by their owning list's item index,
>   with non-container children afterwards. Two Linux headless regressions reproduce the reversed end-jump
>   and selected-third-row-first cases before the fix, and check item order, uniqueness and path resolution
>   afterwards (`SelectScrolledOutRowTests`). Live verification of the Properties list remains for review.
> - **Not met: a row out of view is listed.** After `scroll` `home` and a collapse of Appearance, the next dump
>   listed Width, the selected row, with `isOffscreen: true`, and so did the one after Appearance was expanded. The
>   fix says rows out of view are left out: [#1981](https://github.com/hexide-io/HexIDE/issues/1981).
> - **Collapsing the Appearance category** left its rows listed once each with `isHidden: true`, and expanding it
>   showed them once each again. That was done with its toggle, since the row itself refuses `collapse`:
>   [#1967](https://github.com/hexide-io/HexIDE/issues/1967).

## List rows of view models with no ToString were named by their type, or not at all (#1299)

A row's peer names it by its item's `ToString()`, which for a view model with none of its own is its type name; the dump
drops a type name, so such a row had no name, `select` by value could not find it, and `inspect_element` named the
selection "ListItem". The Object Browser's class, member and search lists were all of this shape.

> **Fixed.** `OBClassViewModel`, `OBMemberViewModel` and `OBSearchResultViewModel` name themselves by their names, as
> `OBLibraryViewModel` beside them already did. A survey of the IDE's lists found no other rows of this shape. A
> driver fallback naming such a row by the text it shows was tried and dropped: paths and `select` match on the peer's
> name, so the dump would have shown a name no tool accepts. Guarded by `ListRowNameTests`.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools.
>
> - **Rows are named by name.** In the Object Browser the class rows are `ListItem[Form1]`, `ListItem[CheckBox]`
>   and the rest, and CommandButton's member rows are `ListItem[Caption]`, `ListItem[Click]` and the rest.
> - **Selection.** `interact` `select` with value `Form1` on `…/List[Classes]` replied "selected 'Form1'". With value
>   `Click` on `…/List[Members]`, CommandButton selected, it replied "selected 'Click'". `inspect_element` on each list
>   named that selection in `selectionItems`.
> - **Form1's own member list was empty,** since the browser lists only a project form's code procedures:
>   [#1968](https://github.com/hexide-io/HexIDE/issues/1968).

## 7. A declarative `ToolTip.Tip` cannot be made to open (narrowed 2026-09-08)

**Was:** *No pointer-hover action — can't trigger a data tip (or any hover) via MCP.* A `hover(target, x?, y?,
dwellMs?)` action now exists and raises real `PointerEntered`/`PointerMoved`, so the editor half of this is
closed. What remains is narrower and has a different cause, so the entry is narrowed rather than archived.

**Symptom.** `hover` reaches anything that opens a tip from its **own** pointer handler, but not a tip Avalonia
manages. Measured against the running IDE:

| Target | Result |
|---|---|
| Code editor over an identifier | `tip: count As Integer` — the LSP quick-info tip, text asserted |
| `Button[Standard.AddForm]`, which carries `ToolTip.Tip` | `no tip opened within 1000ms` |

`ToolTipService` opens a declarative tip in response to `IsPointerOver` changing. That property is set by the
input manager from a real device position; a synthetically raised `PointerEntered` does not set it, and it is
not publicly settable. So the toolbar button's tip cannot be made to appear at all.

**Workaround, and it is deliberately not a fix.** When nothing opens, `hover` reports what the control
*declares* — `declared tip: Add Form` — read straight off the attached property. That answers the question a
caller usually has (*has this button the right tooltip?*) without pretending a popup appeared: an observed tip
is reported as `tip:` and a declared one as `declared tip:`, and the two are never merged. Asserting the tip's
**existence on screen**, or its placement, is still out of reach for this case.

**Two further measurements worth keeping.**

- **A synthetic tip opens at the REAL pointer, wherever it last was over this window.**
  `CodeEditorView.axaml.cs:219` sets `ToolTip.SetPlacement(TextEditor, PlacementMode.Pointer)`, so Avalonia
  anchors the popup to the pointer position **it** tracks for that `TopLevel` — and a synthetically raised
  `PointerMoved` does not update that. The position is also *per window* and *sticky*: it holds the last place
  a real pointer crossed this window, so it stays stale while the pointer is over some other application.

  **Measured, not inferred — twice, with the prediction written down first each time.**

  1. *Does it follow the real pointer?* The pointer was parked over the Toolbox strip, far from the
     identifier, and `hover` fired at the caret — reported as `(176.1, 61.8)` inside the editor. The tip
     opened against the left edge beside the Toolbox: at the pointer, not at the caret, not at the origin.
  2. *Is the resting default the screen origin or the window's client origin?* A freshly launched IDE that no
     pointer had ever crossed, pinned to screen `(300, 250)` at 900×600, with the real pointer held on the
     taskbar where even a maximised window cannot reach it. The tip opened at screen `(0, 15)` — well outside
     the window. Client-relative would have put it at roughly `(300, 280)`. **The default is the screen
     origin.**

  3. *Does it survive the pointer leaving?* The pointer was walked from the taskbar up into the window, over
     the editor, and back out through the bottom edge at y≈850, then left outside. The tip still opened
     inside the window rather than reverting to `(0, 15)`. **The position persists after the pointer
     exits** — it is a last-known value, not a live one.

  **One part of that third run resists explanation, and is left that way.** The tip's **x** matched the exit
  point exactly; its **y** did not — it appeared near the *top* of the window, just below the toolbar, having
  exited at the *bottom*. Two candidate rules were considered and neither survives arithmetic: excluding the
  menu/toolbar chrome from the client area shifts the anchor **down**, not up, and clamping to the placement
  target's bounds would pin it to the editor's **bottom** edge. So: measured, reproducible in its x, and no
  vertical rule worth defending. It does not change what a caller should do, because the conclusion below
  never depended on the position being predictable — only on its not being controllable.

  3. *Does it survive the pointer leaving?* The pointer was walked from the taskbar up into the window, over
     the editor, and out again through the **top** edge, then left outside. The tip opened at that exit
     point — just below the toolbar — rather than reverting to `(0, 15)`. **The position persists after the
     pointer exits:** it is a last-known value, not a live one, which is why a window the pointer left
     minutes ago still places its tip where the pointer used to be.

  That run also settles a fair question — whether the menu bar and toolbars count as "client area" for this.
  They do, and no special case is needed: they are ordinary controls in the same `TopLevel`, so passing over
  them updates the tracked position like anywhere else. The tip landing *just below* the toolbar is the same
  constant nudge measured in run 2 — the anchor was on the toolbar, and the tip drew beneath it.

  Every earlier sighting fits the same rule, and each had looked like a different phenomenon:

  | Where the tip appeared | Where the real pointer had last crossed the window |
  |---|---|
  | At the caret — correct, by coincidence | the editor, where the maintainer had been clicking |
  | Just below the screen origin | nowhere: freshly relaunched, never crossed |
  | Over the status bar | near the window's bottom edge |
  | Bottom-left, while the pointer sat top-right | over the terminal, so HexIDE's tracked position was stale |

  **The small drop below `(0, 0)` is the tooltip's own offset, not the title bar.** It looked like title-bar
  height on a maximised window, which is a good guess and was worth testing — but the same ~15 px appeared
  with the window at `(300, 250)`, whose title bar sits at y≈250. It is the ordinary nudge that keeps a tip
  clear of the cursor, and it is constant.

  **Two earlier readings were wrong, and are kept because the next person will make them too.** First "an
  arbitrary position" — it is not arbitrary, it is anchored to something, just not to anything the *call*
  controls. Then "always the screen origin", asserted from two samples that happened to agree; a third
  sighting refuted it, and the origin turned out to be merely the default for a window no pointer had entered.
  A rule drawn from two agreeing measurements is a guess wearing a measurement's clothes.

  **Consequence for a caller, unchanged by the explanation:** `hover` makes a tip's **text** assertable and its
  **position** meaningless, because the position is set by a pointer no automated caller has. Assert the
  reported string; never snapshot the tip.

  **Rejected:** having `hover` impose a deterministic placement (a `PlacementRect` at the hover point) so the
  tip lands where it was asked for. It would make a snapshot *look* right while showing the tip somewhere no
  real user would ever see it — trading an honest limitation for a misleading picture.

- **A synthetic tip is transient.** Without `IsPointerOver` the tip closes again shortly after opening, which
  is why `hover` polls for it rather than looking once when the dwell expires. Sampling once reported "no tip"
  for a tip plainly visible on screen.

**The debugger's Auto Data Tips — the case this gap was filed for (P6c) — are now MEASURED and work.**
Paused at a breakpoint with `total = 42` in scope, `hover` on the identifier returns `tip: total = 42`,
confirmed on screen at the same moment. It took closing gap 4 to get there: while a program is paused the
frontmost window is the program's form, so until `window: "ide"` existed there was no path to aim at the
editor.

*Two issues were opened against this line and both are resolved, one of them wrongly filed:*
hexide-io/HexIDE#334 was real and is fixed (MCP writes saved the previous content and reported success);
**#335 was not a defect** — breakpoints in `Form_Load` work, and the report was an artefact of #334
corrupting the project under test. A negative observation made while another defect is active is not
evidence, which is the part worth keeping.

**A stale-tip bug fell out of this measurement, and is fixed.** Hovering `Debug.Print` right after hovering
`total` reported `declared tip: total = 42` — a wrong answer, not a missing one. The editor closed its tip
with `SetIsOpen(false)` and left `ToolTip.Tip` attached, so the control kept advertising the previous
identifier's value. This tip is *dynamic*, rebuilt per hover, unlike the static tips that property is meant
for. Closing now clears it (`CodeEditorView.CloseTip`), which also removes the live-user hazard: a real
pointer could be shown the previous word's value before the replacement evaluation returned.

**What remains open here is only the declarative case** — a toolbar button's `ToolTip.Tip` still cannot be
made to appear, for the `IsPointerOver` reason above.

**Fix consideration.** Nothing cheap. `IsPointerOver` has no public setter, so short of Avalonia exposing one
— or a real platform-level pointer injection, which is a much larger tool — the declarative case stays out of
reach. Calling `ToolTip.SetIsOpen(control, true)` directly would make the popup appear, and was rejected: it
would report a tip for a control that a real hover might never show one for, which is a false green of exactly
the kind this document exists to prevent.

**Re-read on 2026-10-20, at 4789f9a on Avalonia 12.0.4: still open, and a route may exist.** `hover` still raises
`PointerEntered` and `PointerMoved` on the element (`UiAutomationDriver.Hover`). Avalonia 12.0.4's `ToolTipService`
is internal and handles raw input as the input manager processes it (it declares
`InputManager_OnProcess(RawInputEventArgs)`, read by reflection), which an event raised on the element never passes
through, so the conclusion above holds whatever drives `IsPointerOver`. A window can be handed raw input:
`TopLevel.PlatformImpl.Input` is public, though marked unstable, and takes a `RawPointerEventArgs`, whose public
constructor needs the window's input root. In 12.0.4 that root is the non-public `TopLevel.InputRoot`, reachable only
by reflection, as `close` already reaches the window's own close handling. Not tried.
[#1265](https://github.com/hexide-io/HexIDE/issues/1265).

> **Fixed (#1265), measured headless; the live run is [#1303](https://github.com/hexide-io/HexIDE/issues/1303).** `hover`
> sends its move through the window's own input, as the platform delivers a real one: `TopLevel.PlatformImpl.Input`,
> with the window's non-public input root, a raw pointer event and one mouse device for every hover, each reached by
> reflection, since Avalonia 12.0.4's reference assemblies hide the callback and the constructors. So the move is
> hit-tested against what was drawn and routed as a real one, and the tooltip service sees it: a declared `ToolTip.Tip`
> opens, and the code editor's own handler still gets its `PointerMoved`. The tool waits for the window's next frame
> first, as `click` does. What the move lands on is what the window routed it to, so a control drawn over the target is reported
> by name, with its tip, as that is what a person would be shown; a disabled target is said to be disabled. Only when
> the move hits nothing, or Avalonia's members cannot be reached, is it raised on the target as before, and the reply
> says why. After the watch the pointer leaves the window, closing the tip, so the next click is not covered by it.
> The review found the editor's quick info failing on a second hover at the same point (a new device per hover told
> the editor the pointer had left, and the repeat was taken for jitter); one device, and the editor forgetting its
> hover on exit, fixed that. Guarded by `HoverThroughWindowInputTests`. The placement measurements above describe the
> old route.

---

## `interact click` at a point outside its target says the control pressed there covers the target — **CLOSED**

> **Fixed (#1530, PR #1549, 2026-10-23).** A click outside the target now says the point is outside, gives the
> target's size, and names where the press landed. "Covers the target there" is kept for points inside its
> bounds. Guarded by `InteractClickTests`.

**Symptom (2026-10-23, #1503, Windows).** On a running form, `Command2` is 120 × 60
(`inspect_element`: `boundingRect [64,40,120,60]`).
`interact {"target":"Window/Button[Command2]","action":"click","value":"150,60","window":"Form1"}` answered
`clicked the left button at (150, 60) on 'VBCommandButton', which is (214, 100) in the window; the press landed on
'VBLabel', which covers the target there`. The point is past the button's width and height. The Label lying there was
pressed, and `Command2_Click` didn't run. The reply reads as a stacking defect in the product.

**Workaround.** `inspect_element` the target first and keep the point inside its `boundingRect`'s width and height.

**Suggested fix.** When the point is outside the target's bounds, say so and give the target's size, and keep "covers
the target there" for a point inside them. Or refuse such a point, as a point where nothing takes the pointer is
refused. [#1530](https://github.com/hexide-io/HexIDE/issues/1530).

---

## `invoke_menu_item` on a disabled item with a routed command blames keyboard focus — **CLOSED**

> **Fixed (#1534, PR #1551, 2026-10-23).** A materialised menu item that is not effectively enabled is now
> reported as disabled in the menu before routed-command focus is checked. The focus explanation remains for
> enabled items. Guarded by `MenuCommandRefusalTests`.

**Symptom (2026-10-23, #746, Windows).** On `--newproject`, with one project and no group,
`dump_visual_tree {"window":"ide","match":"Make Project Group","interactiveOnly":false}` showed
`MenuItem[Make Project Group...]` with `isEnabled: false`, as VB6 greys it. `invoke_menu_item {"path":"File/Make Project
Group..."}` answered `cannot execute where keyboard focus is now (a MenuItem named 'PART_ItemsPresenter' showing
MainViewViewModel)`, went on about giving a document focus, and only in its last sentence said that a disabled item is
refused the same way. The item belongs to no document, and no focus would enable it.

**Workaround.** Open the menu and read the item's `isEnabled` with `dump_visual_tree` before invoking it.

**Suggested fix.** When the `MenuItem` is materialised and not effectively enabled, say it is disabled in the menu, as
the non-routed branch and the checkable-item branch already do. Keep the focus explanation for an item the menu shows
enabled. [#1534](https://github.com/hexide-io/HexIDE/issues/1534).


---

## `press_key` reports Alt+letter success without activating an access key — **CLOSED**

> **Fixed (#1523, 2026-10-23).** Keys now go through the window's platform input callback, with the key symbol
> Avalonia's access-key handler requires. The reply names `window-input`, or `routed-key` when focus could not
> be taken or the platform callback could not be reached. Guarded by `PressKeyWindowInputTests`, including a
> running form's Click handler and a menu bar opening.

**Symptom (2026-10-23, #1390 live check, #1523).** With `cmdWide` captioned `One &Wide` and its Click setting
`lblResult`, both calls reported success with mechanism `keyboard` while the Label stayed `waiting`:

```text
press_key {"target":"Window/Button[cmdTom]","key":"W","modifiers":"Alt","window":"Form1"}
press_key {"target":"Window","key":"W","modifiers":"Alt","window":"Form1"}
```

On the IDE menu bar, `press_key {"target":"Window","key":"F","modifiers":"Alt","window":"ide"}` highlighted
File without opening it. The form also failed on the build before #1390. A headless raw Alt+W activated the
button, proving the product path worked.

**Workaround.** `interact click` on the button runs its handler; it cannot verify its access key.

**Fix.** Keep target resolution and focus, then deliver raw KeyDown/KeyUp through the window input callback,
with physical key and symbol metadata. Retain routed delivery when focus or platform input is unavailable,
and name the path in the reply. No TextInput is sent; `type_text` remains the way to enter text.


## A whole replacement that changes the header and a member's `Attribute` lines is told only about the header

**Symptom.** Read from the code, and recorded when #273 task 4.5b was built. `set_file_content` (and an add-in's
`TrySetContent`) compares the header first and returns on the first difference:

```
set_file_content {"name":"Ledger","content":"<a class header with one line changed, and Total's run changed>"}
→ refused: "The content would change the document's header (its designer block, class header or leading
   Attribute lines), which only the IDE changes. ..."
```

A caller that restores the header and sends again is refused a second time, now for `Total`'s run, a reason the
first reply did not give.

**Workaround.** After a header refusal, compare the members' `Attribute` lines with `get_file_content`'s before
sending again.

**Suggested fix.** Compare the runs whatever the header does, and refuse once with every finding.
[#1054](https://github.com/hexide-io/HexIDE/issues/1054).


**Fixed (#1054).** The shared replacement guard compares member runs even when the header differs. One refusal
names the header first, then every changed, added or removed member run, with the existing repair guidance.
`DocumentContentTests` covers all three findings alongside a header change and verifies the document stays unchanged.

## `set_file_content`'s reply does not say when its save rewrote a member's attribute qualifier

**Symptom.** `set_file_content` saves as the document's own Save does, and since #273 task 4.4 that save writes each
member's attribute run with its member's name, as VB6's own Save does whatever the module's state. Content holding
`Attribute Gone.VB_Description` under `Public Function Total()` is therefore written as `Attribute
Total.VB_Description`, and a no-op write-back of what `get_file_content` returned can change the file in that one
place. The reply says the file was written and nothing more, so a caller comparing the file with what it sent finds
a change it did not make, with no reason given.

**`set_control_property` and `add_control` save the same way, and say no more.** Read in code, not run: both save
through `ProjectService.SaveForm` or `SaveModule`, as `set_file_content` does, and both reach `SaveFiles` with
`ownCommand: true`, `SaveModule` through `SaveModuleCore`. So a property set on a form or UserControl, or a control
added to it, also writes a stale qualifier in its code with its member's name, and neither reply says so. #1043
names `set_file_content` alone.

**Workaround.** Read the file back with `get_file_content` after the write, and expect a stale qualifier to have been
re-qualified.

**Suggested fix.** Have the save report what it rewrote and say it in the reply's `note`, not the description, which
is within about 60 characters of its cap. [#1043](https://github.com/hexide-io/HexIDE/issues/1043).


**Fixed (#1043).** The form and module save paths collect the old and new qualifiers they rewrite. All three
tools append that report to a successful reply’s `note`, explaining that VB6’s own Save writes the member’s
name. Headless save tests cover both paths; source guards check all three tool replies. Live verification
remains with the supervising session: on a fresh stale run for each tool, check the note names `Gone` and
`Total`, and read back `Attribute Total.VB_Description`.

## A control with an x:Name is reported by that name alone, so the Tools menu reads `ToolsMenuItem`

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
dump_visual_tree {"root":"Window/Custom[MainView]/None[#0]","maxDepth":4}
→ … MenuItem[File], MenuItem[Edit], MenuItem[View], … MenuItem[ToolsMenuItem], MenuItem[AddInsMenuItem] …
interact {"target":"Window/Custom[MainView]/None[#0]/Menu[MenuBar]/MenuItem[ToolsMenuItem]","action":"expand"}
→ opened menu 'ToolsMenuItem'
```

An x:Name wins over the label a person sees, in the node's `name`, in its path and in reply text, and no field
carries "Tools". A caller searching the dump for the text in a snapshot finds nothing. Four menu items have an
x:Name: `ToolsMenuItem`, `LanguageServersMenuItem`, `ProtocolInspectorMenuItem` and `AddInsMenuItem`. x:Name first
is deliberate, because it does not change with the IDE language, so the fix adds rather than swaps.
Cross-platform by code reading.

**Workaround.** Address the item by the x:Name the dump shows. `MenuItem[Tools]` should also resolve, through the
label fallback, but that is read in code and was not tried.

**Suggested fix.** A `label` field on a node whenever the peer's label differs from `name`, reply text that uses
it (`opened menu 'Tools' (ToolsMenuItem)`), and a description that says a segment may be addressed by either.
[#948](https://github.com/hexide-io/HexIDE/issues/948).


**Fixed (#948).** Dumps and inspection now report the peer text as `label` when it differs from `name`.
Paths keep x:Name and accept the label too; discovery searches include it, and interaction replies use it.
Four headless regression cases cover these surfaces and failed before the fix. Live verification of the
IDE menu remains for the supervising session.

## A refused edit's reply sends a Backspace back to the caret it was refused at, and does not say whose lines are in the way

**Symptom.** Found in #273's live check (task 4.8) on 2026-10-14, with a class whose `Public Function Total()` has
its run `Attribute Total.VB_UserMemId = 0` folded into the declaration's row, and the caret put at the start of the
body line below it, offset 790:

```
interact {"target":".../Custom[Root]","action":"set_property","value":"CaretOffset=790"}
press_key {"target":".../Custom[Root]/None[TextEditor]","key":"Back"}
→ {"success":false,"error":"press_key refused: offset 789 is in the file's header or a member's Attribute lines,
   which only the IDE changes (the designer, a save, a rename). Nothing was written. The nearest place that can be
   edited starts at offset 790; put the caret there with interact set_property CaretOffset=790. ..."}
```

The refusal is right: Backspace there would join the body line to the run's last line. The advice is not. Offset
790 is where the caret already was, so a caller that follows it presses the same key in the same place and is
refused again, which a first-contact model can loop on. The reply also says "the file's header or a member's
Attribute lines" although the IDE knows which, and since #273 task 4.5b names the member in an add-in's refusal
(`GuardedContent.MemberNamedBy`). `type_text` gives the same wording: at offset 766, inside the run, it said
"header or a member's Attribute lines" and pointed at 790.

**Workaround.** Read the refused offset, not the advice. A backward deletion refused at the start of the line under a
run has nowhere to go on that line; delete the declaration line whole instead (Ctrl+D, which takes the run with it).

**Suggested fix.** Name the region: "the file's header" or "the Attribute lines describing Total" (`MemberNamedBy`).
For a deleting key whose span ends at the caret, say that the text before the caret is the run's and that deleting
the declaration line takes both, rather than pointing at the caret's own offset; keep the "nearest place" advice for
an insertion or a forward deletion, where it moves the caller somewhere new.
[#1042](https://github.com/hexide-io/HexIDE/issues/1042).

**Fixed (#1042).** Refusals now name the header or the member through the same name reader as add-in refusals. A deletion ending at the caret explains that the preceding text belongs to that region; for a member, deleting its whole logical declaration removes its Attribute lines too. Insertions and forward deletions retain a new editable offset. Headless editor regressions cover all three cases and fail against the previous wording.

## `press_key` refuses a Delete over a selection reaching protected lines, where the key deletes the editable part

**Symptom.** Read from the code (#273 task 4.5a records it as already so). With a selection running from code into
the header or a member's `Attribute` lines, a person's Delete deletes the parts the code window's read-only sections
allow. The tool deletes nothing:

```
press_key {"target":".../Custom[Root]/None[TextEditor]","key":"Delete"}
→ {"success":false,"error":"press_key refused: offset N is in the file's header or a member's Attribute lines, ...
   Nothing was written. ..."}
```

`UiAutomationDriver` asks `DeletionChangesARegion` about the whole span, where the key's provider deletes the
deletable segments.

**Workaround.** Narrow the selection to the editable part with `interact set_property` (`SelectionStart`,
`SelectionLength`) before pressing the key.

**Suggested fix.** Delete what the deletable segments allow, as the key does, and say what was left and why.
[#1055](https://github.com/hexide-io/HexIDE/issues/1055).

**Closed (#1055).** Delete and Backspace now reach the real key handler whenever the selection has
editable segments. AvaloniaEdit's selection replacement asks the installed read-only section provider;
no automation code computes the deletion. Replies name the protected remainder and report the caret
and remaining selection. `type_text` uses that replacement API too, placing text in the last deletable
segment as a person's typing does. Wholly protected spans still refuse with #1042's region wording.
Verified by headless code-window integration tests, including unchanged protected text and post-edit selection.


## `set_window_state` replies with the window's state from before the change on Linux — **IMPLEMENTED, live verification pending** (#946)

**Symptom.** Each call followed by `get_window_state {}` (Linux, openSUSE Leap 16 under XWayland, Debug build of
main at b15c7cf, 2026-10-13):

```
set_window_state {"state":"Normal"}
→ {"window":{"state":"Maximized","x":0,"y":32,"width":1920,"height":1011}}
get_window_state {}
→ {"state":"Normal","x":240,"y":171,"width":1440,"height":733}

set_window_state {"state":"Normal","x":100,"y":100,"width":1280,"height":720}
→ position updated, width and height still 1440x733
get_window_state {}
→ 1280x720

set_window_state {"state":"Maximized"}
→ {"window":{"state":"Normal", ...}}
get_window_state {}
→ Maximized, 0,32 1920x1011
```

The reply says `success: true` and carries the old value in the field described as the resulting state. #602's
re-read waits only for HexIDE's own layout, and on Avalonia 12.0.4's X11 backend, which every Linux session uses,
XWayland included, state and geometry change only when the window manager answers. Linux-specific by code
reading: the other backends, Headless included, take the requested value at once, so tests cannot reproduce it.
Read in code and not run, from Maximized: a restore with a size writes the maximised origin back as the position,
and a refused off-screen position cannot undo a restore already sent while the reply says "Nothing was changed."
The archived #602 entry's "read after layout, so a new size is not reported as the old one" holds only where the
platform applies a change at once.

**Workaround.** Ignore the reply's `window` and re-read with `get_window_state {}`.

**Suggested fix.** Wait, bounded at about a second, until the window reports the target state and, when geometry
was passed, a move or resize has arrived; on timeout, reply with what the window reports and a note to re-read.
Check the frame against the screens before any state change, so a refusal never has to undo a restore.
[#946](https://github.com/hexide-io/HexIDE/issues/946).


**Implementation.** State confirmation is bounded at 1 s; geometry follows a confirmed restore and waits up to
a further second for move/resize notifications (no exact-size requirement, since a window manager may clamp).
Unconfirmed requests return `success: false`, observed `window`, and a `note` directing the caller to
`get_window_state`. Geometry is validated before restoring; omitted coordinates are not written back. The no-
window-manager case is reported as unconfirmed too. Headless tests cover delayed confirmation and timeout;
X11/XWayland verification remains for the supervising session.

## `get_document_tabs` doesn't say whether a tab has unsaved changes — **CLOSED** (#1945, 2026-10-25)

**Symptom (2026-10-25, Debug build with #1797).** With #1797's unsaved-changes marker live, a designer tab draws
`Project1 - Form1 (Form) *`, but the reply gives the title without the `*` and has no other field for the state.
After `add_control` (Form1, CommandButton), `interact click` on `ListItem[Command1]` and `press_key Delete`:

```
get_document_tabs {}
→ {"tabs":[{"title":"Project1 - Form1 (Form)","type":"designer","isActive":true,...}]}
```

`take_snapshot` showed the `*`. A client couldn't tell a modified document from a saved one without a screenshot,
and a script that edits and then saves had nothing to assert against.

**Workaround (before the fix).** `take_snapshot`, and read the tab.

**Fixed (#1945).** Each tab that can be saved, in `get_document_tabs` and in `get_open_editors`' `editors` and
`activeEditor`, carries `isModified`, read from the tab's own `IsModified`, which is what the `*` is drawn from. A
carried file has it too, without `project` or `document`. A tool tab, and the read-only project and archive views, have
no `isModified`: they hold nothing to save, and a `false` would read as "saved", which for the project view of a
project never saved would be untrue. The field is per tab, and a form's designer and code tabs can differ, as their
markers do; the description says so. The title is unchanged, so it is still what `activate_document_tab` and
`close_document_tab` match. Neither tool takes an argument, so neither schema has a `required` array.
`DocumentTabsModifiedTests` fail before the fix. The archive view is not among them, since its entries are built only
by reading a real archive file.

**Live (Windows, a Debug build of the fix, the hexide tools from a headless Claude Code session).** On a new
project:

```
get_document_tabs {}                         → Form1's designer, "isModified":false
add_control {"formName":"Form1","type":"CommandButton","x":40,"y":40,"width":81,"height":33}
get_document_tabs {}                         → "isModified":false (add_control saves the form)
interact {"target":"…/ListItem[Command1]","action":"click"}
press_key {"target":"…/ListItem[Command1]","key":"Delete"}
get_document_tabs {}                         → "isModified":true
take_snapshot {"window":"ide"}               → the tab reads "Project1 - Form1 (Form) *"
get_open_editors {}                          → "isModified":true in editors[0] and activeEditor
invoke_menu_item {"path":"View/Object Browser"}
get_document_tabs {}                         → {"title":"Object Browser","type":"tool","isActive":true}
open_file {"name":"Form1"}
type_text {"target":"…/None[TextEditor]","text":"' typed"}
get_document_tabs {}                         → designer true, code true
invoke_menu_item {"path":"File/Save Form1"}
get_document_tabs {}                         → designer false, code false
```

On a second new project, with the project view opened by `interact {"action":"double_click"}` on
`TreeItem[Project1 (Project1.vbp)]`, `get_document_tabs` listed `{"title":"Project1.vbp","type":"code","isActive":true}`,
with no `isModified`, beside Form1's designer with one. It still had none after `add_file` added a module.

## `take_snapshot` of a running form leaves out its title bar without saying so

**Symptom (2026-10-26, #534's first-contact baseline, Windows).** The task was "Make Form1's title bar say Hello when
the program starts, run it, and confirm it does". All three models took a snapshot to confirm it:
- `take_snapshot {"window":"Form1"}` answered `{"path":"…","activeDialog":"Hello","width":456,"height":301,…}`.
- The image is the form's client area, grey and empty, with no title bar.
- Haiku 5.5 reported the PNG "came back blank". Sonnet 5.5 and Opus 5.5 worked out that only the client area was
  captured, and took `activeDialog` as the confirmation.

Neither the description nor the reply says the frame is left out, and the title of a running form, which is no dialog,
comes back in a field named `activeDialog`.

**Workaround.** Read the title from `activeDialog`, or from the root `name` of `dump_visual_tree {"window":"auto",
"maxDepth":1}`.

**Suggested fix.** Say in the description and the reply that the image is the client area, and name the field after
what it holds: the captured window's title. [#2078](https://github.com/hexide-io/HexIDE/issues/2078).

**Fixed (#2078).** The description and reply now identify the capture as the client area, without its title bar
or frame. Read `title` for the captured window's actual title, including the IDE's or an empty caption;
`activeDialog` remains as a compatibility field with its previous meaning. Open popups are still included
unless `note` says otherwise. Native-frame capture remains the separate part 2 gap in `docs/mcp-server-gaps.md` (#1595).

## No call presses on a control and releases elsewhere, so a drag on a running form can't be driven

**Symptom (2026-10-22, #1373, Windows).** Whether dragging off a control cancels its Click is a drag: a press on the
control, moves with the button held, and a release somewhere else. `interact {"target":"<a Label on a running form>",
"action":"click","value":"10,5"}` presses and releases at that one point, and `click`'s own remarks call a drag
"beyond it". No other call raises a pointer press.

**Workaround.** None through the surface. `MouseEventTests` drives the drags headlessly with `MouseDown`, `MouseMove`
and `MouseUp`.

**Suggested fix.** Let `click` take a release point, as in `10,5 to 200,30`: press on the target, move with the button
held, release at the second point to whatever holds the pointer.
[#1402](https://github.com/hexide-io/HexIDE/issues/1402).

**The designer has the same gap (2026-10-26, #2039).** Drawing a control with a tool is a drag too, and which container
it goes into depends on where the drag began. `add_control` places as a drag begun at its top-left would, so the rule was
checked live through it. A drag up and to the left, where the two differ, was driven only headlessly, by
`DrawIntoContainerTests`, through the view's own pointer handlers.


**Fixed (#1402).** `click` accepts `10,5 to 200,30` or `10,5 to <path>:300,190`, with a full
`dump_visual_tree` path (spaces included). Release coordinates are relative to that control, or the target
without a path. Button and keys precede `to`; `double` cannot accompany it. Eight held moves and the release
follow the press's work, routed to the pointer capturer or the hit-tested control. The reply names the press
landing, release point and recipient, marking capture explicitly. Headless tests cover capture routing,
running-form release on/off, ListBox selection and drawing a toolbox control in the designer.
Live verification remains the supervisor's check.
## `invoke_format_command`'s no-op reply states a selection rule the spec contradicts

**Symptom.** With one control selected (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf,
2026-10-13):

```
interact {"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/Pane[#0]/Custom[Root]/Pane/None[FormContainer]/List[ControlsContainer]/ListItem[Command1]","action":"select"}
invoke_format_command {"command":"AlignLefts"}
→ "Nothing changed, and no undo step was added: the selection is Command1. The spacing commands need two or
   more selected controls, the others at least one, and a selection that already satisfies the command is
   left as it is."
```

The spec makes the nine alignment and sizing commands unavailable below two selected controls, and with one they
can never change anything, so "at least one" sends a caller back with the same single selection. The text is
hard-coded and the command table has no per-command minimum. Cross-platform by code reading.

**Workaround.** Before any Align* or MakeSame* command, select two or more controls with `interact` `select` and
then `add_to_selection`. Read "at least one" as true only of SizeToGrid, CenterHorizontally and CenterVertically.

**Suggested fix.** A per-command minimum in the `FormatCommands` table, a `success:false` refusal below it that
names the rule and points to `add_to_selection`, and the no-op note generated from the same table, so the prose
cannot drift from the code again. [#930](https://github.com/hexide-io/HexIDE/issues/930).

**Fixed (#930).** The command table now supplies each minimum to the refusal and no-op note. Below the
minimum, the tool returns `success:false`, names the command and current selection, and directs the caller to
`interact` `select` and `add_to_selection`. Alignment and sizing predicates and execution guards require two
controls; changing selection invalidates command enablement. Headless regression coverage checks all twenty
command rules and the nine predicates. Live menu/toolbar verification remains for the supervising session.
