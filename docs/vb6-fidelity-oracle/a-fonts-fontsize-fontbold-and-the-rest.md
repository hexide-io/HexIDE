## A font's FontSize, FontBold and the rest (2026-10-22, #1343, #915)

**The probes.** Found by the Notepad demo, whose Format > Font reads a TextBox's `FontName`, `FontSize`, `FontBold` and
`FontItalic`, shows the CommonDialog's Font dialog, and sets them back. Five probes, each built native and p-code. Every
row agreed between the two, and every one ran in the probe runner's session, since none needs a desktop.
1. **The members.**
   - A TextBox in Arial 9.75, a Label with no Font block, a Label in Tahoma 12 bold italic, and the form.
   - `Form_Load` reads the six members' types.
   - It sets `FontSize` on each control and on the form to a list of values, and reads each back.
   - It sets the four Booleans to a list of values.
   - It sets each member in turn, reading all of them after each.
2. **Load and limits.**
   - Labels whose .frm says Arial 10, MS Sans Serif 10, Arial 0.1, and Arial 12 at weight 600.
   - A change of family back and forth.
   - Weights from 0 to 1001 set through `Font.Weight`.
   - Sizes either side of 2160.
3. **Every height.** For each pixel height *n* from 1 to 400, a Label is set to `FontSize = n * 0.75` and read back. Each
   *n* starts fresh: Arial 9, then the family, then the size. It ran for every family `FontName` accepts by name, the
   symbol fonts included.
4. **The rest.**
   - FontUnderline and FontStrikethru take the same values as the other Booleans.
   - Sizes from 2160.2 to 2160.6.
   - MS Sans Serif at 300 points.

**Types.** `FontName` String, `FontSize` Single, and `FontBold`, `FontItalic`, `FontUnderline` and `FontStrikethru`
Boolean.

**Sizes are whole pixels.** At 96 dpi, a size is given `size * 4 / 3` pixels, rounded with halves up, and reads back as
those pixels times 3/4. The smallest is 2 pixels, or 1 for the vector fonts Modern, Roman and Script.

| `FontSize =` on Arial | 8 | 9 | 10 | 11 | 13 | 14 | 16 | 7.5 | 9.7 | 9.375 | 11.625 | 0.5, 1, 0.1 | 2000 | 2160 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| reads | 8.25 | 9 | 9.75 | 11.25 | 12.75 | 14.25 | 15.75 | 7.5 | 9.75 | 9.75 | 12 | 1.5 | 2000.25 | 2160 |

9.375 is 12.5 pixels and 11.625 is 15.5, so the rounding is halves up, not to even. A .frm's size is given the same way at
load: `Size = 10` reads 9.75, and `Size = 0.1` reads 1.5. Every TrueType family probed agreed at every height: Arial,
Lucida Console, Segoe UI, Tahoma, Courier New, Times New Roman, Microsoft Sans Serif, MS Shell Dlg 2, MS Gothic,
Helvetica, Times, Wingdings, Webdings, Symbol and Marlett. So did Modern, Roman and Script, from 1 pixel.

**The bitmap fonts snap to the heights GDI has for them.** Each line is the requested height in pixels, `n` or a range
`a-b`, then `:` and the height it was given. `=` means the height asked, and `A` means the height asked but in Arial,
whose `FontName` then reads `Arial`:

```text
MS Sans Serif: 1-10:11 11:= 12:11 13:= 14-15:13 16:= 17:16 18:= 19:18 20:= 21:20 22-24:= 25:24 26:= 27-29:26 30:= 31:30 32:= 33-35:32 36:= 37-38:36 39-40:= 41-43:40 44:= 45:44 46:= 47:46 48:= 49-51:48 52:= 53:52 54-55:= 56-59:55 60:= 61-63:60 64-65:= 66-68:65 69:= 70-71:69 72:= 73-77:72 78-79:80 80:= 81-87:80 88-89:90 90:= 91:90 92:= 93-95:92 96:= 97-99:96 100:= 101-107:100 108:= 109-110:108 111-114:115 115:= 116-119:115 120:= 121-126:120 127:128 128:= 129-137:128 138:= 139-142:138 143:150 144:= 145:144 146-149:150 150:= 151-156:150 157-159:160 160:= 161-175:160 176-179:180 180:= 181-187:180 188-191:192 192:= 193-194:192 195-199:200 200:= 201-209:200 210:= 211-218:210 219-223:224 224:= 225-232:224 233-239:240 240:= 241-252:240 253-255:256 256:= 257-269:256 270-279:280 280:= 281-309:280 310-319:320 320:= 321-385:320 386-400:A
MS Serif: 1-7:8 8-9:= 10:9 11:= 12:11 13:= 14-15:13 16:= 17:16 18:= 19:18 20:= 21:20 22-24:= 25:24 26-27:= 28-29:27 30:= 31:30 32:= 33-35:32 36:= 37-38:36 39-40:= 41-43:40 44-46:= 47:46 48:= 49-51:48 52:= 53:52 54-55:= 56-59:55 60:= 61-63:60 64-65:= 66-68:65 69:= 70-71:69 72:= 73-77:72 78-79:80 80:= 81-87:80 88-89:90 90:= 91:90 92:= 93-95:92 96:= 97-99:96 100:= 101-107:100 108:= 109-110:108 111-114:115 115:= 116-119:115 120:= 121-126:120 127:128 128:= 129-137:128 138:= 139-142:138 143:150 144:= 145:144 146-149:150 150:= 151-156:150 157-159:160 160:= 161-175:160 176-179:180 180:= 181-187:180 188-191:192 192:= 193-194:192 195-199:200 200:= 201-209:200 210:= 211-218:210 219-223:224 224:= 225-232:224 233-239:240 240:= 241-252:240 253-255:256 256:= 257-269:256 270-279:280 280:= 281-309:280 310-319:320 320:= 321-385:320 386-400:A
System: 1-12:13 13:= 14-22:13 23-25:26 26:= 27-35:26 36-38:39 39:= 40-48:39 49-51:52 52:= 53-61:52 62-64:65 65:= 66-74:65 75-77:78 78:= 79-87:78 88-90:91 91:= 92-100:91 101-103:104 104:= 105-400:104
Terminal: 1-5:6 6:= 7:6 8:= 9-11:8 12:= 13-15:12 16:= 17:16 18:= 19-23:18 24:= 25-30:24 31:32 32:= 33-35:32 36:= 37-39:36 40:= 41-45:40 46-47:48 48:= 49-52:48 53:54 54:= 55-59:54 60:= 61-63:60 64:= 65-69:64 70-71:72 72:= 73-78:72 79:80 80:= 81-86:80 87-89:90 90:= 91-103:90 104-107:108 108:= 109-111:108 112:= 113-121:112 122-125:126 126:= 127-139:126 140-143:144 144:= 145-209:144 210-400:A
Fixedsys: 1-11:12 12:= 13-21:12 22-23:24 24:= 25-32:24 33-35:36 36:= 37-44:36 45-47:48 48:= 49-56:48 57-59:60 60:= 61-68:60 69-71:72 72:= 73-80:72 81-83:84 84:= 85-92:84 93-95:96 96:= 97-161:96 162-400:A
Courier: 1-12:13 13:= 14-15:13 16:= 17-19:16 20:= 21-24:20 25-26:= 27-31:26 32:= 33-37:32 38:39 39-40:= 41-46:40 47:48 48:= 49:48 50:= 51:50 52:= 53-57:52 58-59:60 60:= 61-63:60 64-65:= 66-71:65 72-74:75 75:= 76-79:75 80:= 81-90:80 91:= 92-93:91 94-95:100 96:= 97-99:100 100:= 101-111:100 112:= 113-116:112 117-118:120 119:125 120:= 121:120 122-124:125 125:= 126-139:125 140:= 141-145:140 146-149:150 150:= 151-159:150 160:= 161-168:160 169-174:175 175:= 176-193:175 194-199:200 200:= 201-265:200 266-400:A
Small Fonts: 1-2:3 3-5:= 6:5 7-11:= 12-13:11 14:= 15:14 16:= 17:16 18:= 19:18 20-22:= 23:22 24:= 25-26:24 27-28:= 29:28 30:= 31:30 32-33:= 34-35:33 36:= 37-39:36 40:= 41-43:40 44-45:= 46-49:45 50:= 51-54:50 55:= 56-63:55 64-65:60 66:= 67-69:66 70:= 71-75:70 76:77 77:= 78-85:77 86-87:88 88:= 89-153:88 154-400:A
MS Shell Dlg: 1:2 2-8:= 9-10:11 11:= 12:11 13:= 14-15:13 16-400:=
```

So MS Sans Serif 9 points (12 pixels) reads 8.25, 11 points reads 9.75, and 13 reads 12. System is 13 pixels for anything
up to 22. Above a height of its own, each bitmap family but System becomes Arial: MS Sans Serif and MS Serif at 386
pixels, Terminal at 210, Courier at 266, Fixedsys at 162 and Small Fonts at 154. MS Shell Dlg snaps heights from 9 to 15
pixels to 11 or 13, and is exact otherwise. These tables are only right from a fresh start. A run that stepped one Label through every height
without resetting got the same snapping, but its family, once it had become Arial at a large size, stayed Arial, which
the first run of this probe took for a different table.

**A change of family keeps the size as it reads.**

| Step | Reads |
|---|---|
| Arial, `FontSize = 9` | 9 |
| `FontName = "MS Sans Serif"` | 8.25 |
| `FontName = "Arial"` | 8.25: the snapped size, not the 9 asked for |
| `FontName = "System"`, then `"Arial"` | 9.75, then 9.75 |
| MS Sans Serif, `FontSize = 300` | `Arial`, 300 |
| then `FontSize = 9` | `Arial`, 9: it stays Arial |
| then `FontName = "MS Sans Serif"` | MS Sans Serif, 8.25 |

**Refusals.**

| Member | Value | Result |
|---|---|---|
| `FontSize` | 0, -1, True (-1) | 380 |
| `FontSize` | 2160.2 to 2160.6, 2161, 1E+30 | 6 |
| `FontSize` | `"12"` | taken |
| `FontSize` | `"abc"`, **Null** | 13: Null too, where every other font member's is 94 |
| the four Booleans | True, 1, 2, -1, 0.4, `"True"` | True |
| the four Booleans | False, 0 | False |
| the four Booleans | `"x"` | 13 |
| the four Booleans | Null | 94 |

Each refusal leaves the font as it was.

**Weight.** `FontBold = True` makes the weight 700, and False 400. `Font.Weight` reads 400 for any weight up to 550 and 700
above it, 1000 included, and 1001 or -1 is 380. A .frm's `Weight = 600` loads as 700 and reads Bold.

**Each member leaves the rest alone.** Bold, italic, underline and strikethrough survive a change of family and of size,
and a form's `FontSize = 13`, which reads 12, changes none of its controls'.

**In HexIDE (#1343, #915):**
- **A TextBox models its Font**, as every sibling control does. It draws in it, at design time and at run time, and its
  members answer. A TextBox's Font block, until now kept as the file's own lines, is read into the model and written back
  byte for byte.
- **The five members read and write** on every control with a font and on the form, as the tables say. The sizes come
  from the measured runs, generated from the probe's log.
- **Above 400 pixels** the last run is taken to go on, which for System is 104 pixels. That is assumed, not measured.
- **Font object (#934):** member reads now use an object, with Currency Size and Weight reading 400 or 700 as above.
  The measured direct Size writes 9 and 12 and shared control references are implemented; other object-member writes
  and standalone StdFont remain refused. HexIDE keeps the file descriptor's raw weight for serialization.

