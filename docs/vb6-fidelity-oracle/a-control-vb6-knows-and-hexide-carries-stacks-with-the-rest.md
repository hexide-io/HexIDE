## A control VB6 knows and HexIDE carries stacks with the rest (2026-10-23, #1498, design-time, IDE, release build)

**The question.** The rule above was measured on intrinsic controls only. HexIDE carries a control it does not model as
the text it was read as, written back where it was read, so the designer never sees it. Does VB6 stack an OCX control
like the others?

**The probe.** VB6's own `Template\Forms\Web Browser.frm` in a one-form Standard EXE project, with `MSCOMCTL.OCX` and
`ieframe.dll` referenced. Its form-level blocks are `tbToolBar` (`MSComctlLib.Toolbar`), `brwWebBrowser`
(`SHDocVwCtl.WebBrowser`), `timTimer`, `picAddress` (holding `cboAddress` and `lblAddress`), then `imlIcons`
(`MSComctlLib.ImageList`). HexIDE models only the Timer, the PictureBox and what it holds. Through the console agent:
the project opened (VB6 raised "Error accessing the system registry" once, dismissed with Enter), Ctrl+R, the form,
Shift+F7, a real double-click on the CommandButton tool, a click on the new button, then the step below through the
Format menu's Order submenu, a click on the designer, and Ctrl+S. Each run started from a fresh copy of the template.

| Step | Form-level blocks VB6 wrote |
|---|---|
| CommandButton added | **Command1**, tbToolBar, brwWebBrowser, timTimer, picAddress, imlIcons |
| Added, then Send to Back | tbToolBar, brwWebBrowser, timTimer, picAddress, imlIcons, **Command1** |
| Added, Send to Back, then Bring to Front | **Command1**, tbToolBar, brwWebBrowser, timTimer, picAddress, imlIcons |

After Send to Back the button was hidden under the WebBrowser, its selection handles still showing.

**What it shows:**
- **An OCX control stacks with the intrinsic ones.** A new control goes in front of the toolbar, and Send to Back puts
  it behind the ImageList, a control with no window at run time. The Timer and the ImageList count as windowed: Send
  to Back went past both.
- **Nothing else moves.** Each step changed only Command1's place. `picAddress`'s own blocks were untouched.

**What HexIDE did:** a carried block kept the place it was read at, so the added button went in after the toolbar and
the WebBrowser, and the ImageList, read fifth, was written ahead of `picAddress`, a control nobody had moved. Send to
Back and Bring to Front moved a control past the modelled controls only.

**In HexIDE (#1498):** each control in the designer counts the carried controls in its container that are stacked under
it, read from the file on load. A control added, pasted or brought to front goes over all of them, and one sent to back
under them. A save turns the counts back into where each carried block goes, through the same placement the writer
uses, so a form loaded and saved untouched comes back byte for byte.

**In HexIDE (#1553):** the designer draws each carried control's box at its place in that stack too. It used to draw
every box over every modelled control, so a control brought in front of one was saved in front of it and still drawn
under its box. A box now goes over the last control in its container that has fewer carried controls under it than the
box has, or over the container itself where there is none.

**Inferred, not measured:** a carried block VB6 wrote after a Label is taken to be windowless, since VB6 writes every
windowed block first. A windowed control sent to back stays over such a block, and a Label sent to back goes under it.
No corpus file has a carried block after a windowless one.

**On SP6** (2026-10-24, VB6.EXE 6.00.9782, #1663). All three saves were byte-identical to the release build's, from the
unchanged template. At first, though, the template would not load:
- **The first load after the control caches changed** raised the same "Error accessing the system registry". On SP6,
  "Continue Loading Project?" followed it, and with Enter alone nothing loaded. On the release build the project loaded
  after that one Enter.
- **That load left a virtualised `MSCOMCTL.oca`** in the console user's VirtualStore, under `VB98`. Every load after it
  was clean, with no dialog. The release run left a virtualised `ieframe.oca` in the same way.
- **Not settled:** whether the second dialog belongs to SP6, or only to the cache it hit.

