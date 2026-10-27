## In-box controls: the UpDown (2026-10-27, #1093)

**The probes.**
- **The software:** VB6 6.00.8176 SP6 with MSCOMCT2.OCX 6.01.9782, Microsoft Windows Common Controls-2 6.0 (SP6), on
  Windows 10 at 96 dpi.
  - The form line is `Object = "{86CF1D34-0C5F-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCT2.OCX"`.
  - The block is `Begin MSComCtl2.UpDown`, `_Version` 393216.
- **The form:** Text1 (TabIndex 0), UpDown1 (TabIndex 1, at 3000, 120, 255 × 495, bound to nothing), Text2 and Label1.
- **The programs:** each ran in a Sub Main and logged each value with its `TypeName`.
  - Every error was read straight after its statement.
  - Each scenario ran on a fresh instance of the form.
- **The builds:** `/make` as native code and as p-code, both run on the guest's console. They logged the same lines.

### The object model

- **Its members,** from the type library:
  - **The range:** Value, Min, Max, Increment, Wrap.
  - **The buddy:** BuddyControl and BuddyProperty, both Variants; AutoBuddy, SyncBuddy, Alignment.
  - **The rest:** Orientation, Enabled, hWnd, OLEDropMode, OLEDrag.
  - **Hidden:** Acceleration, an Integer, and AboutBox.
- **Its events:** Change, UpClick, DownClick, MouseDown, MouseMove, MouseUp, and the OLE drag events.
  - There is no Click and no key event.
- **The constants:**
  - OrientationConstants: cc2OrientationVertical 0, cc2OrientationHorizontal 1. **This is the reverse of MSCOMCTL.OCX's**,
    whose ccOrientationHorizontal is 0.
  - AlignmentConstants: cc2AlignmentLeft 0, cc2AlignmentRight 1.
- **`TypeName`** is `UpDown`.
- **The default member is Value.** `v = UpDown1` read 0, a Long, and `UpDown1 = 4` set Value.

### Defaults

| Read | Logged |
|---|---|
| Min, Max, Value | 0, 10, 0, Longs |
| Increment | 1 |
| Wrap, AutoBuddy, SyncBuddy | False |
| Orientation | 0, vertical |
| Alignment | **1, right** |
| BuddyControl, BuddyProperty | empty Strings |
| Enabled, TabStop | True |
| OLEDropMode | 0 |
| Acceleration | 0, an Integer |

### Value, Min and Max

| Statement | Logged |
|---|---|
| `Value = 5` | Change |
| `Value = 5` again | **Change again** |
| `Value = 11`, past Max; `Value = -1`, below Min | **380**, and Value unchanged |
| `Value = 2.5`, `Value = 3.5` | 2, 4: **half rounds to even** |
| `Value = "x"` | 13 |
| `Max = 3` with Value 5 | Max 3, and **Value pulled down to 3, with Change** |
| `Min = 4` with Max 3; `Min = 3` with Max 3; `Min = 9` with Max 3 | **taken**: Min may equal Max or exceed it |
| `Value = 5` with Min 9 and Max 3 | taken. **20 and 0 are 380** |
| `Min = -40000` with Max 3 and Value 5 | Value pulled to 3, with Change |
| `Max = 2147483647` | taken |
| `Min = 10`, then `Max = 0`, from 0 to 10 with Value 0 | Value pulled up to 10, with Change, then kept. `Value = 4` is taken |

- **Value must lie between Min and Max, whichever is larger.**
- **Assigning Value always raises Change,** even with the value it already has.
- **A Min or Max that pulls Value in raises Change too.**

### The other properties

| Assignment | Logged |
|---|---|
| Increment 0, 3, 20, 100 | taken, more than the range included |
| Increment -1 | **380** |
| Wrap or Enabled -1, 1, 2 | True: any non-zero |
| **Orientation, any value** | **382**, at run time |
| Alignment 0 or 1 | taken at run time |
| Alignment -1, 2 | 380 |
| OLEDropMode 0 or 1 | taken. -1 and 2 are 380 |
| Acceleration 2 | taken |
| `hWnd = 1` | 450 |

**The two 382s read differently.** Through `CallByName`, Orientation's error said "Set not supported at runtime". Assigned
directly, through the same late-bound `Object` variable, it said "Property cannot be set at run time".

### The buddy

**BuddyControl takes a control or a control's name, and reads back as the name.**
- `Set BuddyControl = Text1` and `BuddyControl = "Text2"` both bound. Either reads back as the String `Text1` or `Text2`.
- A Label is taken too.
- `"NoSuch"`, `Set BuddyControl = Me` (the form) and `BuddyControl = 5` are 380.
- `Set BuddyControl = Nothing` and `BuddyControl = ""` unbind.

**Binding moves the UpDown to the buddy and narrows the buddy.** With Alignment 1:
- The UpDown went to the buddy's right edge: Left 1066.
- It took the buddy's height: 315, from 495.
- The buddy narrowed by the UpDown's width: 1200 to 945.

**With Alignment 0 the pair swaps sides.** The UpDown took the buddy's old Left, and the buddy moved right by the UpDown's
width, to 375. Alignment 1 put them back.

**What follows binding:**
- **Another buddy gives the first its width back.** Text1 went back to 1200 when Text2 was bound, and Text2 narrowed.
- **Unbinding puts the UpDown back where it was designed:** 3000, 120, 495 high. The buddy got its width back, plus
  anything it had gained since: a Text1 widened to 2000 while bound became 2255.
- **The UpDown doesn't follow a buddy moved later.** After `Text1.Move 500, 600` it stayed at 1066, 120.
  - Setting Alignment again re-attached it, at 2501, 600.
  - `UpDown1.Move` moved it away while still bound.

**SyncBuddy writes Value into the buddy.**
- **With no BuddyProperty, it writes the buddy's default property:** Text for a TextBox, Caption for a Label.
- **The buddy's Change comes first:** Text1_Change, then the UpDown's Change.
- **It writes only when Value is assigned.**
  - Turning SyncBuddy on wrote nothing: Text1 kept `2` with Value 8.
  - Binding a buddy wrote nothing.
- **Code that changes the buddy doesn't change Value.** `Text1.Text = "3"`, and `"abc"`, left Value at 8.
- Turning SyncBuddy off stopped the writes. Switching to another buddy kept SyncBuddy on.

**BuddyProperty names the property to write, and turns SyncBuddy on.**
- `BuddyProperty = "Tag"` was taken, set SyncBuddy True, and the next Value went into Text1's Tag.
- **`"text"`, lower case, was taken**, and the next Value went into Text.
- `""` and `"NoSuch"` are 380.
- **BuddyProperty always read back as an empty String,** whatever it had been set to, Tag, text and Caption included.
- **With no buddy, BuddyProperty and SyncBuddy are 35754, "BuddyControl property must be set first".**

**AutoBuddy binds the control before the UpDown in tab order.**
- `AutoBuddy = True` at run time bound Text1, TabIndex 0, for the UpDown at TabIndex 1, moving and narrowing as above.
- **Binding another buddy turns AutoBuddy off.**
- `AutoBuddy = False` kept the buddy it had.
- `AutoBuddy = True` again went back to Text1, and gave Text2 its width back.
- `BuddyControl = ""` turned it off too.

### Events from code

- **Change** comes from every assignment to Value, and from a Min or Max that pulls Value in.
- **UpClick and DownClick** were never raised from code.

### In HexIDE

HexIDE doesn't know MSCOMCT2.OCX. `ControlLibraries` lists only MSCOMCTL.OCX and COMCTL32.OCX, so nothing maps an UpDown
to this issue (#1093).
