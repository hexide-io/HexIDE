# VB6 fidelity oracle

The oracle is now [indexed by subject](vb6-fidelity-oracle/README.md). This redirect preserves
references in immutable archived design records; new references should link directly to a subject file.

The #2062 brief's measured load-time font inheritance is recorded in
[Form font inheritance](vb6-fidelity-oracle/form-font-inheritance.md).

The #1631 brief’s measured extender surface is recorded in
[A UserControl’s extender members at run time](vb6-fidelity-oracle/usercontrol-extender-members.md).

The #1631 brief's measured focus and key behaviour is recorded in
[A UserControl's focus and access keys](vb6-fidelity-oracle/usercontrol-focus-and-keys.md).

The #1631 brief's measured containers, drawing and events are recorded in
[A UserControl's containers, drawing and events](vb6-fidelity-oracle/usercontrol-containers-drawing-and-events.md).

The #1631 brief's measured PropertyPage HelpContextID and StandardSize file handling is recorded in
[PropertyPage file properties](vb6-fidelity-oracle/usercontrol-files.md#propertypage-file-properties).

The #178 issue comments' measured whole-record copy and array behavior is recorded in
[A UDT copied whole, and arrays of a UDT](vb6-fidelity-oracle/a-udt-copied-whole-and-arrays-of-a-udt.md).

The #788 comments' measured hex/octal String conversion widths, leading zeros and malformed inputs are
recorded in [Assigning to a control property](vb6-fidelity-oracle/assigning-to-a-control-property.md).

The #1483 comments' project-name and sourceless Raise measurements are recorded in
[Err.Source after a run-time error and after Err.Raise](vb6-fidelity-oracle/when-err-is-cleared-and-what-a-running-handler-traps.md#errsource-after-a-run-time-error-and-after-errraise-1483).

The #2325 comments’ measured keyboard input is recorded in
[Key events](vb6-fidelity-oracle/key-events.md).

The #2474 measurements of declared arrays named with empty parentheses are recorded in
[Whole-array parentheses](vb6-fidelity-oracle/whole-array-parentheses.md).

The #2523 issue records vb6.exe SP6 measurements (native and p-code) for Pmt, FV, PV, NPer,
IPmt, PPmt and Rate: exact Double bits, zero-rate cases, timing, optional arguments and errors.
The [issue and supervisor's precision experiment](https://github.com/hexide-io/HexIDE/issues/2523)
are the measurement source; [Financial precision](financial-precision-2523.md) describes HexIDE's
model comparison, which is a computation against those measurements, not a new VB6 measurement.
The #881 comments’ measured compare validation order is recorded in
[A compare argument, and when it is checked](vb6-fidelity-oracle/compare-argument-validation.md).

The ParamArray forwarding measurement supplied by hexide-int in
[#2474](https://github.com/hexide-io/HexIDE/issues/2474) and restated in
[#2532](https://github.com/hexide-io/HexIDE/issues/2532) agrees in native and p-code:
`T a()` inside a ParamArray procedure supplies one element containing a copy of the whole
array to T's ParamArray. Writes through that nested array leave the caller's ParamArray unchanged.

The #2497 correction to form termination order, measured by #2496 in native and p-code builds, is recorded in
[The Forms collection](vb6-fidelity-oracle/the-forms-collection.md#the-terminates-at-the-runs-end).
