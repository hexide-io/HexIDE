## A form that declares both rectangles (2026-11-21, #21, IDE, SP6)

**The question.** VB98's `Template\Forms\Dialog.frm` gives its form both rectangles: the client one (`ClientLeft`,
`ClientTop`, `ClientWidth`, `ClientHeight`) and the outer one (`Left`, `Top`, `Width`, `Height`). #21's findings list
asks, as Q4, which one VB6 honours when the two disagree.

**The probe.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM, driven on its console. One Standard EXE held three
copies of `Dialog.frm`, each renamed and given a marker comment. Only these lines varied:

| Form | Client height × width | Outer height × width | Scale height × width |
|---|---|---|---|
| D0 | 3195 × 6030 | 3600 × 6150 | 3195 × 6030 |
| D1 | 3195 × 6030 | 3195 × 6030 | 3195 × 6030 |
| D2 | 2000 × 4000 | 3600 × 6150 | 2000 × 4000 |

- **D0** is the template as shipped. Its outer rectangle is its client one plus a Fixed Dialog's frame, 405 by 120.
- **D1** makes the outer rectangle equal to the client one.
- **D2** gives a client rectangle much smaller than the outer one, with the scale following the client.

The project loaded with no message. A Replace All of the marker over Current Project made three replacements, and File
> Save Project wrote all three forms.

**What VB6 saved:**

| Form | Client height × width | `Height`, `Width`, `Left`, `Top` lines | `ClientLeft`, `ClientTop` |
|---|---|---|---|
| D0 | 3195 × 6030 | **gone** | 2760, 3750, kept |
| D1 | 3195 × 6030 | **gone** | kept |
| D2 | **1995 × 4005** | **gone** | kept |

- **VB6 honours the client rectangle.** D1's outer size, equal to its client size, changed nothing, and D2's client
  size was kept whatever its outer size said.
- **Its save drops the outer rectangle.** None of the three forms kept `Height`, `Width`, `Left` or `Top`.
- **D2's client size came back as whole pixels at 15 twips each:** 2000 as 1995 (133 pixels) and 4000 as 4005 (267
  pixels). Its `ScaleHeight` and `ScaleWidth` followed, 1995 and 4005.

### Not measured

- **A sizable form,** or one without a border, where the frame differs.
- **What the running form's window measures** in each case. Only the saves were read.
- **The release build.**
