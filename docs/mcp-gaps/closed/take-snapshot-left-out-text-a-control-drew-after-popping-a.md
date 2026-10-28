# `take_snapshot` left out text a control drew after popping a clip, above 100% scaling — **CLOSED** (#2293, 2026-10-27)

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
