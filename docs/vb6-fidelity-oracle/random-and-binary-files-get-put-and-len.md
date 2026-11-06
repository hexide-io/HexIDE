## Random and Binary files, Get, Put and Len (2026-10-24, #1646)

**The question.** `Open ... For Random` and `For Binary` were refused, and with them `Get`, `Put` and `Len =`, so no
program that keeps records or structs in a file got past its `Open`. What bytes does `Put` write for each kind of
operand, what does `Get` read back, how do positions, records, EOF and a second handle behave, and what do the `Access`
and `Lock` clauses do on these modes?

**The probe.** Eight batches, each built native and p-code with vb6.exe 6.00.8176 (the release build) and run on the
MSVBVM60 6.00.9848 (SP6) runtime, en-GB, code page 1252. Native and p-code agreed on every row. Each batch is one program
under `On Error Resume Next`, which makes its files byte for byte with `Put` of a Byte array, and logs each step's error,
`LOF`, `Loc`, `Seek` and `EOF`, and the file's bytes in hex, read back with `Get` of a Byte array. One-line projects tried
the forms that might not compile. The first run of two batches logged a leftover 53 from the `Kill` that cleared each
file; they were run again with `Err.Clear` after it, and only the second run is recorded here.

**What `Put` writes, alone in a Binary file:**

| Operand | Bytes |
|---|---|
| Integer 1, -2 | `01 00`, `FE FF` |
| Long &H01020304 | `04 03 02 01` |
| Single 1.5; Double 1.5 | `00 00 C0 3F`; `00 00 00 00 00 00 F8 3F` |
| Currency 1.5 | `98 3A 00 00 00 00 00 00`: 15000, scaled by 10,000 |
| Date `#1/2/2003#` | `00 00 00 00 E0 5E E2 40`: its serial, a Double |
| Boolean True; False | `FF FF`; `00 00` |
| Byte 200 | `C8` |
| String "abc"; "" | `61 62 63`; nothing |
| String "a" & Chr$(233); "a" & ChrW$(&H416) | `61 E9`; `61 3F`: the ANSI code page, `?` where it has no byte |
| Variant Integer 1; Long 1 | `02 00 01 00`; `03 00 01 00 00 00` |
| Variant Double 1.5; Single 1.5; Currency 1.5; Date | `05 00` then 8 bytes; `04 00` then 4; `06 00` then 8; `07 00` then 8 |
| Variant String "abc" | `08 00 03 00 61 62 63` |
| Variant Empty; Null | `00 00`; `01 00` |
| Variant Boolean True; Byte 200 | `0B 00 FF FF`; `11 00 C8` |
| Variant CVErr(5) | `0A 00 05 00 0A 80`: the SCODE 800A0005 |
| Variant CDec(1.5) | `0E 00` then 16 bytes, `0E 00 01 00 00 00 00 00 0F 00 00 00 00 00 00 00`: a DECIMAL whose reserved first two bytes hold the type again, then the scale, the sign, the high 32 bits and the low 64 |
| Variant Nothing, or holding a Collection | `09 00` written, then 13, the file not moving on: the next `Put` writes over the `09 00` |
| Variant `Array(1, 2)` | `0C 20 01 00 02 00 00 00 00 00 00 00 02 00 01 00 02 00 02 00` |
| a fixed Byte array; a dynamic one | `01 02 03`; `04 05 06`: no descriptor in a Binary file |
| a 2-D Integer array (0 To 1, 0 To 2) | `01 00 02 00 03 00 00 00 00 00 09 00`: the first index fastest |
| a fixed String array ("ab", "c") | `02 00 61 62 01 00 63`: each with its length |
| Type (Integer 1, String "xy", Long 2) | `01 00 02 00 78 79 02 00 00 00`: the String with its length |
| Type (Byte 1, Long 2) | `01 02 00 00 00`: packed, no alignment |
| a Type with a nested Type, and one holding a Variant "ab" | the nested one's bytes in place; `08 00 02 00 61 62` |
| `String * 5` "ab"; `String * 2` "abcd" | `61 62 20 20 20`; `61 62`: padded or cut, no length |
| Type (Integer 1, `String * 3` "a") | `01 00 61 20 20` |

**A String's length**, where it has one, is 2 bytes below 65,535 and `FF FF` then 4 bytes from 65,535 on: a Variant
String of 40,000 is `08 00 40 9C`, of 65,535 `08 00 FF FF FF FF 00 00`, and of 70,000 `08 00 FF FF 70 11 01 00`, and a
Type's String field of 70,000 is the same. A String has a length in a Random record, in a Type, in an array and in a
Variant, and none alone in a Binary file. A `String * n` never has one, in either mode.

**An array's descriptor** is written for an array in a Variant, in either mode, and for a dynamic array in a Random file;
never for a fixed array, and never for a dynamic array alone in a Binary file. It is the number of dimensions, 2 bytes,
then for each dimension, **from the last to the first**, its element count and its lower bound, 4 bytes each:
`Integer(1 To 2, 3 To 5)` is `02 00`, `03 00 00 00`, `03 00 00 00`, `02 00 00 00`, `01 00 00 00`, then the six elements.
The obvious guess was first to last; the 3 and 2 show otherwise. A dynamic array with no bounds is `00 00` in a Random
file and nothing in a Binary one, and a Variant holding an `Integer()` with no bounds is `02 20 00 00`. An array in a
Variant is type 8192 plus its element type: `02 20` for Integer, `08 20` for String, `0C 20` for a Variant's.

**An expression is written by its static type**, which is how VB6's compiler types it, so a Variant's goes with its type
first and a typed value bare:

| Written as a Variant | Written bare |
|---|---|
| `Mid`, `Left`, `Trim`, `Chr`, `Str`, `Format`, `Space`, `String`, `UCase`, `Hex`, `CVar`, `IIf`, `Choose`, `Weekday`, `Year`, `DateSerial`, `Now`, `Date`, `Time` | `Mid$`, `Chr$`, `LCase$`, `CStr`, `Val` and `Int(2.5)` (Doubles), `CDate`, `Len` (a Long), `Asc`, `Abs(-2)`, `Sgn`, `Timer` (a Single), `Timer > 0` (a Boolean) |
| `v + 1`, `-v` and `v & ""` for a Variant `v`; a Function returning a Variant; `Array(1)(0)`; a Variant array's element | a Function `As Integer`; a String array's element; `5`, `5&`, `40000` (a Long), `1.5` (a Double), `1.5!`, `1.5@`, `"ab"`, `True`, `#1/2/2003#`, `CByte(7)`, `ia + 1` and `(ia)` for an Integer `ia` |

`Null` and `Empty` are only ever a Variant's: `01 00` and `00 00`. In a Random file `"ab"` is `02 00 61 62`.

**Random files.** A record is `Len` bytes, 128 with no `Len`. Records count from 1, at byte `(r - 1) * Len`:

| Step, `Len = 8` | Bytes, LOF, Loc, Seek |
|---|---|
| `Put #1, 1, Integer 1` | `01 00`; 2, 1, 2: the record is not padded |
| `Put #1, 3, Long 2` | the gap zeros, `02 00 00 00` at 16; 20, 3, 4 |
| `Put #1, , Integer 5` | at 24, the next record; 26, 4, 5 |
| `Put #1, 6, "abc"` | `03 00 61 62 63` at 40 |
| `Put #1, 7`, an 8-character String; a 6-character one | 59, nothing written, Seek 7; written, 2 + 6 = 8 bytes |
| `Put #1, 9`, a Variant "abcd"; "abcde" | `08 00 04 00 61 62 63 64`; 59 |
| `Get #1, 2, Integer`, a gap record; `Get #1, 50`, past the end | 0, EOF False; 0, Loc 50, Seek 51, EOF True |
| `Get #1, 0`; `Put #1, 0` | 63 each |
| `Input`, `Line Input #`, `Print #`, `Write #`, `Input #` | 54 each |
| `Seek #1, 2` | Loc 1, Seek 2, EOF False |

`Len = -1` is the 128 of no `Len` at all (LOF 130 after a `Put` at record 2), while 0, -2 and -32768 are 5 and 32768,
65535 and 65536 are 6, as for an Integer. 1.5 and 2.5 are both 2 and `"8"` is 8. A Binary file checks `Len` the same
way, 0 and -2 being 5 and 40000 6, and with 3 does nothing with it. `Loc` is the last record and `Seek` the next. A Type
whose bytes are more than `Len` is 59: (Byte, Long), 5 bytes, is 59 in `Len = 4` and fits `Len = 5`.

`Get` reads the whole record: zeros past the end of the file, and EOF True when the record runs past it, though the
value itself was all there (a String of 3 in a 5-byte file with `Len = 8` reads `abc` and sets EOF). A value longer
than the record is 59, the file staying where it was: a Long in `Len = 2` keeps its value, and a String whose length says
10 in a record of 8 is left `""`; a Variant String likewise is left Empty. In a Type of (Byte, Long) the Byte is read and
kept and the Long, which runs past the record, is not (`3 9`). A Type's String field that runs past is left `""` with the
fields before it read, and so is a Variant array's element left Empty with the elements before it read.

**Binary files.** Positions are bytes from 1, and `Loc` is the last byte read or written, `Seek - 1`. A Binary file
opened new is LOF 0, Loc 0, Seek 1, EOF False. `Put #1, 5, Integer 513` on it gives `00 00 00 00 01 02`. `Get` past the
end reads zeros and moves on the whole value: `Get #1, 9, Long` with 2 bytes left is 2569 and Seek 13, `Get #1, 20,
Integer` is 0 and Seek 22. A String reads as many bytes as it holds: `Space$(3)` reads 3 and `""` none, and `Space$(5)`
with 3 bytes left is `abc` and two NULs. Record numbers are Longs, banker's-rounded: `Put #1, 2.5` writes at 2, `3.5` at
4, and `"6"` at 6.

**EOF on both modes** is True once a read has run past the end, and only then: a `Get` that ends exactly at the end
leaves it False, as does a new file. A `Seek` makes it False, past the end too (`Seek #1, 100` on 10 bytes: False). A
`Put` leaves it as it was, True after a read past the end.

**After a read past the end, VB6 keeps two positions.** The file's own pointer stops at the end, and that is where the
next `Get` or `Put` with no position reads or writes; `Seek` and `Loc` count on as if the whole value had been read:

| Step | Bytes, LOF, Loc, Seek |
|---|---|
| `Get #1, 2, Long` on `01 02` | 1 byte read; LOF 2, Loc 5, Seek 6, EOF True |
| a second handle puts `AA BB CC DD` at 3; then `Get #1, , Integer` | `BBAA`: read at byte 3, where the pointer stopped; Seek 8 |
| `Get #1, 10, Long` on `01 02`; then `Put #1, , Byte 9` | the byte at 10, where the pointer is: `01 02 00 ... 09`; Seek 15 |
| `Get #1, 2, Long` on 3 bytes; `Seek #1, 3`; `Put #1, , Byte 9` | `01 02 09`: the Seek brings the two together |
| Random `Len = 2` on 2 bytes, `Get #1, 3`; then `Put #1, , Integer 7` | written at byte 5, record 3, while Seek said 4: `01 00 00 00 07 00`, then Seek 5 |
| Random `Len = 2` on 3 bytes, `Get #1, 2`; then `Put #1, , 7`, `Put #1, , 8` | `01 00 02 07 00 08 00`: the first at byte 4, where the read stopped |

So the usual loop that reads to EOF and then appends with `Put #1, , x` appends at the end, with no gap. A Random
`Put` moves the pointer a whole record on, so `Put #1, 3, Long` then `Put #1, , Integer` lands at record 4.

**Get.** `Get` stores into a variable, an array element or a Type's field, `Get #1, 1, bf(2)` and `Get #1, 1, r.i`
alike. A Type is read field by field, a nested Type in place. A fixed array, and a dynamic one in a Binary file, is read
as it stands, and one with no bounds reads nothing. A dynamic array in a Random file takes the bounds its descriptor
gives, whatever it had (`0 To 5` becomes `0 To 1`), and with no bounds it is given them; a record holding a fixed array's
`01 00 02 00` read so is `0 To 1`, the descriptor's missing bytes zeros. A dynamic array whose element is passed ByRef
is 10, after the record is read: the file has moved on. A Variant reads its type, then its value:

| Type read | Result |
|---|---|
| 0, 1, 2, 3, 8, 10, 14, 17 | Empty, Null, Integer, Long, String, Error, Decimal, Byte |
| `&H2002` with bounds 5 To 6 | an `Integer()` 5 To 6 |
| 9 | 13, the Variant keeping what it had |
| 13 | 13, the Variant left Empty |
| 99, `&H4002`, `&H0201` | 458, "Variable uses an Automation type not supported in Visual Basic", left Empty |
| 8 with a length of 3 and 2 bytes | `ab` and a NUL |

The file stays where it was after each of those errors. A Boolean read from `01 00` is True, but `CInt` of it is 1 and
`= True` is False: VB6 keeps the bits, and so it does for a Variant of type 11. `FF 00` is True with `CInt` 255. A Date
read from the bytes of a Double past the year 9999 is taken, `CDbl` of it 9000372404224, and a Decimal with scale 29
reads as a Decimal.

**Text reads on a Binary file.** `Input #`, `Line Input #` and the `Input` function work there, and `Print #` and
`Write #` are 54. A Ctrl+Z is an ordinary byte: `Line Input` reads `ab`, Ctrl+Z, `cd`, and so does `Input(5, #1)`. A line
ends at CR or CR LF, not a lone LF. A line that runs to the end, or whose CR is the last byte, leaves EOF True, and one
ending CR LF at the end leaves it False. At the end `Line Input #` and `Input #` are 62 with the variable kept. The
`Input` function, unlike on an Input file, is no error short of its count: one byte of five reads that byte, and at the
end it reads `""`, EOF True. `Input #` after a last field with no line end, `1,ab` or `1,"ab"`, leaves EOF True, and
after `1,ab,` False.

**Opening.** A missing file is made, even with `Access Read`, in both modes. A second handle on a file open in a
sequential mode is 55, and so is a sequential handle on one open for Binary or Random, every pair of the five modes
measured: only Input with Input, and Binary or Random with Binary or Random, go through. Between those, the `Lock` and
`Access` clauses decide, as Windows decides between two handles: each must allow what the other does, and a `Lock`
clause allows what it doesn't name. With no `Access` clause VB6 takes reading and writing, or failing that writing
alone, or failing that reading alone:

| First | Second: plain | Access Read | Access Write | Lock Read |
|---|---|---|---|---|
| Shared, or no Lock | 0 | 0 | 0 | 70 |
| Lock Read | 0, write only: a `Get` on it is 75 | 70 | 0 | 70 |
| Lock Write | 0, read only: a `Put` on it is 75 | 0 | 70 | 70 |
| Lock Read Write | 70 | 70 | 70 | 70 |
| Access Read | | | | 70; Lock Write 0 |
| Access Write | | | | 0; Lock Write 70 |

`Put` on an `Access Read` handle and `Get` on an `Access Write` one are 75. Two plain Binary handles on one file see each
other's writes at once. `Get` and `Put` on a number not open are 52, and on an Input, Output or Append file 54.

**Refused while compiling:** `Get #1, , 5` and `Get #1, , K` for a Const are "Variable required - can't assign to this
expression", and `Get` of an object variable is "Can't Get or Put an object reference variable or a variable of
user-defined type containing an object reference". `Get 1, , v` and `Put 1, , v`, without the `#`, compile and run.

**Measured, but no rule I would defend:** why a Variant of type 9 keeps its value where one of type 13 is emptied, both
raising 13.

### In HexIDE (#1646)

- **`VbRecordFormat`** writes and reads the bytes above, and **`VbFiles`** opens Binary and Random files, with the
  sharing, the access fallback, the records, EOF and the two positions as measured. Each handle reads and writes the
  file itself, unbuffered.
- **What an operand is** comes from its declaration for a variable, an element or a field. For any other expression it
  comes from the type HexIDE knows the value has: a literal, a typed variable's value, a call declared to return a
  type, and the intrinsics measured for it, the Put probe adding `Space`, `DateSerial`, `Now`, `Date` and `Time` to the
  Variant intrinsics and `Val`, `CDate` and `Timer` to the typed ones. A dynamic array with no bounds now knows its
  element type, as the Variant measurement needs.
- **`RecordFileTests`** runs each probe program, its log written with `Debug.Print`, and must print what vb6.exe logged,
  line for line; a step HexIDE refuses on purpose is cut out, with its lines, and tested as a refusal. One test reads the
  bytes back in C#, so a Put and a Get that agreed with each other but not with vb6.exe would fail.
- **Refused, as HexIDE cannot hold the value:** a Boolean whose bits are neither 0 nor -1, a Date past the year 9999, a
  Decimal of scale past 28, and an Error Variant, which waits on CVErr.
- **Refused, not yet built:** a `Put` of an expression whose static type HexIDE does not know, such as `Timer > 0`,
  since a Variant and its value written bare are different files; fixed-length strings, here as everywhere; and arrays
  of a Type, whose elements are not made (#178).
- **Divergences kept:** the locks hold only within the program, not against another process; `Len` on a sequential
  file is not checked; a `Get` of a String in a Binary file reads as many bytes as the String has characters, which is
  the same in code page 1252 and not measured in a double-byte one.

