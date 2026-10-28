# take_snapshot writes every capture to the same file — **CLOSED** (#784 for #756, 2026-10-12)

> **Fixed by #784, verified live on main a816e0d.** Two `take_snapshot {}` calls, with the Immediate window
> shown between them, returned `hexide_snapshot_20261012-142646-333_1.png` and
> `hexide_snapshot_20261012-142648-443_2.png`. Both files remained, with different sizes and hashes, and the
> second showed the change. The naming is tested in `SnapshotCapturePathTests`. `export_lsp_conversation`'s
> fixed name was not part of the fix.

**Symptom.** With a program running:

```
take_snapshot {}                → "path": "<user temp>\\hexide_snapshot.png"   (the running program's form)
take_snapshot {"window":"ide"}  → "path": "<user temp>\\hexide_snapshot.png"   (the IDE)
```

The file name is fixed in `TakeSnapshotAsync`, so the second capture replaced the first. A caller taking
a before/after pair, or the form and then the IDE, loses the first image, and neither the description nor
the reply says the next call reuses the path. `export_lsp_conversation` writes a fixed name too, read in
code and not measured.

**Workaround.** Read or copy each snapshot away before the next `take_snapshot` call.

**Suggested fix.** Write each capture to its own file, named from a timestamp and a per-process counter,
and say in the description that earlier captures are left in place.
[#756](https://github.com/hexide-io/HexIDE/issues/756).
