# VB6 designer geometry — what the oracle actually says

Companion to [`vb6-fidelity-oracle.md`](vb6-fidelity-oracle.md), which records **runtime language**
semantics — what an expression evaluates to, which error a statement raises. This file records what the
designer file format and the designer itself do with **geometry and structure**: containers, coordinates,
units, z-order and tab order. The two are separate documents because they are answered by different
instruments. Runtime semantics come from compiling and running code; the facts below come from reading the
shipped `.frm` corpus, and from `GetWindowRect` against a compiled binary.

Everything here was measured or read. Where something was not established, it says so — those are the
questions to put to the oracle before anything depends on the answer.

## Containers

**The container list is closed, and it is three classes.** `VB.Form`, `VB.PictureBox` and `VB.Frame`.
The two other designer roots, `VB.UserControl` and `VB.PropertyPage`, behave as a form's root does.
Nothing else in the base control set holds controls.

**VB6 accepts illegal nesting without complaint.** The file format permits writing a control inside a class
that cannot hold one, and VB6 loads such a file, silently relocating the control. It does not fail loudly,
which is precisely what makes a flattening save dangerous: the result opens, and the form is quietly a
different form. This is the case the refusal gate still exists for.

**A container can be an element of a control array.** `ODBC Log In.frm` has a `Frame fraStep3` carrying
`Index = 0`. `Options Dialog.frm` has four sibling `picOptions`, and
`Treeview Listview Splitter.frm` has a two-element `lblTitle` array living entirely inside one `picTitles`
— so a control array spans containers, and containment cannot be keyed by name.

**Whether `TabDlg.SSTab` is a container is unresolved.** It is the commonest third-party container in real
VB6 code, and the probe was blocked by an OCX registry failure. So is `MDIForm`, which appears nowhere in
the corpus.

## Coordinates and units

**`Left`, `Top`, `Width` and `Height` are always persisted in twips**, whatever the container's `ScaleMode`
is. The scale affects what a *running program* sees through those properties, not what the file stores.

**A child's position is measured from its container's client origin.** Measured with `GetWindowRect`
against a compiled binary:

| Container | Client inset |
|---|---|
| Frame, with or without a caption | `(0, 0)` |
| PictureBox, default (3-D, Fixed Single) | `(2, 2)` px — 30 twips |
| PictureBox, flat and borderless | `(0, 0)` |
| PictureBox, flat **with** a border | **not measured** |

**The general rule is `(Width − ScaleWidth) / 2`**, and it agrees with the table. `Tip of the Day.frm`'s
`Picture1` is `Width = 3735` against `ScaleWidth = 3675` — a 60-twip difference, 2 px per side — with no
`BorderStyle` line at all, because VB6 omits default-valued properties and a PictureBox defaults to
`1 - Fixed Single`. That last point is easy to get backwards: **a PictureBox with no `BorderStyle` line is
bordered.**

**A Frame has no `Scale*` properties at all** — reading one raises error 438, and an `awk` scan of every
`VB.Frame` block across all 35 VB98 designer files finds zero `Scale*` lines. Its inset is structurally
always zero.

**`VB.Line`'s `X1`/`X2`/`Y1`/`Y2` are persisted in the container's `ScaleMode` units**, not in twips —
which is why a container's scale is content rather than decoration. `About Dialog.frm:48-55` measures
against `ScaleHeight 2453.724` on a form whose `ClientHeight` is `3555`; `WIZARD.FRM:396-411` measures in
twips because its `picNav` is twips-scaled. This is a different geometry family from `Left`/`Top`, and a
transform that treats them alike corrupts one of them.

## Order

**`Begin` order is z-order**, and there is a separate windowless plane: graphical controls (`Label`,
`Shape`, `Line`, `Image`) draw beneath windowed ones regardless of file order. HexIDE's canvas order is
currently the inverse of the file's, which is a pre-existing defect recorded separately rather than
something this document endorses.

**`TabIndex` is one flat sequence per form**, not one per container, and VB6 renumbers it to `0..N−1` on
load — verified by authoring `0,0,9,9,4,(absent)` and reading back `0,1,2,3,4,5`. Whether VB6 also
*traverses* strictly by `TabIndex` across container boundaries is a separate question the renumbering
evidence does not answer, and it has not been established.

## Corpus notes

The `VB98\Template` tree holds **22** designer files (`.frm`/`.ctl`). Two of them — `ADDIN.FRM` and
`FRMDATEN.FRM` — are **uppercase**, so a case-sensitive scan finds 20 and quietly reports a different
denominator than every count in this project's documents.

The Wizards corpus contains two `ScaleMode = 3 'Pixel` forms, which is where a twips-only assumption shows
up first.

## Oracle-environment facts

Three things that cost time before they were written down:

- **Registering the sample OCXs fails** on a modern machine, which blocks any probe needing `MSComctlLib`,
  `TabDlg` or `SHDocVwCtl`.
- **A `.vbp` must be CRLF.** `vb6.exe` will not read one with LF line endings.
- **`/make` writes its log per form**, not per project, so a failure is reported in a file named after the
  form rather than in the build output the caller was watching.

## Still to ask the oracle

None of these were established, and nothing should depend on a guess at them:

- Does the designer re-parent a control dragged over a container, or only through cut-and-paste?
- Does VB6 scope marquee selection to the container the drag began in, and can a selection ever hold a
  container together with its contents?
- Does a `Timer` inside a hidden `Frame` keep firing, and can a control inside one take focus?
- Does clicking a control inside a `Frame` raise the Frame's `Click` as well?
- Does VB6 tab strictly by `TabIndex` across container boundaries, or traverse containers as units?
- What is the client inset for `Appearance = 0` with `BorderStyle = 1`?
- Are a container's `Scale*` values authoritative on load, or recomputed from `Width`/`Height` on save?
- Does `Load` on a control-array element that is a container clone the element's contents?
- Does a container clip a child that overhangs it at run time? `Splash Screen.frm` has `lblWarning` at
  `Left = 150`, `Width = 6855` inside a 7080-wide Frame — a 75-twip margin.
- Does re-saving a container form in VB6's own IDE renumber `TabIndex` in the file?
- Is `MDIForm` a container, and with what restriction?
