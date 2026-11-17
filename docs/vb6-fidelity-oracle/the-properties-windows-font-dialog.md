## The Properties window's Font dialog: its controls, typed sizes and effects (2026-11-16, #4153, design-time, IDE, SP6)

The dialog the Properties window's Font "…" opens, for a form and for a TextBox at design time. Measured on vb6.exe
SP6's IDE in the oracle guest's console session, at 100% scaling (96 dpi).

**How it was read:**
- **Controls:** read through Win32 as raw window text, so an `&` survives.
- **Sizes:** each one was typed into the Size box with the font set to Arial, and OK'd with Enter.
- **Results:** the project was saved and the `.frm`'s Font block read. The dialog was then reopened and its boxes read.

### Its controls

It's the standard Windows Font dialog (`#32770`), the same for a form and a TextBox. The IDE is disabled behind it.

| Control | Caption | Key |
|---|---|---|
| a box above a list | `&Font:` | F |
| a box above a list | `Font st&yle:` (Regular, Oblique, Bold, Bold Oblique for Arial) | Y |
| a box above a list | `&Size:` | S |
| two check boxes in an `Effects` group | `Stri&keout`, `&Underline` | K, U |
| a `Sample` group | `AaBbYyZz`, drawn in the font | |
| a dropdown | `Sc&ript:`, holding `Western` | R |
| buttons | `OK` (the default), `Cancel` | none |

- **There's a box to type in above each list:** name, style and size.
- **It opens with the focus in the Font name box**, its text selected.
- **Hidden:** Color, Apply and Help exist but aren't shown.
- **The Size list depends on the font.** For MS Sans Serif, a bitmap font, it was 8, 10, 12, 14, 15, 17, 18, 23, 24 and 30.
- **A form's 8.25 shows as `8`** in the Size box.
- **The Properties window's Font row shows only the name**, such as `Arial`, never the size.

### Typed sizes

| Typed | Result | `.frm` Size | Size box when reopened |
|---|---|---|---|
| 13 | accepted | **12.75** | 13 |
| 10 | accepted | 9.75 | 10 |
| 1 | accepted | 1.5 | 2 |
| 1638 | accepted | 1638 | 1638 |
| 1639 | accepted | 1638.75 | 1639 |
| 2048 | accepted | 2048.25 | 2048 |
| 2049 | accepted | 2049 | 2049 |
| 2160, 2161, 2200, 2236 | accepted | 2160, 2160.75, 2199.75, 2235.75 | |
| **2237** to 4096 (I tried 2237, 2239, 2242, 2249, 2250, 2251, 2300, 2457, 2458, 2600, 2800, 3000, 3276, 3277 and 4096) | accepted, no message | **34785.0592, and `Weight = 2000`** | 34785 |
| 5000 | accepted, no message | **526.5** | 527 |
| -1 | accepted, no message | **34785.0592, and `Weight = 2000`** | 34785, with Strikeout shown checked |
| 0 | the dialog closes, and nothing changes | unchanged | |
| an empty box | the dialog closes, and nothing changes | unchanged | |
| 8.5, 13.7 | **refused** | | |
| abc | **refused** | | |

- **The refusal** is a message box titled `Font` with one OK button: **"Size must be a number."**
  - Dismissing it returns the focus to the Size box, with the typed text still there.
  - The Font dialog stays open.
  - **So a decimal point is refused:** the dialog takes whole numbers only.
- **An accepted size is snapped to whole pixels at 96 dpi.** The saved size is a multiple of 0.75 pt, one pixel, nearest to what was typed:
  - 13 is 17 px, so 12.75;
  - 1639 is 2185 px, so 1638.75;
  - 2048 is 2731 px, so 2048.25.

  The exception is 1, which saves 1.5 (2 px), not 0.75. I didn't measure other scalings.
- **From 2237 up, VB6 accepts the value and stores garbage, with no error:** Size 34785.0592 and Weight 2000, which is outside 100 to 900. -1 does the same, and 5000 gives 526.5. A later valid size repairs it, so the state doesn't stick.
  - **The edge** is between 2236 (2981 px), which is good, and 2237 (2983 px), which isn't. Bisected, with a valid size set before each try.
  - I have no rule I'd defend for why the edge sits there.
- **After -1,** the `.frm` says `Strikethrough = 0`, but the reopened dialog shows Strikeout checked. OK there then writes `Strikethrough = -1`.

### Underline and Strikeout

Set in the dialog, both are written in the `BeginProperty Font` block, `Underline = -1 'True` and `Strikethrough = -1 'True`, for a form and for a TextBox. Alt+K and Alt+U toggle them.

### What this overturned

- **#4153 expected "a typed size of 13 is saved as 13", from memory.** VB6 saves 12.75 at 96 dpi: it snaps a typed size
  to whole pixels.
- **A brief for #4153 had stated a size range from memory.** VB6's working range is whole numbers from 1 to 2236. Above
  that, VB6 accepts the value and stores a garbage size and weight with no error.

### What resisted explanation

- **Why the edge is 2236.** 2236 pt (2981 px) is good and 2237 pt (2983 px) isn't, bisected with a valid size before each
  try. Neither number is a power of two in points, pixels or twips.
- **Why the garbage is 34785.0592 with Weight 2000.** That's the same for every value from 2237 to 4096 and for -1,
  while 5000 gives 526.5.
- **Why 1 saves 1.5 (2 px)** when every other accepted size goes to the nearest pixel.

**Not measured:**
- other scalings;
- a TrueType font's own Size list;
- typing a font name that isn't installed.
