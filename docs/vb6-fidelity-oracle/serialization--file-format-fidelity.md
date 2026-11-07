## Serialization / file-format fidelity (2026-08-11)

First use of the oracle for **file format** rather than runtime semantics. Method: author a minimal Standard
EXE, vary one aspect of the `.frm`, and run `VB6.EXE /make` headless. A `/make` must load and parse the form
to build its resource, so a load failure is a compile failure — this exercises VB6's real form parser without
driving the IDE. Harness at `scratchpad/oracle-q1` (see the recipe above: absolute paths, `CreateNoWindow`).

Every result below is from a real `/make` run, not inference.

| # | Question | Result | Consequence |
|---|---|---|---|
| Q1a | Is the **trailing space** after `Begin VB.Form Form1 ` required on load? | **No** — compiles without it | HexIDE omitting it is COSMETIC, not blocking |
| Q1b | Is the **property-name column padding** (`Caption         =`) required? | **No** — compiles unpadded | COSMETIC |
| Q1c | Both deviations together (HexIDE's actual output shape) | **Compiles** | Confirms the two are independent and neither is load-bearing |
| Q5 | Does VB6 accept a **fractional** `ClientWidth`/`ScaleWidth` (`6683.999999999999`)? | **Yes** — compiles | HexIDE's float noise is value drift, not a load failure |
| Q2a | Nested menus with `Shortcut` on a child (VB6's own shape) | **Compiles** | Control |
| Q2b | **Flattened** menus, no shortcut | **Compiles** | Flattening alone destroys structure but still loads |
| Q2c | **Flattened** menus **with** a shortcut | **FAILS TO LOAD** | See below |

### Q2c is the important one

```
Line 16: Cannot set shortcut property in menu mnuFileNew. Parent menu cannot have a shortcut key.
```

VB6 treats a top-level `Begin VB.Menu` as a *parent menu* and **rejects** a `Shortcut` on it. HexIDE flattens
nested `Begin` blocks on save, which promotes every child menu item to top level — so any form whose menus
carry shortcuts is written out in a state **VB6 refuses to open**.

This is not a corner case. Of the six menu templates VB6 ships, `File Menu.frm` (4 shortcuts) and
`Edit Menu.frm` (5) use them — the two menus essentially every VB6 application has. `Ctrl+N`/`Ctrl+O`/`Ctrl+S`
on a File menu is the canonical VB6 idiom.

**Severity: the menu-flattening defect is BLOCKING, not CORRUPTING** — it produces files the oracle cannot
load. Recorded against the round-trip epic.

### Still unanswered

Q4 (which rect VB6 honours when a file declares two that contradict each other), Q6 (`Startup=` without
quotes), Q9–Q10 (`.frx` record layouts), Q11 (`VERSION 4.00` upgrade), Q13–Q16. These need the interactive
IDE or a hex dump rather than a `/make`, so they are a separate session.

Q3 (invented outer rect on a root) stopped being a question when #104 removed the invention: HexIDE no
longer writes a rectangle the file did not declare, so what VB6 would have made of one is moot.

### Corpus-wide: VB6 accepts HexIDE's reformatted output (2026-08-19)

Q1a/Q1b/Q1c above answered the "is the formatting load-bearing" question from a hand-built fixture. This
is the same question asked of the **whole corpus**, which is what that section's own method note asks for —
a fixture only exercises what its author thought to include.

Method: round-trip all 20 `.frm` files in the Template tree through `FormDeserializer` +  `FormSerializer`,
drop each into a generated single-form `.vbp` (carrying the `Object=` references from the form's own header
and the original `.frx` beside it), and `VB6.EXE /make /out` each one.

**Result: 15 of 20 build. The other 5 fail identically when the harness is given Microsoft's ORIGINAL file.**

| Outcome | Forms |
|---|---|
| Built from HexIDE's output | About Dialog, Button ListBox, Dialog, Edit Menu, Explorer File Menu, File Menu, Form1, Help Menu, Log in Dialog, Mover ListBox, ODBC Log In, Splash Screen, Tip of the Day, View Menu, FRMDATEN |
| Failed — **and the original fails the same way** | Window Menu (`Method or data member not found` — the template calls MDI methods with no MDI parent), Options Dialog / Treeview Listview Splitter (`Must have startup form` — the form fails to load without its project), ADDIN (`User-defined type not defined`), Web Browser (`Errors during load` — unregistered OCX) |

So no form is broken by what HexIDE writes; the five failures are template forms lifted out of the project
context they were written for.

**The control run is the whole point.** Run only HexIDE's output and five failures look like five defects.
The same harness on the original files is what turns them into harness artifacts — and it costs one extra
loop. Do not report a `/make` failure against generated output without it.

**Consequence:** property order, column padding, the trailing space on the `Begin` line and the missing enum
comments are confirmed COSMETIC at corpus scale, not merely on a fixture. They keep a file from being
byte-identical; they do not keep VB6 from loading it. That is what makes the remaining round-trip work a
fidelity burndown rather than a data-loss one.

**Limit of this evidence:** `/make` proves VB6 *loads and compiles* the form. It does not prove the built
program looks the same — a form whose geometry changed still compiles. That is why #104 is a separate
finding from this one, and why Q4 stays open.

### `.frx` blob encoding is deterministic (2026-08-11)

Byte-identical `.frx` round-trip is **achievable**, not a fool's errand — worth recording because the
opposite is a plausible-sounding assumption drawn from a real neighbouring case.

Four VB6-shipped forms in four separate projects (`COPY`, `DSKSPACE`, `PATH`, `SERVERDT`) produced companion
files that are **byte-identical** (SHA-256 `2248962480f2260f…`, 1090 bytes each). The same icon encoded the
same way across four independent saves. The record header carries nothing session- or machine-dependent:

```
06 03 00 00  6c 74 00 00  fe 02 00 00  00 00 01 00  01 00 20 20 …
└ length ─┘  └ Preamble ┘  └ Size ────┘  └ raw .ico header ─────┘
             └──────── StdPicture stream, [MS-OFORMS] §2.4.5 ───────…
```

No timestamp, no checksum, no GUID.

**Correction (2026-08-19): the second field is not a "type tag".** It was labelled as one here when this
was first written, from the hex alone. `6c 74 00 00` is `0x0000746C`, and [MS-OFORMS] §2.4.5 specifies
that exactly: a `StdPicture` stream opens with a 4-byte **Preamble** that MUST be `0x0000746C`, followed
by a 4-byte **Size** giving the length of the picture bytes that follow.

The arithmetic confirms it rather than merely fitting it: the outer length is `0x306` = 774, and
8 bytes of Preamble-plus-Size plus `0x2FE` = 766 bytes of payload is 774 exactly.

So the record is **`[4-byte VB6 length][StdPicture stream]`** — VB6's own container wrapped around a
structure that is published, and that HexIDE is licensed to implement. See the section below.

**The Access precedent does not transfer.** MS Access under VSS famously showed diffs on untouched forms
every commit — but the cause was specific to Access: `SaveAsText` embedded `PrtDevMode`/`PrtDevNames`
(printer device settings snapshotted from the current default printer) plus a `Checksum` line. VB6 forms
have neither.

**Limit of this evidence:** it shows the *encoder* is deterministic for a given image. It does not prove
that re-saving an unchanged form in the VB6 IDE reproduces byte-identical output — VB6 could still reorder
blobs. That needs the interactive IDE and remains open.

**Consequence for HexIDE:** none immediately, because blob *pass-through* (keep the original bytes at the
original offsets) sidesteps encoding entirely. Matching VB6's encoder only becomes necessary if the designer
ever lets a user edit an image — the comprehension problem, deliberately deferred.

### The `.frx` payloads have a published specification — the container does not (2026-08-19)

A distinction worth keeping, because it splits the remaining `.frx` work into two very different halves.

| | Documented where | May we implement from it? |
|---|---|---|
| The `.frx` **record container** — `[4-byte length][payload]`, and the 2-byte-count variant `List`/`ItemData` use | **Nowhere.** VB6-specific, undocumented | Black-box observation only, as below |
| The **payload** of a `Picture`/`Icon` record — a `StdPicture` stream | **[MS-OFORMS] §2.4.5** | **Yes** |
| The **payload** of a persisted font — a `StdFont`/`TextProps` structure | [MS-OFORMS] §2.4.x | **Yes** |

[MS-OFORMS] is *Office Forms Binary File Formats* — the MSForms control set as persisted inside Word,
Excel, PowerPoint and VBA project storage. **It is not a specification of VB6's designer files**, and it
never mentions `.frm`, `.frx` or Visual Basic. What makes it useful anyway is that VB6 and Office Forms
both persist standard OLE types the same way, so VB6's container turns out to be wrapped around
structures Office documented.

#### Why implementing from it is clean, where the VBA SDK was not

Two independent clearances, both checked rather than assumed:

- **Copyright.** The Open Specifications IP notice grants, in terms: *"you can make copies of it in order
  to develop implementations of the technologies that are described in this documentation and can
  distribute portions of it in your implementations that use these technologies or in your documentation
  as necessary to properly document the implementation."* It also states plainly: *"Microsoft does not
  claim any trade secret rights in this documentation."*
- **Patents.** [MS-OFORMS] is a **Covered Specification** under the Microsoft Open Specification Promise,
  listed by name under *Office Binary File Formats — First Published June 30, 2008*. Microsoft
  *"irrevocably promises not to assert any Microsoft Necessary Claims against you for making, using,
  selling, offering for sale, importing or distributing any implementation to the extent it conforms to a
  Covered Specification."* [MS-CFB] is covered too, if a compound-file path is ever needed.

So this may be read, implemented, and quoted into HexIDE's own documentation. That is a different licence
from anything else consulted for this work, and the difference is deliberate: it is why the format facts
above can be written down here at all.

**Contrast, recorded so it is not re-litigated.** The VBA 6.4 SDK (`VBA6SDK`) was examined on the same day
and is **not** a usable source: its EULA grants no distribution rights, restricts documentation to internal
use with no republication, and forbids reverse engineering. It also turned out to be irrelevant — it is a
COM host-embedding API (`IActiveDesigner`, `ICodeNavigate`, the `Apc*` interfaces) with zero mentions of
`.frx`, file formats, `IPersistStream` or property bags anywhere in its headers or help file. Nothing from
it has entered this project and nothing should.

#### Qualification: that determinism finding covers INTRINSIC controls only

The four byte-identical files above are a form `Icon` and a `PictureBox.Picture` — blobs VB6 encodes itself.
It does **not** follow that every `.frx` is stable, because VB6 does not author all of a `.frx`.

An OCX persists through the COM interfaces (`IPersistStream` / `IPersistPropertyBag`) and writes its own
assets into the container's stream. `Template\Forms\Web Browser.frm:126` shows the shape — an ImageList
persisting nested property bags that cite the companion:

```
BeginProperty Images {2C247F25-8591-11D1-B16A-00C0F0283628}
   BeginProperty ListImage1 {2C247F27-8591-11D1-B16A-00C0F0283628}
      Picture         =   "Web Browser.frx":0000
```

Those bytes come from third-party control code, not from VB6. Whether they are stable across saves depends
on each control's implementation — one that serialises an internal buffer, re-renders a bitmap, or iterates
an unordered collection would churn while nothing meaningful changed. Reported from practice (MS Access
under VSS showed exactly this pattern; the Access-specific `PrtDevMode`/`Checksum` cause recorded above is a
*different* mechanism that happens to produce the same symptom). **Untested here** — a single corpus snapshot
cannot show churn.

**The permanent consequence for HexIDE, which is a boundary and not a backlog item:** HexIDE does not host
ActiveX controls, and only a control can serialise itself. HexIDE therefore can *never* correctly regenerate
OCX-persisted bytes, at any level of effort. For that data, blob **pass-through — preserve the original
bytes at their original offsets, never re-encode — is the only correct strategy that will ever exist**, not
a pragmatic shortcut.

**Narrowed (2026-10-22, #60): the bytes, not the offsets.** Pass-through as built keeps every record's bytes and never
re-encodes one, but it doesn't pin a record to its original offset. Records are laid out in the order the `.frm` cites
them, which is VB6's own layout, and every citation is rewritten to wherever its record lands. For a form nobody
changed, that gives the original offsets; `Web Browser.frm` and its `.frx` come back byte for byte. Once a record HexIDE
models changes size, an OCX's record held at its original offset would overlap it, and a citation left as read would
address the wrong bytes.

This splits the binary layer cleanly:

| Blob source | Relationship |
|---|---|
| Intrinsic VB controls (`Picture`, `Icon`, `DragIcon`, `MouseIcon`) | HexIDE can own it; encoder verified deterministic |
| OCX property bags | Opaque bytes, permanently. Preserve, never regenerate. |

Corollary: byte-identity is the wrong assertion for an OCX-hosted form, and a read-only gate is the wrong
instinct — with pass-through such a form round-trips *perfectly*, precisely because nothing looks inside.

#### Correction (same day): "never host ActiveX" was wrong, and so was the target

Two errors in the entry above, both corrected here rather than edited away.

**1. OCX hosting is in scope.** `CLAUDE.md:170` and `docs/OUT_OF_SCOPE.md:5` are explicit: COM/OLE is *not*
excluded, it is Windows-gated and foundational to real-world VB6. What is excluded is ActiveX **Documents**
(`.dob`) and ActiveX **Designers** (plug-ins needing Win32 subclassing) — not hosting an OCX on a form.
So "HexIDE can never regenerate OCX-persisted bytes" is false as a permanent claim.

It also conflated two separable concerns. **Rendering** an OCX needs the control and is Windows-gated;
**storing** its persisted bytes is a byte format and needs nothing but the format. The persistence contract
(`IPersistStream`, `IPersistStreamInit`, `IPersistPropertyBag`) is documented in the VBA SDK. Reading and
writing those records is therefore knowable without hosting anything, on any platform.

*(Licence note if the SDK is used: the same clean-room rule as the VBA-Docs repo — facts, APIs and
semantics are not copyrightable, so learn-then-implement is fine; do not copy prose or code samples. Verify
the SDK's own licence terms before relying on it.)*

**2. The target is validity, not byte-identity.** VB6 developers using source control already treat `.frx`
churn as noise and ignore it — a control writing its own state through the COM persistence interfaces has
no obligation to emit identical bytes twice, and in practice does not. So **byte-identical `.frx`
round-trip is not a fidelity requirement and should not be asserted anywhere.**

The requirement is that the file stays **valid**: it loads, every offset the `.frm` cites resolves to a
record, and each control gets back data it can consume.

**Consequences:**

- The corpus harness's byte comparison of companion binaries is the **wrong assertion** — for every `.frx`,
  not merely OCX-hosted ones. The right invariant is the citation-resolution gate
  (`Every_companion_offset_cited_by_a_form_resolves_to_a_blob`) plus "the record a given citation resolves
  to is unchanged". Whole-file byte-identity should be dropped.
- Blob **pass-through** remains the recommended first step, but as the cheapest route to validity — not,
  as previously stated, the only strategy that could ever exist.
- Regenerating records is legitimate once the layouts are known, which is a documentation problem rather
  than a hosting problem.

#### Correction to Q2c: separators are fatal too, independently of shortcuts

The Q2 isolation above concluded "flattening alone still loads; flattening **with** a shortcut fails". That
generalised from a hand-built two-item fixture with no separator, and it is wrong.

The corpus-wide build gate (`Vb6OracleRoundTripTests`) shows menu flattening breaks VB6 for **two
independent reasons**:

```
Line 20: Cannot set shortcut property in menu mnuEditUndo. Parent menu cannot have a shortcut key.
Line 25: Parent menu mnuFileBar1 cannot be loaded as a separator.
```

A separator (`Caption = "-"`) promoted to top level is rejected exactly as a shortcut is. Three of the five
broken corpus forms — `Explorer File Menu`, `Help Menu`, `View Menu` — carry **zero** shortcuts and fail
purely on separators.

So the affected set is not "menus with shortcuts" but "menus with shortcuts **or** separators", i.e.
essentially every real menu. Recorded against #22.

**Method note worth keeping:** the hand-built experiment was decisive about the mechanism and wrong about
the scope, because a fixture only exercises what its author thought to include. The corpus gate found the
second cause on its first run. Prefer running the corpus over reasoning from a minimal repro when the
question is *how much* rather than *why*.

