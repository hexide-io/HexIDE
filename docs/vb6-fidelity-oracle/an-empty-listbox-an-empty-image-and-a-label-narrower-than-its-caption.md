## An empty ListBox, an empty Image and a Label narrower than its caption, in VB6's designer and a run (2026-11-21, #927, #860, #921, SP6)

**The probe.** One form, built with `/make` as native code, run on the guest's console, and opened in VB6's designer
(VB6.EXE 6.00.9782, SP6, 96 dpi). Screens came through the host's framebuffer. It held:
- List1, an empty ListBox, 1935 × 1035;
- List2, the same size, saved with one item, `item`;
- Image1, an empty Image (no Picture), 1215 × 975;
- three Labels, each 1500 × 615 (100 × 41 pixels), with BorderStyle 1 to show their edges, AutoSize False, and
  WordWrap False, the default:
  - Label2: caption `This caption is wider than its label`, Alignment 0;
  - Label3: the same caption, Alignment 2 (Center);
  - Label4: caption `abc`, Alignment 2.

A second form, built the same way and opened by the mouse, added Label5: Label2's caption and size, but with WordWrap True.

**An empty ListBox (#927):**
- **The designer draws the list's name inside it.** It reads `List1` at the top-left, where the first item would be.
- **A list with an item draws the item, and no name.** List2 showed `item`.
- **A run draws nothing in the empty list,** apart from the focus rectangle, since List1 was first in the tab order.

**An empty Image (#860):**
- **The designer draws a dashed outline round the Image's rectangle,** and nothing inside it. The dashes are 3 pixels
  on and 3 off, in a grey read as `636563` through the framebuffer's RGB565, which fits `646464`. The designer's grid
  dots show through the gaps.
- **A run draws nothing** where the Image is.

**A Label narrower than its caption (#921):**
- **The caption wraps with WordWrap False,** in a run and in the designer alike. It breaks between words:
  `This caption is wider` on the first line and `than its label` on the second.
- **With Alignment 2, each line is centred on its own,** and `abc` is centred in Label4.
- **WordWrap True wraps the same:** Label5 broke at the same place, in a run and in the designer.

So WordWrap doesn't decide whether a Label of fixed size wraps. Without AutoSize it wrapped at its width with
WordWrap False and with True, and
[AutoSize on a Label](autosize-on-a-label-and-a-picturebox.md) records what WordWrap does once AutoSize is on.
#921 gave VB6's documentation as saying this, unmeasured; this is the measurement.

### Not measured

- **A single word wider than the Label:** whether it breaks inside the word or is cut off.
- **A Label too short for the lines it wraps to.**
- **A DPI other than 96.**
