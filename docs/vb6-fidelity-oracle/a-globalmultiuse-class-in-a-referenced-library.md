# A GlobalMultiUse class in a referenced library

Provenance: the measurement comment on [#1107](https://github.com/hexide-io/HexIDE/issues/1107),
reported by hexide-int and accepted by the supervisor on 2026-10-26. The comment describes vb6.exe SP6
runs of Sub Main EXEs referencing two GlobalMultiUse ActiveX DLLs, compiled both natively and as p-code.
It points to PR #2125 for the probe sources and logs; those are not present in this checkout. This entry
records the reported observations, not a new measurement by this implementation run.

- Referencing a library alone creates no instance. Each GlobalMultiUse class initializes on its own first
  member call, including when another class in that library has already been used.
- Calls share one instance per class throughout the run. A library-qualified call shares that instance
  with bare calls, and state survives between calls.
- Explicit `New` creates a separate instance with its own Initialize and Terminate.
- The implicit instances terminate after Sub Main's final statement, when Main returns. `End` suppresses
  all Terminate hooks.
- Within one library, implicit instances terminate in reverse creation order. Across libraries native
  and p-code gave different orders; this implementation's tests deliberately assert no such order.
- A project procedure wins over a bare library member and creates no library instance. Otherwise the
  first library in `.vbp` Reference order wins, without an ambiguity error or creation of the loser.
- A library's GlobalMultiUse `Trim` hides VBA's bare `Trim`. Explicit `VBA.Trim` still invokes VBA and
  `HexLibA.Trim` invokes the library.

The supervisor's implementation contract uses managed `ICSharpProxy` factories, with `New` and
`CreateObject` calling those factories. This is a host integration decision, not an additional VB6
measurement. Unknown libraries and COM hosting remain outside #1107.
