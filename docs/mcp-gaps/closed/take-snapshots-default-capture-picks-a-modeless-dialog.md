# take_snapshot's default capture picks a modeless dialog (#1264)

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
