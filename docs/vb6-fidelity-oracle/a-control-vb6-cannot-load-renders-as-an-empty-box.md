## A control VB6 cannot load renders as an empty box (2026-09-01, design-time)

**Verified**, by the human-in-the-loop method in *Note on method* above: a `.frm` citing an unregistered
OCX pushed into the VM, opened in `VB6.EXE`, screenshotted.

VB6 draws a control it cannot load as a **plain etched rectangle at the recorded position and size** —
nothing inside it. No diagonals corner-to-corner, no X, no type name, no control name, no icon. Just an
empty sunken frame where the control would be.

The recollection this was checked against was "an empty box with diagonal lines from each corner". That is
wrong **for VB6**, and it is the sort of detail worth having right before anyone draws one.

It may well be right about something else — VBA's editor, or a later Visual Studio — and that is the point
rather than a footnote. A crossed placeholder is a real thing someone has genuinely seen; it just is not
this product. This is the hazard `CLAUDE.md` names when it says VBA diverges from VB6 at the edges and
`vb6.exe` always wins: a memory can be perfectly accurate and still be about the wrong host. Which host it
came from was not worth chasing, because the only question that mattered — what VB6 draws — now has a
screenshot behind it.

The probe used a made-up GUID (`{A1B2C3D4-1111-2222-3333-444455556666}`, `NOSUCH.OCX`) with a real
`CommandButton` beneath it for scale; the box's width matched the button's `3015` twips, so the geometry
comes straight from the designer file's `Left`/`Top`/`Width`/`Height`.

### Why HexIDE should NOT copy it exactly

VB6's box carries meaning it does not itself supply: the developer reached it by dismissing a *"cannot load
control"* dialog seconds earlier, so an unlabelled rectangle is enough to say "the thing you were just told
about goes here".

HexIDE has no such dialog, and its box would mean something different — **not** "this OCX is missing from
this machine" but "HexIDE does not model this control", which stays true on a machine where the OCX is
correctly installed and registered. A developer following VB6 muscle memory would install the control,
reopen, see the same empty box, and reasonably conclude HexIDE is broken.

So: keep the geometry, which is VB6's intended behaviour and is exactly reproducible from the file; replace
the emptiness, which is a shortcoming rather than a design. Per the Fidelity Principle, a divergence — and
recorded here so it stays deliberate.

### Its size when the file records none, and what the box is (2026-10-22, #157)

**The question.** The probe above had a `Width` and `Height`. An ImageList records neither: `Web Browser.frm`'s has
`Left`, `Top`, `_ExtentX` and `_ExtentY` (HIMETRIC) and nothing else. The obvious guess is that the extents size it.

**The probe.** One form holding three controls of an unregistered class (a made-up GUID, `NOSUCH.OCX`), each beside a
real CommandButton of the size it should come out as. It was opened in VB6's IDE through the console agent. Each
control was then selected with Tab, and its position and size read from the toolbar's readout.

| Control | What the file records | VB6's readout |
|---|---|---|
| `Ext1` | `Left` 240, `Top` 240, `_ExtentX` 3528, `_ExtentY` 1764 (2000 × 1000 twips) | 240, 240, **1200 × 480** |
| `Sized1` | `Left` 240, `Top` 1680, `Width` 3015, `Height` 495, extents for 1000 × 500 | 240, 1680, **3015 × 495** |
| `Bare1` | `Left` 240, `Top` 3000, nothing else | 240, 3000, **1200 × 480** |

**What it shows:**
- **`Width` and `Height` size the box when the file has them, and 1200 × 480 twips when it has neither.** The extents
  play no part, so the guess was wrong: `Web Browser.frm`'s ImageList comes out 1200 × 480. A file recording only one
  of the two was not probed.
- **The box is a PictureBox.** The Properties window names each one "`Ext1` PictureBox". VB6's load log says first
  "Class NoSuchLib.Extent of control Ext1 was not a loaded control class", then "The property name _ExtentX in Ext1
  is invalid", and the same for `_ExtentY` and `_Version`. A PictureBox has none of the three. What VB6 writes when
  such a form is saved was not probed.
- **So its border is a PictureBox's, the sunken one.** The section above calls it both "etched" and "sunken"; it is the
  second. HexIDE's box (#157) is drawn sunken to match, and labelled, which is the divergence argued above.

