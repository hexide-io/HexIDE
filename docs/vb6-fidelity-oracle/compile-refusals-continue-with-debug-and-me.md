# Compile refusals: Continue Do, With Debug and a private field through Me

Provenance: [HexIDE #2121](https://github.com/hexide-io/HexIDE/issues/2121), its body and subsequent
measurement comment, from the language-engine parity run (#1368). The recorded compiler was **vb6.exe SP6**,
invoked with `/make`; native and p-code builds gave the same compile errors. These are the supplied
measurements, not new compiler runs in the implementation session.

| Probe | Compiler result |
|---|---|
| `Continue Do` inside `Do ... Loop Until` | `Syntax error`, on the `Continue Do` line |
| `With Debug` / `.Print 1` / `End With` | `Syntax error`, on the `With Debug` line |
| A class declares `Private mN As Integer`, then one of its methods assigns `Me.mN = 5` | `Method or data member not found` |

HexIDE previously executed all three. It now raises these compile errors when the walk reaches the
construct; an untaken path is not checked. The grammar retains `Continue Do` so its executor can report
the measured message through the existing compile-error mechanism.

The private-field measurement covers a scalar assignment through explicit `Me.` in a class. It supplies
no separate results for reads, private methods/properties, `Set`, indexed stores, module-level `Dim`,
forms or access through another reference. Those forms retain their existing behaviour.
