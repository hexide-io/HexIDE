# press_key cannot reach a handler attached to TextArea, which is where the code editor's handlers live — **CLOSED** (2026-09-08)

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
