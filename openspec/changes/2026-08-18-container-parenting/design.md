## What is actually lost

Open `Options Dialog.frm` — the tabbed options dialog Microsoft ships with VB6 — in HexIDE today and you see
a form nobody designed. Four picture boxes make up the four tab pages, and three of them are parked off the
left edge of the form at `Left = -20000` (`Options Dialog.frm:23`, `:43`, `:63`). Their contents are not
parked with them, because HexIDE reads every control's `Left` as a distance from the form's left edge, and a
control inside one of those picture boxes records a small positive offset from *its container's* left edge
instead. So three hidden pages of the dialog are drawn on top of the one visible page, overlapping it and
each other. The developer sees a jumble, and any arrangement they do around that jumble is done against a
false picture.

Then they cannot do the arrangement anyway, because the form is held read-only. HexIDE's writer would emit
those controls as siblings of the containers rather than inside them, and the refusal gate at
`FormDeserializer.cs:316-318` correctly refuses to let a save happen at all. Six of the twenty-two
VB6-authored files in the corpus are in that state, and they are exactly the six that contain non-menu
nesting: `Options Dialog.frm`, `Splash Screen.frm`, `ODBC Log In.frm`, `Tip of the Day.frm`,
`Web Browser.frm` and `Treeview Listview Splitter.frm`.

The stakes on the writing side are higher than they look, because VB6 will not protect anyone.
Machine-verified: a `.frm` that nests a control under a class that cannot contain one compiles cleanly,
produces an empty `<FormName>.log`, and is silently re-parented to the nearest real container at a position
that does not follow any single rule. VB6 does not reject HexIDE's flattened output. It loads it and renders
it wrong. That is outcome 3 in `docs/serialization-outcomes.md` — the worst category — and it is the reason
the gate exists.

Three corrections to the premise before going further, two of them to this design's own earlier draft.

**The issue text has the classes the wrong way round.** In `Options Dialog.frm` it is the **PictureBox**
`picOptions` that is parked at `Left = -20000` (`:19`, `:23`) and the **Frame** `fraSample4` that sits inside
it at `Left = 2100` (`:30`, `:33`). The substance is unaffected and if anything strengthened, because that
file is a container inside a container and both levels of the transform have to be right before anything
renders correctly.

**A container form is not "permanently dirty", and the earlier draft was wrong to say so.** `IsDirty` does
re-serialise the form (`ProjectService.cs:1115-1122`), but it compares the result against a *render*
baseline, not against the bytes on disk: `BaselineMatches` (`:1152-1153`) looks the path up in
`renderBaselines`, which `SnapshotRenderBaseline` (`:1188-1194`) fills with the hash of what the serializer
produced at load time. An untouched form therefore renders to exactly its baseline and reports clean, which
is precisely the contract `file-watcher/spec.md:73-91` states. There is no phantom Save prompt, and the
"offers to save changes the save path then declines" claim goes with it. What *is* real is that `Save As` is
not gated at all — `if (!form.CanSaveFaithfully && !saveAs)` at `ProjectService.cs:471` lets it through — so
the flattened file can already be written today, with no warning.

**The corpus really is 22 files, and the earlier draft's open question about it is answered and wrong.** A
case-sensitive `find` over `VB98\Template` returns 18 `.frm`, which is where the "20" came from; a
case-insensitive one returns 20, because `Projects\ADDIN.FRM` and `Projects\FRMDATEN.FRM` are uppercase.
With `Userctls\Colorful Control.ctl` and `Control Events.ctl` that is 22, exactly the figure in
`serialization-round-trip/spec.md:17-29`, and exactly what `Directory.EnumerateFiles(root, "*.frm")` finds
on Windows because that API is case-insensitive. Both reviewers were right and the design was wrong. The
question is struck.

## What this change actually frees, and the number that will not move

This has to be said before anything else, because the honest answer is deflating and it changes how the
work should be judged.

Closing the container half of the gate does not, on its own, make six forms editable. It makes **two**
editable — `Options Dialog.frm` and `Tip of the Day.frm`. The other four are held by a second defect that
the gate does not currently see at all, and un-gating them without fixing that defect would convert a
refusal into a corruption. The corpus read-only count therefore goes 6 → 6, with a different set of six
forms in it and two more forms moving from "saves lossily" into it. That is a burndown of *corruption*, not
a regression of editability, and the corpus test has to be re-expressed as an expected set rather than a
count before it can say so.

The second defect is set out under "The gate's missing half" below. It is a prerequisite, not a nicety.

## The coordinate decision

**The in-memory model stores container-relative coordinates, in pixels, exactly as the file expresses them.
No layer rebases them on load. At run time a container hosts its children in its own canvas placed at the
container's client origin, so the stored number is written through unchanged. In the designer the flat
canvas needs an absolute number, so the accumulated container origin is computed — in exactly one place —
and every consumer that needs to cross the boundary asks that one place for it.**

The earlier draft phrased the second half as "the designer's view-model is the single place that translates
to form-absolute", and that was too strong in a way that hid two real defects. Translation is not confined
to a property getter: the resize adorner clamps and snaps in canvas space, and both operations are
space-dependent. What can honestly be confined to one place is the *origin function*. The consumers that
need it — the getter, the setter, the snap, the clamp — read it from there rather than each deriving it.

### The model stays relative

The file is relative and stays relative. VB6's persistence rule was established against the real product: a
container child's `Left`/`Top`/`Width`/`Height` are written in the `.frm` **always in twips**, measured from
the container's client origin, and converted to the container's `ScaleMode` units only when running code
reads the property. Microsoft's own `Colorful Control.ctl` proves it — a `ScaleMode = 3 'Pixel` UserControl
320 units wide (`:11-12`) holds a `VB.Shape` persisted at `Width = 1575` (`:21`), which is 105 pixels and
cannot be 1575 pixels — and an oracle run reproduced it directly: a label persisted at `Left = 1500` inside
a pixel-scaled PictureBox reads back as `Left = 100` at run time, and reads `1500` again once re-parented to
the twips form. `About Dialog.frm` gives a second, independent confirmation: the form is
`ScaleMode = 0 'User` with `ScaleWidth = 5380.766` against `ClientWidth = 5730` twips (`:5-15`), yet
`cmdSysInfo` sits at `Left = 4245`, `Width = 1245` (`:40-47`) — a right edge of 5505, which fits inside 5730
twips and does not fit inside 5380.766 user units.

So `FormDeserializer.cs:206-217`'s unconditional divide-by-15 is **correct for children and must stay
unconditional**. A container's `ScaleMode` must never be applied to a child's `Left`/`Top`/`Width`/`Height`.
That is the single most counter-intuitive fact in this issue and it removes a large amount of work that
would otherwise be necessary.

Rebasing to absolute on load was the tempting alternative and it is unsound, for a reason that has nothing
to do with the file format: containers move at run time. `Options Dialog.frm:216-224` switches tabs by
assigning `picOptions(i).Left = 210` and `= -20000` in `tbsOptions_Click`, and in VB6 an `Align = 1/2`
PictureBox has its position and width overridden by the runtime entirely. An absolute child coordinate is
only valid for the instant it was computed. The offset has to be stored relative and resolved late.

One caveat the reviewers were right to press: `Align` is **modelled but unhonoured** in HexIDE. It is
declared at `VBProperties.cs:54` and listed first in `PictureBoxComponentClass.cs:13`, and it is read
nowhere — `VBLoader.SpawnComponents` writes the stored `Left`/`Top` unconditionally (`VBLoader.cs:57-58`).
So the Align argument is evidence about VB6, which is what the argument needs, and not evidence about
HexIDE. See "the run-time form is still not correct" below.

It also happens to be what the model already claims. `VBProperties.cs:12` describes `Left` as "The distance
from the inside-left of the container to the control's left edge", and `:14` says the same for `Top`. The
property grid reads the model directly (`PropertiesToolViewModel.cs:111`), so making the model honest makes
the property grid show the VB6-correct value for free — a bug fix against a declared contract rather than a
semantic change.

### The runtime hosts children at the container's client origin

`AvaloniaInteroperability.cs:99-100` binds VB6's `Left` and `Top` literally to `Canvas.GetLeft`/`SetLeft`.
That single line decides the runtime design: if a container hosts its children in its own nested Canvas
*positioned at the container's client origin*, VB6's container-relative semantics fall out for nothing. The
loader writes the raw stored value through, and `Text1.Left` inside `Frame1` reads back the number VB6 would
report in a twips-scaled container.

The earlier draft said the runtime "does no arithmetic at all", and the adversarial reviewer is right that
this is false as written for both container implementations, though the fix is not the one the reviewer
proposed.

`VBFrame` sets `StyleKeyOverride => typeof(HeaderedContentControl)` (`VBFrame.cs:8`) and the repository
defines no `ControlTheme` for it, so its `Content` is presented by SimpleTheme's stock
`HeaderedContentControl` template — a `ContentPresenter` inset by the border and pushed below the header.
`Canvas.SetLeft(child, storedValue)` inside *that* presenter does not put the child at the container's
origin, and the offset it does apply is a property of whichever theme happens to be loaded.

`VBPictureBox` is worse. Its `ControlTheme` (`Resources.axaml:113-125`) hardcodes
`ClassicBorderDecorator BorderStyle="Sunken" BorderThickness="2"` and never consults `BorderStyle` or
`Appearance`. So the *drawn* border is always 2px, while this design's inset table computes 0 for a flat or
borderless box — and both real PictureBox containers in the gated set are exactly that case
(`Options Dialog.frm:20` `BorderStyle = 0 'None`; `Treeview Listview Splitter.frm:15-16`
`Appearance = 0 'Flat` / `BorderStyle = 0 'None`). Designer and runtime would disagree by 2px on the
commonest container in the set, in the opposite direction to the one the earlier draft warned about.

The resolution is to make the chrome and the inset the same number rather than to add compensating
arithmetic:

- `VBFrame` gets its own `ControlTheme`, whose template is a `Panel` holding the group-box border, the
  caption, and a named child host `Canvas` that fills the control's outer bounds. That matches the measured
  VB6 fact — a Frame's client origin is its outer origin, `(0,0)`, for both the captioned and borderless
  cases — which is *only* expressible if the host is at the outer bounds. Losing SimpleTheme's free
  group-box chrome is the price; it is a small template and `ClassicBorderDecorator` already provides the
  etched border everything else in `Resources.axaml` uses.
- `VBPictureBox`'s `ControlTheme` drives its `ClassicBorderDecorator`'s `BorderStyle`/`BorderThickness` from
  the modelled `BorderStyle`/`Appearance`, and the child host sits *inside* the decorator. The inset is then
  the border thickness by construction, and the number the designer computes is the number the runtime draws.

So the runtime still does no per-child arithmetic. The arithmetic that exists lives once, in the container's
own template, where it is also the thing being drawn.

### The child host is owned by the component class, not looked up by name

The earlier draft proposed reaching the host through a named template part. That cannot work and would
silently lose children, for two reasons the adversarial reviewer identified and both of which check out.

`VBLoader.SpawnComponents` instantiates controls and adds them to a Canvas that is not yet in any visual
tree — `window.Content` is assigned afterwards, at `VBLoader.cs:151`. Avalonia applies a template during
`MeasureCore`, so at load time a `NameScope` lookup for the part returns null. And when the template *is*
re-applied later — a `Visible` toggle, a theme change; `VBProperties.VisibleProperty` maps to
`Visual.IsVisibleProperty` at `AvaloniaInteroperability.cs:105` — a new part instance is created and
whatever the loader put in the old one is orphaned.

So the host is an object the component class creates and owns: a `Canvas` exposed as a styled property
(`ChildHost`) on `VBFrame` and `VBPictureBox`, which the template presents. The loader gets it from the
component class, never from a name scope, and template re-application re-presents the same instance.

The accessor itself goes on `ComponentBaseClass` (HexIDE.Runtime), beside `InstantiateInternal` — **not** on
`IComponentClass`. `HexIDE.Core.csproj` declares "Framework-agnostic abstractions… Zero third-party
dependencies" and carries no Avalonia reference, and `IComponentClass.cs:9-24` exposes no framework types; a
member returning an Avalonia `Canvas` would break that and the build (`TreatWarningsAsErrors` is on). The
containment link itself *does* belong on `ComponentInstance`, which is a Core type. The two live in
different assemblies and that is fine — the link is data, the host is a control.

### The designer computes the origin in exactly one place

The designer cannot have a real visual tree yet, for reasons set out under "what the designer must not learn
yet". So it keeps one flat Canvas and does the arithmetic, in `ComponentInstanceViewModel`
(`ComponentInstanceViewModel.cs:58-68`):

- `Left`/`Top` become computed — the getter adds the accumulated origin of every container above, the setter
  subtracts it before storing;
- a new read-only `ContainerBounds` exposes that same accumulated origin plus the container's size, as a
  `Rect` in canvas space, for the consumers that cannot go through the getter.

Everything that only reads or writes an absolute position continues to work unedited: the
`(Canvas.Left)`/`(Canvas.Top)` TwoWay style setters at `FormEditView.axaml:250-251`, the drag write-back
through `ResizeAdorner.axaml.cs:172-175`, the marquee's `new Rect(item.Left, item.Top, …)` at
`FormEditView.axaml.cs:205`, and all sixteen align/space commands at `FormEditViewModel.cs:291-523`.

Three things do not, and each is a real defect the earlier draft missed.

**Nothing notifies a descendant when its container moves.** `InstanceOnOnComponentPropertyChanged`
(`ComponentInstanceViewModel.cs:38-56`) raises `PropertyChanged` only for the instance whose own property
changed. Once `Left` accumulates the parent chain, moving a Frame changes every descendant's absolute
position with no notification at all: the TwoWay `(Canvas.Left)` setter never fires, so the children stay
drawn where they were, while the marquee and the align commands read the new value. Selection rectangles
stop matching what is painted and the children snap into place only on the next reload. Both reviewers found
this independently and both are right. The ordered child list gives the walk, so the fix is a fan-out: when
a container's `Left`/`Top` changes, raise `Left`, `Top` and `ContainerBounds` on its whole descendant
subtree. That fan-out is not an implementation detail of the getter — it is the half of the mechanism that
makes flat-canvas-with-computed-positions render at all.

**Snapping happens in the wrong space.** `ResizeAdorner` snaps the absolute canvas coordinate
(`ResizeAdorner.axaml.cs:126-128`, `:156-157`) via `SnapGridUtils.SnapToGrid` (`SnapGrid.cs:16-25`), and the
grid is 8 px = 120 twips (`SettingsDefaults.cs:26-27`). Container origins are not grid multiples, so
absolute and relative snapping cannot both hold. Worked end to end on the form this issue is named for: a
child at `Left = 2100` twips inside `picOptions` at `Left = -20000` twips loads as model 140 px inside a
container at −1333.3333 px, so the designer getter yields −1193.3333 px. Drag it 10 twips and snap:
`(int)(-1192.6667)` is −1192, `-1192 / 8 * 8` is −1192. The setter stores `-1192 + 1333.3333` = 141.3333 px,
and `FormSerializer.ToTwips` (`:125-126`) rounds `141.3333… × 15` to **2120** twips. The float arithmetic is
exact — the six-decimal rounding absorbs about 1e-13 px of noise robustly, not luckily, which corrects the
earlier draft's claim that byte-identity survives "by accident of an unrelated fix" — but 2120 is not a
multiple of 120, so the control has been knocked off its own container's grid, and repeated drags never
settle. VB6 snaps to the container's grid, which is drawn in the container. So the snap subtracts
`ContainerBounds.Position` before snapping and adds it back, in the adorner, reading the origin from the one
place that computes it.

**The clamp throws on a negative origin.** `ResizeAdorner.axaml.cs:138` is
`Math.Clamp(SnapToGrid(originalOrigin.X + diff.X), 0, originalRight)` and `:150` is the same for `Top`. For a
child of a container parked at −20000 twips the computed absolute origin is about −1193 px, so
`originalRight` is negative too — `min` exceeds `max` and `Math.Clamp` throws `ArgumentException`. A
left-edge or top-edge resize of anything inside an off-screen container crashes the designer. Even where it
does not throw, clamping to absolute 0 is wrong: it pins the control to the form's left edge, storing a
relative `Left` of +20000 twips. The clamp is against the container's client rect, not against canvas zero —
which means the adorner learns the container too, and is exactly why "one translation site" had to become
"one origin site". This is unreachable today only because the four `picOptions` containers live on a
read-only form; Phase 6 makes it reachable.

Making the *view-model* relative instead, and rebasing its consumers, was the alternative. It is correct
end-to-end and it is roughly thirty edits, including the marquee, all sixteen align/space commands, the drag
write-back and two shipped MCP tool surfaces whose meaning would change silently. Making the model absolute
would have broken the run-time container move. The boundary is the right place.

### The accumulated origin, and the inset

A child's origin is the container's **client** top-left. Measured with `GetWindowRect` against a compiled
VB6 binary: a captioned Frame insets by `(0,0)`, a borderless Frame by `(0,0)`, a default 3-D PictureBox by
exactly `(2,2)` pixels — 30 twips — and a flat borderless PictureBox by `(0,0)`. A Frame has no `Scale*`
properties at all (error 438 on every one of them), and an `awk` scan of every `VB.Frame` block across all
35 VB98 designer files finds zero `Scale*` lines, so a Frame's inset is structurally always zero.

For a PictureBox the general rule is `(Width − ScaleWidth) / 2`, which is what the measurements show. But
`ScaleWidth`/`ScaleHeight` are discarded at load for every component class (`FormDeserializer.cs:143-144`)
and re-emitted only for the form (`FormSerializer.cs:132-138`), and where the container's `ScaleMode` is not
twips the subtraction is not even dimensionally meaningful — `picTitles` in
`Treeview Listview Splitter.frm:20-22` carries `ScaleMode = 0 'User` with `ScaleWidth = 5797.147` against
`Width = 5676`.

So the implementation reads the inset from `BorderStyle` and `Appearance`, both already modelled on
`PictureBoxComponentClass` (`:14-15`, declared at `VBProperties.cs:42`, `:46`): 2 px per side for a bordered
PictureBox, zero for a flat or borderless one, zero for a Frame. `(Width − ScaleWidth) / 2` becomes a **test
assertion** rather than the implementation — where `ScaleMode` is absent or twips and `ScaleWidth` is
present, the two must agree. `Tip of the Day.frm:31-40` is the live confirmation: a PictureBox with no
`BorderStyle` and no `Appearance` line, `Width = 3735`, `ScaleWidth = 3675` — a 60-twip difference, 2 px per
side, exactly as predicted.

That same file exposes the defect. `VBProperties.cs:46` gives `BorderStyleProperty` a default of
`VBBorder.None`, but VB6's PictureBox default is `1 - Fixed Single`, and VB6 omits default-valued properties
from the `.frm`. `Picture1` therefore reads as borderless in HexIDE and would be inset by zero where VB6
insets by two pixels. The correction needs no new machinery — `PropertyClass.OverrideDefault<TComponent>`
already exists (`PropertyClass.cs:159-163`) and is already used four times, and `GetPropertyOrDefault` routes
through `DefaultValue(BaseClass)` — so it is a one-liner. It has one consequence worth naming so it is not
mistaken for a rendering regression: the property grid reads `GetBoxedPropertyOrDefault`
(`PropertiesToolViewModel.cs:111`), so every PictureBox with no `BorderStyle` line will start showing
`1 - Fixed Single`, which is what VB6 shows.

`Scale*` preservation is not merely a tidiness item. `ScaleMode` is **not** in `SpecialCasedPropertyNames`
(`FormDeserializer.cs:40-44`), so it falls through to `UnknownRawPropertyLines` and is written back
verbatim, while `ScaleWidth`/`ScaleHeight` are dropped. Today's output therefore already emits
`ScaleMode = 0 'User` with no `Scale*` at all, on `About Dialog.frm`'s `picIcon` and on
`Treeview Listview Splitter.frm`'s `picTitles` and `picSplitter`. That is C4 in
`docs/serialization-fidelity-2026-08.md` and it is a live defect, not a hypothetical — which also answers
the earlier draft's open question about whether the drop is "merely lossy". It is worse than lossy on any
form whose geometry depends on the scale, which brings us to the family the transform must not confuse.

### The two geometry families, and why they are not the same family

`VB.Line` children carry `X1`/`X2`/`Y1`/`Y2` rather than `Left`/`Top`. The earlier draft said these are
"container-relative too", implying the same rule. They are container-relative, and they are **not in the
same units**, and that matters.

`About Dialog.frm:48-55` has `Line1` with `X1 = 84.515`, `X2 = 5309.398`, `Y1 = Y2 = 1687.583`. The form is
`ScaleMode = 0 'User` with `ScaleHeight = 2453.724` against `ClientHeight = 3555` twips. `lblDescription`
(`:57-64`) runs from `Top = 1125` to 2295 twips and `lblDisclaimer` (`:91-98`) starts at `Top = 2625`, so the
separator must fall between them. Read as twips, `Y1 = 1687.583` lands inside the description text — wrong.
Read as user units and scaled by 3555/2453.724, it lands at 2445 twips — neatly between the two. So a
`VB.Line`'s coordinates are in the **container's `ScaleMode` units**, while `Left`/`Top`/`Width`/`Height` are
always twips. `WIZARD.FRM:396-411` is consistent rather than contradictory: its two `VB.Line` inside `picNav`
carry `X1 = 108`, `X2 = 7012`, and `picNav` (`:319-337`) has `ScaleWidth = 7155` equal to its `Width` with no
`ScaleMode` line, so its scale *is* twips.

Two consequences. First, whatever applies the container origin must be keyed on the geometry family, not on
`Left` alone, and the two families must not share a transform. Second, this strengthens the case for
preserving `Scale*`: a container's scale is load-bearing for its `VB.Line` children's meaning, so dropping it
is not a cosmetic byte difference. `VB.Line` is not modelled at all, so within this change it is preserved
verbatim inside its container rather than transformed — but the transform's shape must not assume one
family, and the design must not claim it has verified a rule it has not.

## FormDefinition.Components, and who actually reads it

The issue's "~127 call sites reading `FormDefinition.Components`" does not survive contact. Filtering the
raw `.Components` hits for the namespaces `HexIDE.Runtime.Components` / `HexIDE.Core.Components` and for the
three unrelated collections (`ToolBoxToolViewModel.Components`, `ComponentRegistry.Components`,
`FormEditViewModel.Components`) leaves **about twenty** code sites that actually read a `FormDefinition`'s
component list — `CodeEditorViewModel.cs:260`/`:371`, `ProjectRunnerService.cs:321`,
`ProjectService.cs:521`, `FormLayoutToolViewModel.cs:69-70`, `FormEditViewModel.cs:150`/`:177`,
`HexIdeTools.cs:213`/`:270`, `FormSerializer.cs:37`/`:50`/`:67`/`:88`, `VBLoader.cs:36`/`:52`/`:120`/`:139`,
`Standalone/Program.cs:88`, and the tests.

They split cleanly: most want a flat list of every control, four want top-level-only, two want the tree, two
are indifferent. Which is why:

**`FormDefinition.Components` does not change at all.** It stays a flat list containing every component
including the form, in pre-order depth-first document order, with the form at index 0 and remaining the only
`FormComponentClass` entry. The parent/child link is purely additive to the list's *contents*; no consumer
needs the order changed, because the deserializer adds a component (`FormDeserializer.cs:111`) before
recursing into its children (`:306-307`), so a container's children are already contiguous and immediately
after it.

Keeping it flat is not a workaround, it is the correct VB6 model in three places. `VBLoader.cs:35-50` groups
control arrays by shared name across the whole form, and VB6 control arrays genuinely span containers —
`ODBC Log In.frm:35-42` has a `Frame fraStep3` that is itself array element `Index = 0`, and
`Treeview Listview Splitter.frm:27-46` has a two-element `lblTitle` array entirely *inside* `picTitles`.
`VBLoader.cs:77` allocates every control as a form-level interpreter variable, which is correct because VB6
form-module scope is flat: `Text1` inside `Frame1` is still `Text1`. And `TabIndex` is a single flat
form-wide sequence that VB6 renumbers to 0..N−1 on load — verified by authoring `0,0,9,9,4,(absent)` and
reading back `0,1,2,3,4,5`.

Making the list top-level-only would also break something silent and expensive. Blob collection walks the
flat list (`FormSerializer.cs:29-38`) and the blob-reference write has no else branch (`:174-185`); a nested
control's `Picture`/`Icon`/`MouseIcon` would simply vanish from the `.frm` with no diagnostic while the
`.frx` was preserved, and `WouldLoseBlobs` (`ProjectService.cs:928-941`) would then leave the companion file
untouched beside a picture-less form.

The four top-level-only sites gain a one-line filter. Two are the placement loops (`VBLoader.cs:52-63` and
the designer spawner at `:120-132`); the writer's root loop already has the mechanism, in `claimedByAParent`
(`FormSerializer.cs:66-72`), a `HashSet<ComponentInstance>` that works because `ComponentInstance` has no
equality overrides and so uses reference equality — the type has one `partial class ComponentInstance`
declaration and no partial part elsewhere.

One thing the earlier draft dismissed too fast. `TabIndex` needs no change *in the file*, and the writer's
flat sequence stays flat — but the **runtime** consequence is the opposite of "no change whatsoever".
`ComponentBaseClass.Instantiate` sets `KeyboardNavigation.SetTabIndex(control, …)` per control
(`ComponentBaseClass.cs:50`), and Avalonia resolves `TabIndex` among siblings within a navigation scope and
then descends. Today every control is a sibling on one Canvas, so the flat file order and the flat visual
order coincide and VB6's behaviour falls out for free. The moment children move into a Frame's own Canvas
they stop being siblings of the form-level controls: `ODBC Log In.frm` would tab `cmdCancel`(13),
`cmdOK`(12), then `fraStep3`(14) and only then descend into its children 0–11, where VB6 tabs 0–11 first.
That hits all six gated forms. The completeness reviewer is right, and it is ruled out on the wrong
evidence. The requirement is that the running form's tab order stays the flat form-wide `TabIndex` sequence
regardless of containment; the mechanism is `KeyboardNavigation.TabNavigation = Continue` on each host
canvas so a container is not a navigation scope, pinned by a runtime test on `ODBC Log In.frm`'s ordering.
Whether VB6 truly tabs strictly by `TabIndex` across container boundaries is listed as an oracle question —
the load-time renumbering evidence is about the file, not about traversal.

## What holds the link

A typed member on `ComponentInstance`: an ordered `ContainedControls` list on the parent, with a derived
`Container` back-pointer on the child, both maintained by a single mutator that enforces one parent per child
and rejects a cycle.

Ordered, because sibling order **is** the z-order and is load-bearing. Both directions, because the
back-pointer makes the writer's root filter and the designer's origin walk O(depth) rather than O(n), and the
child list is what carries order. Two things to keep in sync is a real cost; it is paid once, in the mutator,
and the undo commands carry the pair.

Not in the property bag, which is where #83 put the menu tree. `GetAllSetProperties()` is a shallow copy
(`ComponentInstance.cs:98-99`), `DesignerClipboard.cs:20` captures it wholesale, and `PasteControls` replays
every entry through `SetUntypedProperty` (`FormEditViewModel.cs:617-625`). Copy-pasting a Frame would hand
the clone the *same* `List<ComponentInstance>` object, holding the *original's* children. Menus escape this
only because a menu is never a canvas selection — the trap is already latent there and containers walk
straight into it.

Not on `IComponentClass.Properties` either, or `FormSerializer.cs:153`'s property loop hands a
`List<ComponentInstance>` to `VbFrmFormatSerializer`'s `WriteProperty`, and `PropertiesToolViewModel.cs:109`
renders it in the property grid. `PropertyCategory.Internal` is no defence — nothing in production code
filters on it.

And not `MenuComponentClass.SubItemsProperty`. `FormSerializer.cs:69` and `:81` probe `SubItems` on **every**
component regardless of class, so reusing it would make a Frame writable as a menu item the moment the load
path populated it. Menus keep their existing mechanism unchanged; the writer gets one `ChildrenOf(instance)`
helper that returns sub-items for a menu and contained controls otherwise. Unifying the two is a separate
refactor that would have to move the menu tree off the property bag as well.

Keyed by object reference throughout, never by name: `Options Dialog.frm` has four sibling controls called
`picOptions` (`:19`, `:39`, `:59`, `:79`) and `Treeview Listview Splitter.frm` has two `lblTitle` inside the
same `picTitles` (`:27`, `:37`). A `Container = "picOptions"` string field is unsound against Microsoft's own
templates.

The load side is nearly free: `LoadRecur` already computes the parent for every component and already throws
it away for everything except menus (`FormDeserializer.cs:92`, `:113-124`, `:129-130`). Recording it is a
two-line change at the point where `parent` is already in hand.

**Menus are in this list too, and every new walk has to know it.** `FormEditViewModel.cs:150` and `:177` add
every non-form component to the designer's `Components` collection, menus included, and
`MenuComponentClass.InstantiateInternal` returns a real `MenuItem` (`MenuComponentClass.cs:16-26`), so a
loaded form's menu items are already zero-sized `ControlItem`s parked at (0,0) on the design canvas — the
symptom `docs/serialization-fidelity-2026-08.md` records as C10. The tree rebuild in the
`ApplyAllUnsavedChangesEvent` handler and the sibling-scoped z-order commands must treat a menu as never a
canvas sibling, or a `SendToBack` on a control will reorder against a menu item and the write-back will
reorder the menu tree.

## The gate's missing half: blob loss

This is the stop-ship, and it is the reason the read-only count will not fall.

`FormDefinition.cs:113` is `CanSaveFaithfully => UnfaithfulSaveReason is null` and nothing else feeds it.
`HasUnmodelledBinaryProperties` (`:125`) has exactly one production consumer — `ProjectService.cs:930`,
inside `WouldLoseBlobs` — and all that decides is whether to leave the **companion** file alone. Nothing
stops the `.frm` itself being written with the property gone. And it will be gone: `ReconstructRawSubtree`
skips any raw property group containing an `.frx` reference (`FormDeserializer.cs:344-345`), and modelled
controls drop unknown `.frx`-referencing properties at `:294-302`.

Checked against the real corpus:

- `Web Browser.frm:116-153` is an `MSComctlLib.ImageList` whose entire `BeginProperty Images` block holds all
  six `Picture = "Web Browser.frx":XXXX` lines as one raw group. A save emits the `ImageList` `Begin` with no
  images at all and every toolbar icon is destroyed beside an intact but now-unreferenced `.frx`.
- `Splash Screen.frm:25-32` loses `imgLogo`'s `Picture`.
- `ODBC Log In.frm:64-74` loses `cboDSNList`'s `ItemData` and `List`.
- `Treeview Listview Splitter.frm:90-93` loses `imgSplitter`'s `MouseIcon`.

That is outcome 3, on this design's own acceptance fixtures, produced by the very phase whose purpose is to
remove a gate. So `HasUnmodelledBinaryProperties` must feed an `UnfaithfulSaveReason` of its own **before**
the container gate opens, with a safety net for the cases that set no flag: at the end of `Deserialize`,
compare the number of blobs the model actually captured against `LoadedCompanionBlobCount`
(`FormDefinition.cs:137`) and refuse when fewer. `ODBC Log In.frm`'s `List` is exactly that case — `List` *is*
a modelled `PropertyClass` (`ListBoxComponentClass.cs:16`, `VBProperties.cs:93`) of CLR type
`List<string>`, which matches none of the deserializer's type branches, so it is dropped with no diagnostic
at all and `ItemData` is the only reason that form gets flagged.

The predicted corpus effect of the explicit flag alone, read off the files, is six gated forms:
`Splash Screen`, `ODBC Log In`, `Web Browser`, `Treeview Listview Splitter` — and, newly,
`Button ListBox.frm` (`DragIcon` on a `ListBox`, `Picture` on four `CommandButton`s; `CommandButtonComponentClass`
has no `Picture`) and `Mover ListBox.frm` (two `CommandButton` `Picture`s). Those last two save lossily today
and the README already admits it. `Options Dialog.frm`'s only `.frx` reference is the form's `Icon` (`:10`),
which *is* modelled, and `Tip of the Day.frm`'s only one is a `PictureBox` `Picture` (`:35`), also modelled —
so both come out clean and are the two forms this issue frees.

Six in, six out. The corpus assertion must therefore become an expected **set**, not a count, or it will
report no progress on a change that removes two whole classes of corruption. That set assertion is also the
only form of the check that survives the transition honestly: `KnownReadOnly` at
`SerializationCorpusTests.cs:249` is a `BeLessThanOrEqualTo`, so introducing the blob reason before removing
the container reason would turn it red at eight.

## The heterogeneous-children problem, and the safety inversion it hides

The refusal gate counts nesting depth only for components the IDE *models*: an unknown control type returns
from `LoadRecur` at `:105`, before the depth line at `:129-130`. Its text is preserved, but on the form
(`FormDefinition.cs:88` is a flat form-level `List<string>`, with no parent and no ordinal) and re-indented
to a hardcoded level (`ReconstructRawSubtree(serializedComponent, 1)` at `:98`), and on save every preserved
subtree is emitted in one loop at the end of the root `Begin` (`FormSerializer.cs:96-100`).

So a form whose only nesting is an unmodelled control inside a Frame loads with `CanSaveFaithfully = true`
and saves with that control re-parented onto the form, still carrying frame-relative coordinates. That is a
live outcome 3 today — C9 in `docs/serialization-fidelity-2026-08.md` — independent of this issue.
`Splash Screen.frm:19-32` is the corpus case, masked only by luck: `Frame1` holds `VB.Image imgLogo`
(unmodelled — there is no `ImageComponentClass`) alongside eight modelled Labels, and only the Labels trip
the gate.

The earlier draft made this its own first phase and, as the adversarial reviewer spotted, contradicted
itself doing so: it proposed both *re-placing* preserved subtrees inside their container (which removes the
corruption) and *gating* that case (which refuses it), and the writer half would have produced a third wrong
shape — raw subtrees emitted inside a container whose modelled children were still flattened to form level.
The reviewer's diagnosis is right; the proposed split is resolved the other way round from the reviewer's
suggestion, because gating first is what keeps every intermediate state strictly safer:

- **Gate first.** Make the depth counter honest — an unmodelled subtree read from a non-form container
  contributes its depth — and change nothing in the writer. Verified against the corpus, this moves no form:
  `Splash Screen` is already gated by its Labels, and every other unmodelled subtree in the corpus
  (`imlIcons`, `tbsOptions`, `tvTreeView`, `lvListView`, `imgSplitter`, `brwWebBrowser`, `tbToolBar`) sits at
  depth 2 directly under the form, where re-placing it at the form is already what happens.
- **Re-place later**, in the same phase that teaches the writer to nest modelled children, so the two land
  as one coherent output shape.

The re-placement itself is structural. `UnknownChildSubtreeTexts` moves from `FormDefinition` onto
`ComponentInstance`, as an ordinal-tagged list on the *container* it was read from — and since the form is
`Components[0]`, form-level subtrees land there under the same rule.
`FormDefinition.UnknownChildSubtreeTexts` becomes a derived pre-order aggregation, which keeps the five
existing assertions (`PlaceholderComponentTests.cs:104-105`, `:118`, `:167`;
`SerializationRoundTripTests.cs:592-594`; `UserControlComponentTests.cs:119`) meaning what they mean. The
writer then emits each container's modelled children and its raw subtrees interleaved by ordinal, which
preserves both position and z-order without needing a discriminated child type. That also forces
`ReconstructRawSubtree`'s hardcoded `1` to become the real load depth, because verbatim lines are replayed
with their captured indentation and no re-indent.

Note the inversion it corrects: today an unknown **container** round-trips better than a known one, because
`ReconstructRawSubtree` recurses into `SubComponents` (`FormDeserializer.cs:349-350`) and preserves the whole
subtree as one block. Modelling a control type is currently what loses its children. An unknown subtree
nested inside a known container must keep its verbatim text and gain a position — never be re-modelled into
`ComponentInstance`s.

## How a Frame becomes a real container at run time

`VBFrame` is a `HeaderedContentControl` whose `Content` is never set (`VBFrame.cs:6-13`;
`FrameComponentClass.cs:20-29` sets only `Header`, colours and font), so a Frame today is a group box with a
caption and a permanently empty interior. As set out above it gets its own `ControlTheme` with a child-host
`Canvas` at the control's outer bounds, `ClipToBounds = true` — not optional, because VB6 clips children to
the container and a `ContentPresenter` does not.

`VBLoader.SpawnComponents`'s placement loop (`VBLoader.cs:52-63`) becomes a recursive walk that adds each
child to its container's host. The stored relative value goes straight to `Canvas.SetLeft`/`SetTop`, which is
the whole point of the nested-canvas shape. The name-allocation and array-grouping passes above it
(`:35-50`, `:64-84`) are untouched and stay flat.

The earlier draft proposed unifying `SpawnComponents` and `SpawnComponentsForDesigner` before adding the
recursion. **Both reviewers are right that this is unsafe and the task is withdrawn.**
`SpawnComponentsForDesigner` (`VBLoader.cs:115-134`) is not only a designer path: it is the *run-time* body
of a hosted UserControl (`UserControlComponentClass.cs:26-27`), and it deliberately allocates no interpreter
variables and builds no control-array groups. Unifying them would silently start allocating a hosted
UserControl's children into the host form's scope. What is shared is the *placement* recursion, extracted as
a helper both call; the allocation stays in `SpawnComponents` alone.

This also satisfies a requirement it was not aimed at: `usercontrol-rendering/spec.md` requires a hosted
UserControl be drawn showing its children as they will appear at run time, and that renderer flattens
containers one level down exactly as the form renderer does. A `.ctl` whose definition holds a populated
Frame is worth a verification scenario.

Six things come with it that are easy to miss.

**Event wiring needs nothing.** All Click/Focus handlers are static class handlers registered in each
control's static constructor (`AttachedEvents.cs:9-25`), and dispatch walks the visual tree to the window
(`RuntimeExtensions.cs:10-22`). As long as the host canvas is genuinely in the visual tree, `Text1_Change`
inside a Frame dispatches exactly as it does today. Nothing per-instance may be added.

**But nesting creates false container Click events.** The non-Button branch of `AttachClick` is a
`PointerReleased` class handler (`AttachedEvents.cs:21-24`), registered for `VBFrame` and `VBPictureBox`;
`PointerReleasedEvent` routes `Tunnel|Bubble` and `AddClassHandler` defaults to `Direct|Bubble`, both
verified against Avalonia 12.0.4, and the handler inspects neither `e.Source` nor `e.Handled`. The moment a
control lives inside a Frame, every click on it also raises `Frame1_Click`. This is unreachable today only
because containers are always empty, and the guard must land **before** anything is nested.

**A hidden container must not unrealise its children.** Avalonia does not apply a hidden templated control's
template at all — verified empirically: a `HeaderedContentControl` with `IsVisible = false`, measured and
arranged, reports zero visual children. The earlier draft treated that as a Timer problem and the adversarial
reviewer is right that it is far broader: with no template there is no host, so *every* control inside
`Frame1.Visible = False` is unattached, `RuntimeExtensions.ExecuteSub`'s
`FindAncestorOfType<IModuleExecutionRoot>()` returns null, and nothing dispatches — not `Timer_Timer`, not
`Text1_Change`, not `Command1_Click`, not `GotFocus`/`LostFocus`. Today those controls are direct children of
the run canvas with `IsVisible = false`, which still counts as attached, so they dispatch correctly. Nesting
would introduce that regression, and hoisting on `IsVisual` does not touch it. The fix is at the interop
registration: for a container class, VB6's `Visible` maps to `Opacity` plus `IsHitTestVisible` rather than to
`Visual.IsVisibleProperty` (`AvaloniaInteroperability.cs:105`), so a hidden container stays realised, its
children stay attached and keep dispatching, and it draws nothing and takes no input — which is what VB6
does. The common VB6 tab idiom parks frames off-screen rather than hiding them, and that path was already
safe: the run canvas has `ClipToBounds = true` (`VBLoader.cs:21-24`), so an off-canvas frame is clipped while
staying realised.

**Non-visual controls are hoisted, not nested.** `VBTimer` starts its `DispatcherTimer` only from
`OnAttachedToVisualTree` and guards on `IsAttachedToVisualTree()` (`VBTimer.axaml.cs:37-58`). With the
opacity fix above a nested Timer would work, but its attachment would depend on its container's template
being applied, which is a dependency with no upside: a Timer has no observable position, so hosting it on the
form's own canvas cannot be detected except through the tick, which is the behaviour being protected. So the
loader consults `IsVisual` (`IComponentClass.cs:19`, `ComponentBaseClass.cs:39`) and hosts non-visual
components on the form canvas regardless of their recorded container, while the **model** keeps the recorded
container so the file still round-trips.

That hoist requires the loader to start consulting `IsVisual`, which it never has — the property's only
consumer anywhere is `FormSerializer.cs:160`. Which surfaces a pre-existing defect: a VB6 `Timer` is
currently added to the run canvas as a **visible 28×28 clock icon** on every running form, because
`VBLoader` sets `Width = 0` while the `VBTimer` `ControlTheme` pins `MinWidth`/`MaxWidth` to 28
(`VBTimer.axaml:9-12`). It is independent of this issue and squarely in its blast radius; the same
`IsVisual` check fixes both.

**OptionButton grouping changes, three times over the phase sequence.** `VBOptionButton : RadioButton`
(`VBOptionButton.cs:11`) and `OptionButtonComponentClass` never set `GroupName`, so Avalonia groups radio
buttons by their parent panel. Today every option button on a form shares one flat Canvas and therefore one
group — wrong for any VB6 form with frames, where each Frame is its own group. After the Frame phase a
Frame's options become their own group (right) while options in a PictureBox stay form-wide (still wrong)
until the PictureBox phase. Nothing pins any of the three states today. The end state is correct and free,
but each phase needs its assertion. The same is true, and equally unmentioned in the earlier draft, of
`Frame1.Enabled = False` propagating to children, which Avalonia gives for free once the tree is real and
which does not happen at all today.

**`ControlArrayGroup` needs a per-element host.** It holds one `Canvas? canvas` field that the last
registration wins (`ControlArrayGroup.cs:23`, `:36-46`), and `Load` clones into that single canvas copying
the template's `Left`/`Top` (`:53-70`). Under container-relative coordinates the copied position is only
right if the clone lands in the same container as its template, so host tracking and coordinate correctness
are one fix. `Treeview Listview Splitter.frm`'s `lblTitle` array lives entirely inside `picTitles`, so this
is corpus-reachable, not theoretical. The harder case is an array element that **is** a container:
`Options Dialog.frm` is a four-element `picOptions` array where every element holds a Frame, and
`ODBC Log In.frm:35-42` is a Frame carrying `Index = 0` that holds thirteen children. `Load` clones the
control only (`:60`), so for a container template it produces an empty container. Whether VB6's `Load` clones
a container element's contained controls is not established and is added to the open questions; until it is,
`Load` on a container element produces an empty container and says so in a test. Related: `Index` is still
unmodelled and parsed out of `UnknownRawPropertyLines` (`VBLoader.cs:96-113`), so a container that is an
array element carries its identity in raw lines the new interleaved writer must keep in the right block.

`VBPictureBox` is the expensive half and is deliberately a separate phase. It is a `TemplatedControl`, not a
`ContentControl` (`VBPictureBox.cs:9`), and its `ControlTheme` is a `ClassicBorderDecorator` wrapping a
single `Image` with no content presenter at all (`Resources.axaml:113-125`). Making it a container means a
new child-host property, a template that presents it inside the decorator, and the decorator itself driven
from `BorderStyle`/`Appearance` — a `ControlTheme` edit, not just C#.

## Z-order: a pre-existing inversion, recorded and scoped out

Sibling order is z-order, and HexIDE's paint order is currently the inverse of VB6's.

Walking the real Win32 z-chain of a compiled form gives `z(0)=picTw, z(1)=picPx, z(2)=fraBox, …` —
byte-for-byte the order of the `Begin` blocks, first block frontmost, since `GW_HWNDFIRST`/`GW_HWNDNEXT`
enumerate topmost first. `Options Dialog.frm` corroborates it from the file alone: `picOptions` is written at
`:19` and the `tbsOptions` TabStrip at `:124`, and the PictureBox has to paint over the TabStrip body for the
dialog to look like a tabbed dialog at all. In Avalonia a Canvas paints its children in order, so *later* is
on top — and `FormEditViewModel.cs:263` moves to `Components.Count - 1` for `BringToFront` while `:283` moves
to `0` for `SendToBack`, matching Avalonia and therefore inverting VB6. A `BringToFront` saved today writes
the control last in the file, which VB6 reads as backmost.

The adversarial reviewer calls this self-contradictory in the earlier draft. It is not contradictory — both
statements are true and together they *are* the finding — but the earlier draft failed to draw the
conclusion, and the reviewer is right that "make the commands sibling-scoped" preserves the inversion rather
than fixing it.

The fix is a consistent reversal at the render boundary, and it is deliberately **not** in this change, for
two reasons. It is a pre-existing defect affecting every overlapping form, saveable or not, so folding it in
doubles the review surface of an already large change and mixes a behaviour change to existing projects into
a change about containers. And it is only *safe* after containment is real: today a container's children
appear after its `Begin` and so get higher canvas indices, which is why they happen to paint on top at all —
reversing before Phase 3 would bury every container child inside its own container. What this change owes is
that the ordered child list makes the later fix expressible, and that `BringToFront`/`SendToBack` become
sibling-scoped so they can never move a control out of its container.

The reviewer additionally concluded that the Phase 6 visual check — "observe one tab page rather than four
overlapping" — is therefore not a valid acceptance criterion. **That conclusion is wrong.**
`MSComctlLib.TabStrip` is not in `FormDeserializer.AllSupportedComponents` (`:12-29`), so HexIDE renders
nothing at all for `tbsOptions`; there is no TabStrip body to be painted over. The four tab pages overlap
each other today purely because their `Left = -20000` is not being applied to their children, and that is
exactly what the coordinate fix corrects. The check stands.

## What the designer must learn

**That model coordinates are relative**, via the computed getter/setter, the `ContainerBounds` accessor and
the descendant fan-out described above. This is the change that satisfies the maintainer's condition for
opening the gate.

**That a drag applies its displacement once per subtree.** `form-designer/spec.md:55-58` mandates one
displacement for every selected control; where a selection holds both a container and a control inside it,
the child is already being moved by its container and must be skipped, or it moves twice.

**That the marquee will produce exactly that selection, and that this is a divergence.**
`form-designer/spec.md:35-49` mandates that a dragged region select every control it intersects, and on a
flat canvas a marquee across a Frame selects the Frame *and* its children — a selection VB6 never produces,
because VB6 scopes the marquee to the container the drag began in. Container-scoped marquee needs the
container hit-test that interactive re-parenting needs and is deferred with it. So the parent/child
double-move rule is not an implementation detail; it is the consequence of a deliberate divergence, and both
belong in the `form-designer` delta and in the residual risks rather than only in the code.

**That snapping and clamping happen in the container's space**, per the worked example above, reading the
origin from `ContainerBounds`.

**That deleting a container deletes its subtree, and one undo restores it.**
`RemoveControlsCommand.cs:21-33` restores by index into two independent `ObservableCollection`s; it gains the
captured descendant set and the parent links. Undoing a container delete must not resurrect orphans, and must
not take a dozen undos.

**That copying a container copies its subtree, and that copying *out of* a container is a coordinate
change.** `DesignerClipboard`'s flat `ClipboardEntry` (`DesignerClipboard.cs:9-11`) carries only a base class
and a shallow property snapshot — no container — and `PasteControls` (`FormEditViewModel.cs:604-642`) replays
`Left`/`Top` verbatim plus a grid offset and appends at form level. With re-parenting out of scope, copying a
control out of a Frame and pasting it puts a container-relative coordinate onto the form as an absolute one.
The entry carries its source container; paste back into it where it still exists, and convert to form
coordinates where it does not. Building the copies from fresh instances rather than replaying a property
value is also what removes the reference-aliasing trap.

**That z-order is sibling-scoped.** `BringToFront`/`SendToBack` (`FormEditViewModel.cs:258-289`,
`ZOrderCommand.cs:21-39`) reorder within the parent's child list and never move a control out of its
container, and skip menus, which share the `Components` collection.

**That the status bar and the property grid must agree.** `FocusedProjectUtil.cs:74-89` binds
`FocusedComponentPosition`/`FocusedComponentSize` to the *view-model*'s `Left`/`Top`/`Width`/`Height`, while
the Properties window reads the *model* (`PropertiesToolViewModel.cs:111`). After this change they land on
opposite sides of the split and report different numbers for the same selection. VB6 shows the
container-relative number in both, so the status bar switches to the model value, subscribing through the
same fan-out.

**That the write-back must reconstitute the tree.** `FormEditViewModel.cs:123-132` — the
`ApplyAllUnsavedChangesEvent` handler — is the only place the designer authors the model's component list,
ordering it by position within `Components`. Whatever the canvas holds, the tree order has to be rebuilt
there or a save flattens again. It being a single choke point rather than twenty-five is the one piece of
luck in this issue.

**That there is a new undo command.** None of the seven existing commands can express a change of container,
and `MoveResizeCommand` would restore a container-relative number into a control that had left that
container. Even with interactive re-parenting out of scope, the undo contract has to be written for it
(`form-designer-undo-redo/spec.md:17-35` enumerates the undoable mutations and re-parenting is not among
them), and any command that runs from a drag must be composed into a single `BatchCommand` and pushed from
`EndDrag` after `IsDragging` is cleared, because `DesignerUndoStack.Push` silently discards everything while
a drag is in progress (`DesignerUndoStack.cs:18-23`).

## The automation surface

The earlier draft listed `move_control` among the sites that keep working unedited. That is true of the write
path and false of the contract, because the read path sits on the other side of the new boundary and both
reviewers found it independently.

- `get_form_controls` builds `ControlInfo` from `designerVm.AllComponents.Select(v => v.Instance)` or
  `form.Components` (`HexIdeTools.cs:211-213`) and reports `c.GetPropertyOrDefault(LeftProperty)` (`:218-221`)
  — the **model**, hence container-relative after this change. `ControlInfo` (`:1324-1334`) carries no
  container field, so a caller cannot tell which space it read or what the control is inside.
- `set_control_property` (`:271-305`) resolves against model instances and calls `SetUntypedProperty`
  directly — also relative.
- `move_control` (`:583-599`) writes `target.Left = left.Value` on the `ComponentInstanceViewModel` — absolute.

An agent that reads 140 from `get_form_controls` and writes 140 through `move_control` moves the control by
the container's whole origin: for `Options Dialog.frm`'s `picOptions` that is 20000 twips, silently, into the
saved file.

The decision is **container-relative everywhere**, matching the model, the property grid and VB6.
`move_control` writes the model through the same undo path instead of the view-model; for a top-level control
nothing changes, which is the overwhelmingly common case. `ControlInfo` gains a `container` field naming the
containing control (null at form level) so the space is self-describing. `openspec/specs/hexide-mcp-server/spec.md`
states no per-tool requirements and needs no delta — which is itself the problem, because nothing in openspec
would pin this. So the new `container-controls` capability carries the requirement that the automation surface
reports and accepts container-relative coordinates and identifies a control's container.

One residual to record rather than fix: both `move_control` and `set_control_property` resolve by name with
`FirstOrDefault`, which picks one of the four `picOptions` arbitrarily on the corpus form this issue is named
for.

## The gate

The gate is a depth counter with an explicit menu exemption (`FormDeserializer.cs:129-130`, `:316-318`), and
`FormDefinition.cs:141-156` documents narrowing it as the intended mechanism. This change adds a second
exemption on the same shape: a component whose parent link was recorded does not contribute to the depth,
exactly as a menu under a menu does not. The dead `parent is null` arm goes — `LoadRecur(rootComponent, null, 1)`
at `:310` means it is only ever the root, and a non-visual root type is already rejected at `:78-82`.
`VbFrmFormatDeserializer.MaxBeginDepth` goes too: it is written at `:137-138`, read nowhere, and its summary
at `:36-43` is the last place in the tree still describing the pre-#83 world.

It also gains the blob reason described above, which is a *widening* of the gate on two forms and the
prerequisite for the container narrowing being safe.

The gate stays **shut** through every phase up to the last one. That is the maintainer's decision and the
evidence makes it non-negotiable rather than cautious: the save half of this issue is nearly free. Every
`Left`/`Top`/`Width`/`Height` at `Begin` depth ≥ 3 across all 25 VB98 template files is an integer, so ÷15
then ×15 rounded to six decimal places is exact, and emitting nested `Begin` blocks reproduces the bytes with
no coordinate work at all. Narrowing on that alone would convert misrendered read-only forms into misrendered
*editable* ones, and the first drag would write a number into the wrong coordinate space.

It stays shut afterwards for anything still unreproducible — including a component nested under a class that
is not one of the three containers, which the format permits writing and VB6 accepts silently, and which
HexIDE must therefore treat as corrupt input it handles itself. That closed list is Form, PictureBox and
Frame. An add-in may register its own component class (`IHexIdeHost.RegisterComponent(IComponentClass)`,
`IHexIdeHost.cs:20`), and within this change an add-in-registered class is **not** a container: a component
nested under one keeps the form read-only. Whether the add-in API should be able to declare a container is
out of scope and belongs with `addin-system`, since it would mean adding a member to a public interface that
third parties implement.

The existing tests that assert the current gate are the inventory of what "narrows" has to mean, and each
needs a successor rather than a deletion: `MenuHierarchySaveTests.cs:118-137` and `:139-159` (a populated
container is still read-only), `MenuHierarchyLoadTests.cs:121-128` (asserts the depth `Be(3)`),
`UnfaithfulSaveGateTests.cs:146-152` (asserts the reason contains "nested" and "flatten"), and — missed by
the earlier draft — `CodeEditorViewModelTests.cs:521` (asserts the banner reason contains "nested").

Rewording the reason **does** need a localisation delta, and a larger one than the completeness reviewer
suggested. `Str.Dialog.UnfaithfulSave.Body.One`/`.Many` do not merely frame the reason; their own text names
the cause — "because it uses nested controls or menus that HexIDE would flatten" — across all 33 packs in
`IDE/HexIDE/Localization/Packs`. With a second reason category the body must become cause-neutral and the
per-form reason must become a localised key rather than the hardcoded English literal it is today
(`FormDeserializer.cs:317-318`, surfaced untranslated through `FormEditViewModel.cs:82` and
`CodeEditorViewModel.cs:104`). That is a bounded change — two reworded keys plus a small closed set of reason
keys — and it also closes an existing untranslated-string defect. The per-language work is the language-pack
workflow's job, not this change's.

`KnownVb6Failures` stays at 22 (`SerializationCorpusTests.cs:233`) — it will not move, because K1/K2
whitespace still dirties line 2-3 of every file — and `KnownDrifters` stays at 0 (`:372`). `KnownReadOnly`
(`:249`) is replaced by an expected-set assertion, for the reasons given above. The comment above
`KnownVb6Failures` at `:231-232` says "RAISE this number as fixes land", which is inverted for an upper bound
asserted with `BeLessThanOrEqualTo` and should be corrected in passing.

The honest problem is that **CI cannot see any of this**. The corpus roots at `HEXIDE_ROUNDTRIP_CORPUS`, else
the VB6 install, else the repository's `demo/` — and `demo/` holds no container nesting, so on Linux both
assertions pass vacuously. So the change owes checked-in fixtures in the `MenuHierarchy*Tests` style plus a
positive, corpus-independent assertion, and if the template forms are to be the proof, that lane needs a
Windows-only required-corpus mode rather than a silent skip. `docs/TEST_PROJECTS.md` also documents a harness
that does not exist — `HexIDE.Standalone.exe --roundtrip --corpus … --report`, where `Program.cs:25` handles
only `--check` and a project path — which should be corrected rather than left as a promise.

There is also a hole in the gate that predates this work and gets worse under it.
`ProjectService.ReloadFormFromDisk` transfers only `Code` and `Components` from the fresh parse (`:520-522`,
and `:551-557` for UserControls), leaving `UnfaithfulSaveReason`, `MaxUnreproducibleNestingDepth`,
`HasUnmodelledBinaryProperties`, `LoadedCompanionBlobCount`, `UnknownChildSubtreeTexts` and `HeaderLines` at
their previous values; and `FormEditViewModel.cs:80`'s `IsReadOnly` is a plain getter that never raises
`PropertyChanged`. A form that acquires container nesting through an external edit keeps its open gate and
stays editable and saveable. That must be fixed in the same change, because it is a correctness hole in the
very gate being narrowed — and it is a second argument for putting the tree on `ComponentInstance`, which
travels with `UpdateComponents`, rather than on `FormDefinition`, which does not.
`file-watcher/spec.md:93+` ("Adopting a change SHALL re-establish the baseline it is compared against") is the
contract this hole sits under, and `ExternalFormChangeTests.cs:121` is the test that already covers the
component half of that reload.

## Where the reviewers were wrong

Two corrections are not accepted, and one open question they both endorsed is struck.

**"The Phase 6 visual check is not a valid acceptance criterion."** It is. `MSComctlLib.TabStrip` is
unmodelled, so HexIDE draws nothing for `tbsOptions` and there is no TabStrip body for the tab pages to be
painted over. The four overlapping pages are entirely a coordinate defect. The z-order inversion is real and
recorded, and it does not touch this check.

**"The 22-file corpus figure is wrong / is right."** The earlier draft said 20, both reviewers said 22, and
the reviewers are right — but the reasoning matters, because it is a trap that will be re-fallen into: the
uppercase `ADDIN.FRM` and `FRMDATEN.FRM` are invisible to a case-sensitive `find` and visible to
`Directory.EnumerateFiles` on Windows. The open question is struck and the fact is recorded.

**"`openspec/config.yaml`'s `rules: spec:` block contains five rules, not four."** Correct — `:18-25`, with
the last spanning four lines. Whether `spec:` should be `specs:` remains open: it is not verified here that
the CLI treats `spec` as an unknown artifact ID, and asserting it without running `openspec validate` would
be a guess.

## Roads not taken

**Rebase coordinates to form-absolute on load, convert back on save.** The designer would need no change at
all. Rejected because containers move at run time (`Options Dialog.frm:216-224`) and VB6 repositions aligned
PictureBoxes entirely, so an absolute child position is valid only for the instant it is computed; and
because the property grid reads the model and would then diverge from VB6 and from the model's own documented
contract at `VBProperties.cs:12`, `:14`.

**Make the view-model relative and rebase its consumers.** Correct end-to-end, and roughly thirty edits
including the marquee, all sixteen align/space commands, the drag write-back, and two shipped MCP tool
surfaces whose meaning would change silently.

**Store the link in the property bag, as #83 did for menus.** Rejected on clipboard aliasing.

**Reuse `MenuComponentClass.SubItemsProperty` as a generic children link.** Rejected: `FormSerializer.cs:69`
and `:81` probe it on every component class, so a Frame would become writable as a menu item.

**Make `FormDefinition.Components` top-level-only.** Rejected: most read sites want flat; control-array
grouping and interpreter name allocation are correct *because* they are flat; and blob collection walks the
flat list with no else branch on the reference write, so nested pictures would vanish silently beside an
orphaned `.frx`.

**Key parenting by name.** Rejected against Microsoft's own templates.

**Reach the child host through a named `ControlTheme` part.** Rejected: no name scope exists at load time,
and template re-application orphans the children.

**Keep `VBFrame` on SimpleTheme's stock template and compensate for the presenter inset.** Rejected: the
offset would be a property of whichever theme is loaded, and the designer would have to know it.

**Unify `SpawnComponents` and `SpawnComponentsForDesigner`.** Withdrawn from the earlier draft: the latter is
the run-time body of a hosted UserControl and deliberately allocates no interpreter variables.

**Make `VBFrame` a `Panel` subclass.** Works mechanically, and is no longer cheaper than a `ControlTheme` now
that the Frame needs one anyway.

**Model `VB.Image` and `VB.Line` to remove the unmodelled-child case.** Rejected as scope, and now on stronger
grounds: `VB.Line`'s coordinates are in the container's scale units, so modelling it means modelling the scale
system too.

**Fix the z-order inversion here.** Deferred: pre-existing, affects every overlapping form, and is only safe
to reverse once containment is real.

**Fix the blob loss rather than gate it.** That is the binary pass-through phase already tracked as B1/C7 in
`docs/serialization-fidelity-2026-08.md`, and it is a larger change than this one.

**Open the gate after the serialisation half.** Rejected by the maintainer, and the evidence above is what
makes it the right call.

## Residual risk

The design is honest about the following.

The read-only count does not fall. Two forms are freed and two are newly gated, and the change's value has to
be argued from the corruption it removes rather than from a number going down.

The PictureBox inset is verified at only two points: the default 3-D case (2 px per side) and the fully flat
case (0). `Appearance = 0 'Flat` with `BorderStyle = 1` was never measured.
`Treeview Listview Splitter.frm`'s `picTitles` is flat and borderless so it resolves to zero, but that is luck
rather than coverage.

`Align` is modelled and unhonoured, so after both runtime phases the run-time form is still **not** correct
for `Web Browser.frm` (`picAddress`, `Align = 1`) or `Treeview Listview Splitter.frm` (`picTitles`,
`Align = 1`), plus `Web Browser.frm`'s Align-ed OCX toolbar. VB6 docks those to the form edge and stretches
them to the form's width, ignoring the stored position, and re-lays them out on resize. The phase exit
criteria are therefore "children are positioned correctly relative to their container", not "the run-time
form is correct". Nesting is what makes `Align` implementable at all — a docked container that carries its
children is only expressible once children live inside it — so this change unblocks it without doing it.

Container children are not clipped on the design canvas: a flat sibling Canvas cannot clip, and `ControlItem`'s
theme sets `ClipToBounds="False"` (`FormEditView.axaml:198`). VB6 clips and the run-time path will clip, so
the two surfaces will disagree about a control that overhangs its container.

A control cannot be moved between containers, a control dragged onto a Frame will not be adopted by it, and a
marquee across a Frame selects the Frame together with its children. All three are visible divergences from
VB6 for the whole life of this change.

The bubbled-`Click` guard is being chosen without an oracle pin. "Clicking a control inside a Frame does not
fire `Frame1_Click` in VB6" is near-certain but unverified, and the guard's exact shape (`e.Source` check
versus setting `e.Handled`) follows from the answer.

Mapping a container's `Visible` to `Opacity` keeps children dispatching, and it is an approximation: a VB6
control inside a hidden Frame cannot receive focus, and an Avalonia control at zero opacity with
`IsHitTestVisible = false` still can programmatically. Unverified against VB6.

The designer half of this work has **no existing test surface to extend**. Across all four suites only
`FileWatcherReloadTests.cs` and `ReadOnlyBannerIntegrationTests.cs` mention `FormEditViewModel`, and neither
exercises selection, drag, align, z-order, clipboard or undo; `HexIDE.Tests/ViewModels/` has a file for every
other view-model in the IDE and none for this one. Everything named in this design as a serializer test is an
edit to an existing file; everything named as a designer test is new. The integration work lands in
`HexIDE.Integration.Tests/Views/` — `SpawnComponentsForDesignerTests.cs`, `ControlArrayTests.cs`,
`WithControlInteropTests.cs` (which reaches its control as `canvas.Children[0]` on a flat canvas),
`FileWatcherReloadTests.cs` and `ReadOnlyBannerIntegrationTests.cs`, whose header states the gate is enforced
in the view layer, which is exactly what the last phase flips. In `HexIDE.Tests` the affected files are
`Projects/CompanionBinaryPreservationTests.cs`, `Projects/ExternalFormChangeTests.cs`,
`Projects/UserControlSaveRoundTripTests.cs`, `Projects/UnfaithfulSaveGateTests.cs`,
`Debugging/ControlLocalsTests.cs`, `ViewModels/CodeEditorViewModelTests.cs`, and
`IDE/DirtyDetectorTests.cs` + `IDE/FileBaselineStoreTests.cs`. `LspServer` needs nothing —
`HexIDE.Lsp`/`HexIDE.LspProxy` never touch a designer block — which is worth stating so a reviewer does not
go looking.

Nothing in CI can observe this landing or regressing without new fixtures: `demo/` has no container nesting.

The three gated forms that carry an OCX — `Options Dialog`, `Web Browser`, `Treeview Listview Splitter` —
cannot be runtime-verified against `vb6.exe` on this machine: `/make` fails with "Error accessing the system
registry" without an elevated session, with or without the sandbox. Their expected containment comes from
reading the files. The other three were compiled and walked at run time and give exact expected parent links
and child coordinates, so they are the acceptance fixtures.

`ComponentInstanceViewModel.cs:33-34` rejects a rename to any name already present in `AllComponents`, which
real corpus data already violates (four `picOptions`) and which also fires when a control is renamed to its
own current name, because the instance is in the collection when the Changing event runs. Making
`Options Dialog.frm` editable makes this immediately reachable, so it is fixed in the designer phase rather
than merely noted.

`Save As` still bypasses the gate (`ProjectService.cs:471`). This change does not alter that, so "the gate
stays shut" remains an incomplete protection for any structure still unreproducible after it.

`docs/debugger-vb6-divergences.md:110-112` records that container children show flat under `Me` "because the
runtime spawns all controls flat on one form canvas". That stated cause becomes false when this lands while
the symptom survives, because `DebugInspector.cs:298-305` expands a `Control` node to its properties and
nothing else. Either it gains a container-children provider or the row is rewritten — not left advertising a
fix that did not happen.

## Living documents this moves

Named here so none is missed: `README.md:38` (the read-only row and the will-not-reproduce row) and
`README.md:50-60`, whose "HexIDE's designer model has no parent link between controls" and "container nesting
… is the only thing still holding a form read-only" both become false; `docs/MISSING_FEATURES.md:21`, `:247`
and `:281`; `docs/serialization-fidelity-2026-08.md` C1, C4, C7, C9 and C10, which is the remediation plan
this change closes part of and which already records `Scale*` and the unknown-subtree re-parenting that the
earlier draft presented as new findings; `docs/mcp-server-gaps.md`'s advice to hand-author `.frm` files
because the geometry is "`Left`/`Top`/`Width`/`Height` in twips", which acquires a container caveat;
`docs/TEST_PROJECTS.md`, both for the fixture axis and for the harness it documents but that does not exist;
and `docs/vb6-fidelity-oracle.md`, or a new companion beside it, for the designer-geometry findings, which are
about persisted layout rather than interpreter runtime semantics and do not belong in the oracle document
unchanged.

Spec deltas: `serialization-round-trip` (three MODIFIED requirements), a new `container-controls` capability,
`form-designer` (drag once per subtree; marquee divergence; clipboard), `form-designer-undo-redo`
(re-parenting is undoable; a container and its contents restore together), `file-watcher` (the reload must
re-establish the fidelity verdict, not only the components), `user-control-designer` and
`usercontrol-rendering` (a `.ctl` with a populated container is in scope for both). `hexide-mcp-server` needs
no delta because it states no per-tool requirements; the coordinate contract is pinned in `container-controls`
instead. `addin-system` gets a note that a registered component class is not a container.

## Open questions

These were not established and must not be guessed at:

- Does VB6's designer re-parent a control dragged over a container, or only through cut-and-paste into a
  selected container? Needs the interactive VB6 IDE; `/make` cannot substitute.
- Does VB6 scope marquee selection to the container the drag began in, and does it ever return a container
  and its children in one selection?
- Does a `Timer` inside a `Frame` with `Visible = False` keep firing in VB6, and can a control inside a hidden
  Frame receive focus programmatically?
- Does clicking a control inside a Frame fire `Frame1_Click` in VB6?
- Does VB6 tab strictly by `TabIndex` across container boundaries, or does it traverse containers as units?
- Is the PictureBox inset always `(Width − ScaleWidth) / 2` symmetric, and what is it for `Appearance = 0`
  with `BorderStyle = 1`?
- Are a container's `Scale*` values authoritative on load, or does VB6 recompute them from `Width`/`Height`
  on save? This decides whether preservation is enough or whether the values must be modelled.
- Does `Load` on a control-array element that is a container clone the element's contained controls?
- Does a container clip a child that overhangs its bounds at run time? `Splash Screen.frm` has `lblWarning` at
  `Left = 150`, `Width = 6855` inside a 7080-wide Frame — a 75-twip margin.
- Does re-saving a container form in VB6's own IDE renumber `TabIndex` in the file, given that the loader
  renumbers in memory?
- Is `TabDlg.SSTab` a container, and how does it record which tab each child belongs to? It is the commonest
  third-party container in real VB6 code and the probe was blocked by the registry error.
- Is `MDIForm` a container, and with what restriction? There is no MDI form anywhere in the corpus.
- Does `openspec` treat `rules: spec:` as an unknown artifact ID and silently discard the five authoring
  rules, or is `spec` a valid key?
- Should `VB98\Wizards` become a second corpus root? `WIZARD.FRM` has 29 `Begin` blocks, 21 of them nested —
  six sibling Frames all parked at `Left = -10000`, a bottom-aligned PictureBox holding a control array plus
  two `VB.Line`s, and a form-wide `TabIndex` 0..19 threaded through all of it. `SETUP1.FRM:30-32` and
  `GROUP.FRM:14-16` are additionally `ScaleMode = 3 'Pixel` **forms**, a case nothing in the current corpus
  covers. It is the best available stress fixture and the harness never sees it.
