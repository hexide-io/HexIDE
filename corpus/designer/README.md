# Designer-file corpus

Three files that exist because nothing else in the repository is one: an ActiveX control project with a
`.ctl` **and** a `.pag`. `WholeFileGrammarTests`, in both halves, parses every corpus file whole — header
and designer block included — and without these two the `.ctl` case rested on a VB6 install that only some
machines have, and the `.pag` case on nothing at all.

They are here rather than in `demo/` because they are not a demo: nothing about them is worth showing, and
`demo/README.md` describes each project there as something to open and look at. They are here rather than
copied out of the VB98 Template tree because that tree is Microsoft's, and this repository's corpus is
[authored rather than borrowed](../../docs/vb6-grammar-fixes.md#why-the-corpus-is-authored-rather-than-borrowed-2026-09-02)
— the same reason the conformance corpus next door is generated here.

## Provenance, stated exactly

**Authored here and compiled by the real compiler; not written by the VB6 IDE.**

`vb6.exe /make` on the oracle VM, 2026-09-20, answered:

```
Build of 'PagProbe.ocx' succeeded.
```

So VB6 accepts every line of these files, which is the property the grammar tests turn on. What cannot be
claimed is that these are byte-for-byte what VB6's *editor* would write, because the editor cannot be
driven on that VM: a GUI needs an interactive session, and PowerShell Direct lands in session 0 with no
desktop (measured — a process started there has no window handle and `AppActivate` cannot find it).

The shapes that actually matter to a parser were put in deliberately, taken from what real VB6 output
looks like: the trailing space after the identifier on a `Begin` line, and a decoded enumerated value
carrying its comment (`PaletteMode = 0  'Halftone`). That second one is the whole of
[grammar fix 7](../../docs/vb6-grammar-fixes.md) — it is what every `.cls` VB6 writes, and both grammars
rejected it until 2026-09-20.

Upgrading these to IDE-written files needs an interactive session in the VM (autologon, or a login at the
console, then `schtasks /IT`). Worth doing the next time a question is "what does VB6 *write*" rather than
"what does VB6 *accept*".

## Files

| File | What it is for |
|---|---|
| `Gauge.ctl` | A UserControl with a persisted property, so `ReadProperties`/`WriteProperties` and a `Property Get`/`Let` pair are in the body |
| `GaugeGeneral.pag` | The PropertyPage — the only `.pag` anywhere in the corpus |
| `PagProbe.vbp` | `Type=Control`, so the two above are reachable as a project and the build above is reproducible |
