# VB6 fidelity oracle

The oracle is now [indexed by subject](vb6-fidelity-oracle/README.md). This redirect preserves
references in immutable archived design records; new references should link directly to a subject file.

The #2062 brief's measured load-time font inheritance is recorded in
[Form font inheritance](vb6-fidelity-oracle/form-font-inheritance.md).

The #178 issue comments' measured whole-record copy and array behavior is recorded in
[A UDT copied whole, and arrays of a UDT](vb6-fidelity-oracle/a-udt-copied-whole-and-arrays-of-a-udt.md).

Issue [#2320](https://github.com/hexide-io/HexIDE/issues/2320) cites the debugger measurements in
[`debugger-vb6-divergences.md`, D4](debugger-vb6-divergences.md#d4-step-into-past-the-end-of-a-procedure-returns-to-the-caller-a-top-level-handler-disarms):
a step leaving the outermost event handler is cleared and the next event runs freely. This is the
recorded basis for retaining dispatch-idle disarming. The issue explicitly leaves VB6's title and toolbar
state there unmeasured; the shell notification fix does not claim a measurement of that appearance.
