# No call pressed or released a pointer, so a running form's MouseDown and MouseUp could not be driven — **CLOSED** (#1033, 2026-10-14)

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
