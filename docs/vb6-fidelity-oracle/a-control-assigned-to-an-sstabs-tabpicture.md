## A control assigned to an SSTab's TabPicture (2026-11-16, #3426, SP6 native and p-code)

**The question.** #3426's first probe (oracle PR #4239) found that `Set TabPicture(0) = Picture1`, a PictureBox, raises
no error and reads back empty. Picture1's own `Picture` was empty there, so it couldn't say whether the SSTab took the
PictureBox's default property, `Picture`, or ignored the control. What does it take?

**The probe.**
- **The form:** an SSTab with three tabs (TABCTL32.OCX), a PictureBox `Picture1` with `AutoRedraw` and a red square
  drawn on it, an Image control `Image1`, a TextBox `Text1` (Text "x") and a Label `Label1` (Caption "y").
- **The build:** `/make` as native code and as p-code, VB6 SP6. Both builds gave the same rows.
- **What each row reads:** `TabPicture(i)` back, with `TypeName`, whether `Handle` is 0, and `Type`.
- **The filling:** `Set Picture1.Picture = Picture1.Image` and `Set Image1.Picture = Picture1.Image` fill the two
  controls' `Picture` (both then read Type 1, a bitmap). The rows marked "filled" come after this.

| Step | TabPicture(i) reads | Error |
|---|---|---|
| `Set TabPicture(0) = Picture1`, its `Picture` empty | an empty Picture, Handle 0, Type 0 | none |
| `Set TabPicture(1) = Image1`, its `Picture` empty | an empty Picture | none |
| `Set TabPicture(0) = Picture1`, filled | **a Picture, Handle non-zero, Type 1** | none |
| then `TabPicture(0) Is Picture1.Picture` | **False** | |
| `Set TabPicture(1) = Image1`, filled | a Picture, Type 1 | none |
| then `TabPicture(1) Is Image1.Picture` | **False** | |
| `TabPicture(2) = Picture1`, Let, filled | a Picture, Type 1 | none |
| then `TabPicture(2) Is Picture1.Picture` | False | |
| `Set TabPicture(0) = Text1` | unchanged: still the Type 1 picture from before | **13**, "Type mismatch" |
| `Set TabPicture(1) = Label1` | unchanged | **13** |
| `Picture1.Picture Is Picture1.Picture` | True | |
| `Image1.Picture Is Image1.Picture` | True | |
| `TabPicture(0) Is TabPicture(0)`, after `Set TabPicture(0) = Picture1` | True | |

**Readings:**
- **A PictureBox or an Image control is taken by its `Picture`,** its default property: empty when that's empty, a
  bitmap when it holds one, and the same through `Set` and Let.
- **The tab doesn't hold the control's own Picture object.** `TabPicture(0) Is Picture1.Picture` is False, though
  each of `Picture1.Picture` and `TabPicture(0)` is `Is` itself. A Picture object assigned directly, such as
  `Picture1.Image`, *is* kept as itself: #3426's first probe read `Set TabPicture(1) = a` then
  `TabPicture(1) Is a` as True. So the SSTab makes a picture of its own when it's handed a control.
- **A control whose default property isn't a Picture raises 13,** as a String or a number does. TextBox (Text) and
  Label (Caption) were tried. The tab keeps the picture it had.

**Not measured:**
- whether the tab's own picture follows later changes to the control's `Picture`;
- controls other than those four;
- a picture that isn't a bitmap.
