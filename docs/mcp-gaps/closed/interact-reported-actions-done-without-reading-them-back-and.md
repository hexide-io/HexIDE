# `interact` reported actions done without reading them back, and acted where visibility could not be read (#1280)

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
