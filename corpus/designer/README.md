# Designer-file corpus

An ActiveX control project with a `.ctl` **and** a `.pag`, and an MDI-form fixture with a child-form
project. `WholeFileGrammarTests`, in both halves, parses every corpus file whole — header and designer
block included. Without the ActiveX pair, the `.ctl` case rested on a VB6 install that only some machines
have, and the `.pag` case on nothing at all. The MDI fixture covers the root that #5313 originally dropped.

They are here rather than in `demo/` because they are not a demo: nothing about them is worth showing, and
`demo/README.md` describes each project there as something to open and look at. They are here rather than
copied out of the VB98 Template tree because that tree is Microsoft's, and this repository's corpus is
[authored rather than borrowed](../../docs/vb6-grammar-fixes.md#why-the-corpus-is-authored-rather-than-borrowed-2026-09-02)
— the same reason the conformance corpus next door is generated here.

## Provenance, stated exactly

**Written by VB6 SP6's IDE (VB6.EXE 6.00.9782), 2026-11-21.**

On a clone of the oracle guest, the IDE opened the hand-authored `PagProbe.vbp`, added a CommandButton to
`Gauge` and a Label to `GaugeGeneral` by double-clicking their toolbox icons, then saved with the toolbar's
Save Project button. All three files here are the resulting CRLF bytes, including the trailing spaces on
`Begin` lines. The files and their SHA-256 hashes are recorded in
[#485's measurements](https://github.com/hexide-io/HexIDE/issues/485).

The hand-authored versions had three things wrong that VB6 corrected:

- `PagProbe.vbp` was missing `Command32=""`, which VB6 added after `HelpFile=""`.
- `PagProbe.vbp` had `StartMode=0`; VB6 wrote `StartMode=1`.
- `GaugeGeneral.pag` had `Attribute VB_Exposed = False`; VB6 wrote `True`.

VB6 also made the OLE Automation reference's path relative to the guest folder it saved in:
`..\..\Windows\SysWOW64\stdole2.tlb`. The `UserControl=` and `PropertyPage=` item lines kept their order.
`Gauge.ctl` changed only by gaining the `Command1` block; `GaugeGeneral.pag` also gained the `Label1` block.
The headers and code otherwise survived, including the `PaletteMode = 0  'Halftone` comment that exposed
[grammar fix 7](../../docs/vb6-grammar-fixes.md).

The earlier hand-authored versions were compiler-validated: `vb6.exe /make` on the oracle VM, 2026-09-20,
answered:

```
Build of 'PagProbe.ocx' succeeded.
```

That is the 2026-09-20 compile result, not a new compile of the IDE-saved files.

## Files

| File | What it is for |
|---|---|
| `Gauge.ctl` | A UserControl with a persisted property, so `ReadProperties`/`WriteProperties` and a `Property Get`/`Let` pair are in the body, and one CommandButton nested in its designer block |
| `GaugeGeneral.pag` | The PropertyPage — the only `.pag` anywhere in the corpus — with one Label nested in its designer block |
| `PagProbe.vbp` | `Type=Control`, so the two above are reachable as a project |
| `mdi/MDIForm1.frm` | A new MDI form saved by VB6 SP6 on 2026-11-23; the exact 478 bytes and SHA-256 are recorded in [#5313](https://github.com/hexide-io/HexIDE/issues/5313) |
| `mdi/Form1.frm` | Hand-authored child-form input with `MDIChild = -1`; no claim of VB6-saved bytes |
| `mdi/MDIProbe.vbp` | Hand-authored project input listing both forms with ordinary `Form=` lines, starting at MDIForm1 |

The MDI fixture is separate from the ActiveX project described above. Only `MDIForm1.frm` is the measured
VB6 output; its companion project and child are authored harness inputs. `SerializationCorpusTests` always
reads this directory, even without `HEXIDE_ROUNDTRIP_CORPUS`. `ProjectCorpusSmokeTests` also loads and saves
its projects; running the MDI startup form is explicitly refused pending the runtime slice of #5313.
