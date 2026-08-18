# Give containers their children back

## Why

Open VB6's own `Options Dialog.frm` in HexIDE today and you see a form nobody designed. Four PictureBoxes
form the four tab pages, and three of them are parked off the left edge at `Left = -20000` — VB6's trick for
hiding the pages you are not looking at. Their contents are not parked with them, because HexIDE reads every
control's `Left` as a distance from the *form's* edge, while VB6 records a container child's position from
*its container's* edge. Three hidden pages are therefore drawn on top of the visible one.

The developer cannot tidy that up, because the form is read-only. HexIDE's writer would emit those controls
as siblings of their containers, and the refusal gate stops the save. Six of the twenty-two VB6-authored
corpus files are in that state, and they are exactly the six with non-menu nesting.

What makes this urgent rather than untidy is that **VB6 does not reject the flattened file**. Verified
against the real compiler: a `.frm` nesting a control under a class that cannot contain one compiles cleanly,
produces an empty log, and is silently re-parented at a position that follows no single rule. So the damage
would not announce itself — it is outcome 3 in `docs/serialization-outcomes.md`, the worst category, and it
is why the gate exists.

## What changes

Containment becomes real: a parent link on the model, a container that can actually host children at run
time, a designer that knows a child's coordinates are measured from its container, and a writer that emits
nested `Begin` blocks. `FormDefinition.Components` stays flat, because most of its readers want it flat and
two of them — control-array grouping and interpreter name allocation — are *correct* because it is flat.

The coordinate decision, in one sentence: **the model stores what the file stores — container-relative, in
pixels — and the designer computes an accumulated container origin in exactly one place for the consumers
that need an absolute number.** Rebasing to absolute on load was the tempting alternative and it is unsound,
because containers move at run time (`Options Dialog.frm` assigns `picOptions(i).Left` to switch tabs).

## What this does not change, and the number that will not move

This has to be said plainly because it is deflating. Closing the container half **does not make six forms
editable. It makes two** — `Options Dialog.frm` and `Tip of the Day.frm`. The other four are held by a
second defect the gate cannot currently see: they reference `.frx` blobs through properties HexIDE does not
model, so a save drops the reference and the image survives only because a separate guard leaves the
companion file alone. `Web Browser.frm` and `Tip of the Day.frm` both carry `Picture`, and the difference is
whose: `PictureBox.Picture` is modelled, `Label.Picture` is not.

Closing that hole *widens* the gate first — `Button ListBox.frm` and `Mover ListBox.frm` save lossily today
and become honest refusals. So the corpus read-only count goes **6 → 6**, with a different six in it. That
is a burndown of silent corruption, not a regression of editability, and the corpus test has to become an
expected *set* rather than a count before it can express that.

Two other holes are recorded and only one is closed here: the gate does not fire for a control read from
inside a container it cannot model (closed in Phase 2), and `Save As` bypasses the gate entirely
(`ProjectService.cs:471`) so an unreproducible form can already be written today with no warning — recorded,
scoped out, and worth its own issue.

## Scope

Containers are a closed list — Form, PictureBox, Frame. A component nested under anything else, including an
add-in-registered class, keeps the form read-only. Whether the add-in API should be able to declare a
container belongs with `addin-system`, since it means adding a member to a public interface third parties
implement.

Deferred with reasons in `design.md`: `Align` docking, the pre-existing z-order inversion, modelling
`VB.Line`/`VB.Image`, moving a control between containers in the designer, and clipping on the design canvas.

Tracked by #84. The design was produced by a ten-agent investigation of the model, runtime, designer,
serializer, VB6 oracle and existing specs, then adversarially reviewed; both reviewers returned
*needs-revision* and eighteen corrections were folded in, three of them rejected with reasons.
