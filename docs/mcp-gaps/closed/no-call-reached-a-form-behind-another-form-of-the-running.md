# No call reached a form behind another form of the running program — **CLOSED** (#1057, 2026-10-14)

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
