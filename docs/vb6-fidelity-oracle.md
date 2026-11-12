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

Issue [#2320](https://github.com/hexide-io/HexIDE/issues/2320) cites the debugger measurements in
[`debugger-vb6-divergences.md`, D4](debugger-vb6-divergences.md#d4-step-into-past-the-end-of-a-procedure-returns-to-the-caller-a-top-level-handler-disarms):
a step leaving the outermost event handler is cleared and the next event runs freely. This is the
recorded basis for retaining dispatch-idle disarming. The issue explicitly leaves VB6's title and toolbar
state there unmeasured; the shell notification fix does not claim a measurement of that appearance.

The #788 comments' measured hex/octal String conversion widths, leading zeros and malformed inputs are
recorded in [Assigning to a control property](vb6-fidelity-oracle/assigning-to-a-control-property.md).

The #1483 comments' project-name and sourceless Raise measurements are recorded in
[Err.Source after a run-time error and after Err.Raise](vb6-fidelity-oracle/when-err-is-cleared-and-what-a-running-handler-traps.md#errsource-after-a-run-time-error-and-after-errraise-1483).

The #2325 comments’ measured keyboard input is recorded in
[Key events](vb6-fidelity-oracle/key-events.md). The later native/p-code whole-keyboard sweep
records Ctrl+Backspace as KeyCode 8, Shift 2 and KeyAscii 127 in
[the TextBox keyboard table](vb6-fidelity-oracle/key-events-keydown-keypress-and-keyup.md#the-whole-keyboard-on-a-textbox)
(PR #2279, ac291b2a; issue comment 6454480155).

The #2474 measurements of declared arrays named with empty parentheses are recorded in
[Whole-array parentheses](vb6-fidelity-oracle/whole-array-parentheses.md).

The #2523 issue records vb6.exe SP6 measurements (native and p-code) for Pmt, FV, PV, NPer,
IPmt, PPmt and Rate: exact Double bits, zero-rate cases, timing, optional arguments and errors.
The [issue and supervisor's precision experiment](https://github.com/hexide-io/HexIDE/issues/2523)
are the measurement source; [Financial precision](financial-precision-2523.md) describes HexIDE's
model comparison, which is a computation against those measurements, not a new VB6 measurement.
The #881 comments’ measured compare validation order is recorded in
[A compare argument, and when it is checked](vb6-fidelity-oracle/compare-argument-validation.md).

The #2622 brief records StrPtr and VarPtr relations measured by hexide-int with vb6.exe SP6,
with matching native and p-code results: null/unassigned String buffers are zero, assigned empty
text has a buffer, assignments copy buffers, variable identities are stable and distinct, and
Long/Integer/Currency/Variant array elements step by 4/2/8/16 bytes. ByRef shares the caller's
variable and String buffer identities; ByVal has separate identities. String variable and buffer
identities differ. Both intrinsics return Long, including non-zero temporary identities for `5`.
These constrain relations rather than address values. Provenance: [issue #2622](https://github.com/hexide-io/HexIDE/issues/2622),
measuring PRs [#2624](https://github.com/hexide-io/HexIDE/pull/2624) and
[#2628](https://github.com/hexide-io/HexIDE/pull/2628); the measuring authors own their oracle pages.

The ParamArray forwarding measurement supplied by hexide-int in
[#2474](https://github.com/hexide-io/HexIDE/issues/2474) and restated in
[#2532](https://github.com/hexide-io/HexIDE/issues/2532) agrees in native and p-code:
`T a()` inside a ParamArray procedure supplies one element containing a copy of the whole
array to T's ParamArray. Writes through that nested array leave the caller's ParamArray unchanged.

The #2497 correction to form termination order, measured by #2496 in native and p-code builds, is recorded in
[The Forms collection](vb6-fidelity-oracle/the-forms-collection.md#the-terminates-at-the-runs-end).

Completion acceptance now follows the selected/unselected Enter, Tab and Space behavior and unique-prefix
Complete Word behavior measured by hexide-int in VB6 SP6 for
[#3056](https://github.com/hexide-io/HexIDE/issues/3056#issuecomment-6633933454).
The issue contains the measurement; this run changes the editor, not the measurement record.
Snippet-choice Enter accepts without a line break by the supervisor's decision in that issue (VB6 has no snippets).

The #3210 brief records VB6 SP6 p-code building **7,227 nested If blocks**, with 7,228 refused
as "Procedure too large"; sequential blocks have the same limit, so this is a size limit.
Its **7,643 parenthesis pairs** reference is recorded in
[long lines and expressions](vb6-fidelity-oracle/long-lines-line-continuations-and-long-expressions.md).
Provenance: [#3210 measurements](https://github.com/hexide-io/HexIDE/issues/3210), Win10 oracle guest,
VB6 SP6, `/make` and IDE. These are acceptance references, not a measured conditional-compilation limit.

The #3210 fix brief also supplies host-side parser costs from
[measurement PR #3216](https://github.com/hexide-io/HexIDE/pull/3216): approximately **3.6 KB per
parenthesis pair** and **2.8 KB per nested If**, the latter spanning about five parser rule frames.
These are host stack measurements, not VB6 limits; the [runtime gap note](interpreter-gaps.md) records
how HexIDE derives its guarded bounds from them.

The standard-module Get/Let/Set measurements are recorded in
[A standard module's Property procedures](vb6-fidelity-oracle/a-standard-modules-property-procedures.md)
(measuring PR [#2279](https://github.com/hexide-io/HexIDE/pull/2279)).
The later [#977 measurements](https://github.com/hexide-io/HexIDE/issues/977), by hexide-int on
2026-10-28, establish that a class's or form's own bare property takes precedence over a
standard module's Public property, that a class without its own uses the module's, that a
Let-only property accepts bare and qualified assignments, and that a bare getter statement
inside its class is Invalid use of property. VB6 SP6 native and p-code agreed.

The [#2938 measurements](https://github.com/hexide-io/HexIDE/issues/2938) by hexide-int
(vb6.exe SP6, `/make`, 2026-11-03, matching native and p-code) reject `Show 1,`,
`Call Show(1, )` and `x = F(1, )` with "Syntax error", even with `Optional b = 7`.
`Show 1` builds and supplies 7; omissions between supplied arguments remain valid, as recorded in
[omitted optional arguments](vb6-fidelity-oracle/omitted-optional-arguments-and-string-operands-to-numeric-intrinsics.md).
A module-level or local `Dim Arr()` followed inside a procedure by `ReDim Arr(10) As String`
is refused with "Can't change data types of array elements". The scalar Variant control,
`Dim Arr`, builds, and storing "Hello" reads back "Hello" with TypeName `String()`.
The array probes are also recorded in [ReDim Preserve](vb6-fidelity-oracle/redim-preserve.md).
These are the issue author's measurements, not new measurements by this implementation run.

Issue [#3350](https://github.com/hexide-io/HexIDE/issues/3350) uses
[#3349’s Public object-module member measurements](vb6-fidelity-oracle/a-public-array-in-an-object-module.md):
Public constants, fixed-length strings, UDT variables and Declare statements receive the same compile refusal
as Public arrays. HexIDE checks UDT members later, while allocating instance storage.

The [#3275 measurements](https://github.com/hexide-io/HexIDE/issues/3275), made by hexide-int
with VB6 SP6 on the Win10 guest in matching native and p-code builds, establish ListBox Scroll:
scrollbar movement, keyboard movement, WM_VSCROLL and TopIndex assignments raise it once per
changed top row; unchanged rows, wheel input and ListIndex assignments do not. Keyboard Click
precedes Scroll, and both see the updated TopIndex. TopIndex clamps to the last page and rejects
indices below zero or at/above ListCount with 380. The measuring author's page belongs to
[PR #3286](https://github.com/hexide-io/HexIDE/pull/3286).

The [#3275 supervisor correction](https://github.com/hexide-io/HexIDE/issues/3275) requires
TopIndex to use the existing Integer store coercion: Long and numeric Variant assignments
are accepted, and Integer overflow raises error 6. This is supplied provenance, not a new
measurement by this implementation run.

The [#2525 measurements](https://github.com/hexide-io/HexIDE/issues/2525) of NPV, IRR and MIRR
are recorded in the measuring author's [cash-flow oracle page](vb6-fidelity-oracle/npv-irr-and-mirr.md).
They pin exact Double bits and errors for Double cash-flow arrays, including NPV's first-value
discount, its rate -1 and unallocated-array error 5, IRR's guess-dependent bits and all-positive
error 5, and MIRR's all-positive error 11 and finance-rate -1 error 5. Native and p-code agree.
[Cash-flow precision](financial-precision-2525.md) records model comparisons, not new measurements.

The #2626 resource function measurements (vb6.exe SP6, matching native and p-code runs) are recorded in
[LoadResString, LoadResData and LoadResPicture](vb6-fidelity-oracle/loadresstring-loadresdata-and-loadrespicture.md).
The measuring authors' page remains the source for string, byte-array, bitmap and error results.

Issue [#3778](https://github.com/hexide-io/HexIDE/issues/3778) uses the native and p-code measurements
from [An undeclared name before a dot](vb6-fidelity-oracle/an-undeclared-name-before-a-dot.md)
(measuring PR [#3780](https://github.com/hexide-io/HexIDE/pull/3780)): without Option Explicit,
the probed undeclared object receivers raise trappable 424 and remain Empty.
