## When a document gets a file, and what a dangling `Startup=` does (2026-09-20, #489)

Driven against the real VB6 **IDE** in the oracle guest (see [Reaching the VB6 *IDE*, not just the
compiler](README.md#reaching-the-vb6-ide-not-just-the-compiler-2026-09-20)), with a screenshot after every gesture and
a full filesystem inventory — the project directory, `%TEMP%`, `VB98\`, `Documents\` — diffed against a
baseline taken before VB6 started.

### VB6 never writes a component's file until you ask it to

| gesture | what appeared on disk |
|---|---|
| `File > New Project > Standard EXE` | nothing — no `Form1.frm`, no `Project1.vbp` |
| `Project > Add Module`, on that unsaved project | nothing |
| `Project > Add Form`, on an **already-saved** project | nothing; the `.vbp` was not rewritten either |
| `File > New Project`, answering **No** to *Save changes* | nothing, anywhere, `%TEMP%` included |

Across the whole session the only file VB6 wrote that was not asked for was a 16 KB `~DF….TMP` in the user's
`Temp`, created when the project was created and never touched again.

> **A component with no file is a first-class, persistent state in VB6, not a transient one.** The Project
> Explorer renders it `Form2 (Form2)` — the name repeated where a filename goes — and the *Save changes to
> the following files?* prompt lists it as a bare `Form2` beside `Project1.vbp`.

This settles hexide-io/HexIDE#489 in favour of **never write until the project is saved**.

**And HexIDE does not do that today, which this measurement is what established.** The sentence that first
stood here said route B "was already the route the tree was shaped for". That was wrong, and it was wrong in
the way this file exists to catch: it generalised a true statement about the *consumers* — the interpreter,
the serializers and the sidecar all work from memory and never ask where a document lives — into a false one
about the *creation* paths. Driving the real IDE is what showed the difference, and only because the gesture
was performed rather than reasoned about:

| how a document comes into being in HexIDE | has a file at once? |
|---|---|
| `File > New Project`, its initial `Form1` | **no** — `IProjectTemplate.StandardEXE` leaves the path null and nothing writes |
| `--newproject`, its initial `Form1` | yes — `DesktopStartup` saves the whole project into `%TEMP%` straight away |
| `Project > Add Form` / `Add Module` / `Add Class Module` / `Add User Control` / `Add Property Page` | **yes** — each mints `%TEMP%\hexide_<Name>_<guid>`, assigns the path and writes the file (`ProjectService.AddNewForm` and its four siblings) |

So HexIDE is a hybrid of routes A and B, which is precisely the inconsistency #489's title reported: two Add
commands answering differently. Reaching route B is implementation work, filed separately; this row of the
table is the oracle's part of it.

### `Save Project` asks for every pathless file first, and the project last

On a never-saved project holding `Form1` and `Module1`, one `File > Save Project` produced three dialogs, in
this order:

1. **Save File As** — `Module1`, filter `Basic Files (*.bas)`
2. **Save File As** — `Form1`, filter `Form Files (*.frm)`, in whatever directory step 1 settled on
3. **Save Project As** — `Project1`, same directory

So HexIDE's ordering — per-file pickers before the project's — is **fidelity, not a defect.** That is the
opposite of the obvious guess, which is worth recording because the obvious guess is what prompted the
measurement.

Two details are deliberately left ungeneralised:

- **`Module1` came before `Form1`.** It was also the last component added *and* the owner of the active MDI
  child, so kind, recency and activation are confounded here. None of the three is established as the rule.
- **The first picker's default directory was the VB6 process's current directory**, which the harness had set
  with `Start-Process -WorkingDirectory`. What a normally-launched VB6 defaults to was not measured.

### For a pathless document in a *saved* project, the picker defaults to the project's own directory

Adding `Form2` to the saved `Project1` and choosing `Save Project` produced exactly **one** dialog: *Save
File As*, `Save in:` the project's own folder, `File name: Form2` — the component's `Name`. `Form1`,
`Module1` and the `.vbp` itself were rewritten with no prompt at all. VB6 asks, but it asks with the right
answer already filled in.

### Cancelling that picker abandons the whole save

`{ESC}` on `Form2`'s picker left `Project1.vbp` byte-identical to the previous save — same 585 bytes, same
mtime — with no `Form=Form2.frm` line and no partial write of any kind.

> **VB6 will not write a `.vbp` that names a file it did not manage to save.** Save Project is atomic with
> respect to a cancelled picker.

### A `Startup=` that names an undeclared object is silently ignored

HexIDE's serializer drops an item line when the document has no path, and writes the `Startup=` line
unguarded — so it can emit a `.vbp` whose `Startup="Form1"` matches no item in the file. Four `/make` probes,
`/out` log quoted verbatim:

| `.vbp` | `vb6.exe` says | exit |
|---|---|---|
| `Module=Module1; Module1.bas` + `Startup="Sub Main"` *(control)* | `Build of 'P.exe' succeeded.` | 0 |
| `Module=Module1; Module1.bas` + `Startup="Form1"`, no `Form=` line | `Build of 'P.exe' succeeded.` | 0 |
| `Startup="Form1"`, no item lines at all | `Must have startup form or Sub Main()` | 1 |
| `Form=Form1.frm` + `Startup="Form1"`, the file absent | `File not found: '…\Form1.frm'` | 1 |

Row 2 is the one that matters, and *builds* is not *runs what you asked for* — so it was run. `Sub Main` was
given a body that writes a marker file; after `P.exe` exited, the marker was there.

> **VB6 compiles that project, exit 0, with no warning, into a program whose entry point is `Sub Main`
> rather than the form the project names.** The `.vbp` is not rewritten, so nothing on disk records the
> substitution.

Opening the same `.vbp` in the VB6 **IDE** is equally quiet: the project loads, the tree shows `P (P.vbp)`
with only `Modules` beneath it, and nothing is said about a `Startup=` naming an object that is not there.

This raises the severity of HexIDE's silent drop from *loses a document* to *silently changes the program's
entry point* — a wrong answer that runs, which is the one outcome `docs/serialization-outcomes.md` forbids
outright. Note where the divergence actually lies: VB6 is silent when **reading** such a file too, so the
defect is not the quiet read. It is that HexIDE **writes** a file VB6 would never have produced, because
VB6's own save is atomic across the pickers.

Related: [A `.vbp` item line has two shapes, and the keys do not agree on which
(2026-09-20)](a-vbp-item-line-has-two-shapes-and-the-keys-do-not-agree-on-which.md#a-vbp-item-line-has-two-shapes-and-the-keys-do-not-agree-on-which-2026-09-20), which is the
other way HexIDE's serializer has emitted a project VB6 reads differently from HexIDE.

---

