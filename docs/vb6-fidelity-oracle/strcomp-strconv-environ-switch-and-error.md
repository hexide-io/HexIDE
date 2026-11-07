## StrComp, StrConv, Environ, Switch and Error(), and the case of a character (2026-10-23, #1560)

Measured with `/make` on vb6.exe 6.00.8176 (the release build) with the MSVBVM60 6.00.9848 (SP6) runtime, under en-GB, in eight batches. Native and p-code agree on every row.

**The question.** The five functions stopped any program that called them, so how each behaves had to be measured before it was written: its arguments, their errors and their order, and its answers. Three things came out of the measuring:
- a table of error messages;
- a rule for the case of a character, which UCase and LCase had wrong;
- the names of the functions' named arguments.

Environ's rows compare values rather than print them, since an environment variable's value has no place here.

### StrComp

| Row | Call | vb6.exe |
|---|---|---|
| strcomp-lt | `StrComp("a", "b")` | Integer -1 |
| strcomp-gt | `StrComp("b", "a")` | Integer 1 |
| strcomp-eq | `StrComp("a", "a")` | Integer 0 |
| strcomp-case-default | `StrComp("a", "A")` | Integer 1 |
| strcomp-case-0 | `StrComp("a", "A", 0)` | Integer 1 |
| strcomp-case-1 | `StrComp("a", "A", 1)` | Integer 0 |
| strcomp-case-vbText | `StrComp("a", "A", vbTextCompare)` | Integer 0 |
| strcomp-2 | `StrComp("a", "A", 2)` | err 5 Invalid procedure call or argument |
| strcomp-3 | `StrComp("a", "A", 3)` | Integer 0 |
| strcomp-minus1 | `StrComp("a", "A", -1)` | err 5 Invalid procedure call or argument |
| strcomp-minus2 | `StrComp("a", "A", -2)` | err 5 Invalid procedure call or argument |
| strcomp-1.5 | `StrComp("a", "A", 1.5)` | err 5 Invalid procedure call or argument |
| strcomp-0.5 | `StrComp("a", "A", 0.5)` | Integer 1 |
| strcomp-str1 | `StrComp("a", "A", "1")` | Integer 0 |
| strcomp-cmp-null | `StrComp("a", "A", Null)` | err 94 Invalid use of Null |
| strcomp-cmp-empty | `StrComp("a", "A", Empty)` | Integer 1 |
| strcomp-null-l | `StrComp(Null, "a")` | Null |
| strcomp-null-r | `StrComp("a", Null)` | Null |
| strcomp-null-both | `StrComp(Null, Null)` | Null |
| strcomp-empty | `StrComp(Empty, "")` | Integer 0 |
| strcomp-num | `StrComp(10, 9)` | Integer -1 |
| strcomp-num-str | `StrComp(5, "5")` | Integer 0 |
| strcomp-bool | `StrComp(True, "True")` | Integer 0 |
| strcomp-prefix | `StrComp("ab", "abc")` | Integer -1 |
| strcomp-empty-a | `StrComp("", "a")` | Integer -1 |
| strcomp-B-a | `StrComp("B", "a")` | Integer -1 |
| strcomp-B-a-text | `StrComp("B", "a", 1)` | Integer 1 |
| strcomp-us-a-text | `StrComp("_", "a", 1)` | Integer -1 |
| strcomp-us-A-bin | `StrComp("_", "A", 0)` | Integer 1 |
| strcomp-sz-ss-text | `StrComp(Chr(223), "ss", 1)` | Integer 0 |
| strcomp-ae-text | `StrComp(Chr(230), "ae", 1)` | Integer 0 |
| strcomp-e-acute-text | `StrComp(Chr(233), "e", 1)` | Integer 1 |
| strcomp-e-acute-f-text | `StrComp(Chr(233), "f", 1)` | Integer -1 |
| strcomp-e-acute-f-bin | `StrComp(Chr(233), "f", 0)` | Integer 1 |
| strcomp-hyphen-text | `StrComp("co-op", "coop", 1)` | Integer 1 |
| strcomp-apos-text | `StrComp("it's", "its", 1)` | Integer 1 |
| strcomp-nul | `StrComp("a" & Chr(0), "a")` | Integer 1 |
| strcomp-array | `StrComp(Array(1), "a")` | err 13 Type mismatch |
| strcomp-type | `StrComp("a", "b")` | Integer |

- **The compare is read first, as CLng reads it.** So 0.5 is 0 and 1.5 is 2, a Null compare is 94 whatever the strings are, `"1"` is 1, and Empty is 0.
- **0 is binary and 1 text,** the text compare #1567 measured. 2 and a negative are 5.
- **3 compares as text,** consistent with reading 3 and up as a locale identifier, which is Filter's finding too (#881).
- **Then the strings.** An array is 13 even beside a Null (`StrComp(Null, Array(1))` is 13), a Null string makes the answer Null, and anything else is read as `&` reads it: `StrComp(5, "5")` and `StrComp(True, "True")` are 0.
- **The answer is an Integer.**
- **Given no compare, StrComp takes the Option Compare of the module that calls it.** In an `Option Compare Text` module, `StrComp("a", "A")` is 0 and `StrComp("B", "a")` is 1. A compare of 0 or Empty there is still binary: both give 1 for `"a"` against `"A"`. HexIDE still compares binary with no compare (#1572).

### StrConv

| Row | Call | vb6.exe |
|---|---|---|
| strconv-upper | `StrConv("hello World", vbUpperCase)` | String HELLO WORLD |
| strconv-lower | `StrConv("hello World", vbLowerCase)` | String hello world |
| strconv-proper | `StrConv("hello wORLD", vbProperCase)` | String Hello World |
| strconv-proper-digit | `StrConv("1st place", vbProperCase)` | String 1st Place |
| strconv-proper-apos | `StrConv("o'neil's", vbProperCase)` | String O'neil's |
| strconv-proper-hyphen | `StrConv("jean-luc", vbProperCase)` | String Jean-luc |
| strconv-proper-dot | `StrConv("a.b,c;d", vbProperCase)` | String A.b,c;d |
| strconv-proper-tab | `StrConv("a" & vbTab & "b" & vbCrLf & "c" & vbLf & "d" & vbCr & "e", vbProperCase)` | [65 9 66 13 10 67 10 68 13 69] |
| strconv-proper-nul | `StrConv("a" & Chr(0) & "b", vbProperCase)` | [65 0 66] |
| strconv-proper-ff | `StrConv("a" & Chr(12) & "b" & Chr(11) & "c", vbProperCase)` | [65 12 66 11 67] |
| strconv-proper-spaces | `StrConv("  two  spaces", vbProperCase)` | String   Two  Spaces |
| strconv-proper-accent | `StrConv(Chr(233) & "cole " & Chr(201) & "COLE", vbProperCase)` | [201 99 111 108 101 32 201 99 111 108 101] |
| strconv-proper-nbsp | `StrConv("a" & Chr(160) & "b", vbProperCase)` | [65 160 98] |
| strconv-upper-sz | `StrConv(Chr(223), vbUpperCase)` | [223] |
| strconv-upper-y-uml | `StrConv(Chr(255), vbUpperCase)` | [376] |
| strconv-upper-mu | `StrConv(Chr(181), vbUpperCase)` | [181] |
| strconv-upper-dotless | `StrConv(ChrW(&H131), vbUpperCase)` | [73] |
| strconv-lower-dotted | `StrConv(ChrW(&H130), vbLowerCase)` | [105] |
| strconv-upper-tr | `StrConv("i", vbUpperCase, 1055)` | [73] |
| strconv-lower-tr | `StrConv("I", vbLowerCase, 1055)` | [105] |
| strconv-upper-lcid-0 | `StrConv("i", vbUpperCase, 0)` | [73] |
| strconv-upper-lcid-bad | `StrConv("i", vbUpperCase, 99999)` | err 5 Invalid procedure call or argument |
| strconv-from-unicode | `StrConv("ab", vbFromUnicode)` | String ? |
| strconv-from-unicode-e | `StrConv(Chr(233) & ChrW(&H20AC), vbFromUnicode)` | String ? |
| strconv-from-unicode-unmapped | `StrConv(ChrW(&H2100) & ChrW(&HFF41), vbFromUnicode)` | String ? |
| strconv-from-unicode-empty | `StrConv("", vbFromUnicode)` | String  |
| strconv-roundtrip | `StrConv(StrConv("abc", vbFromUnicode), vbUnicode)` | String abc |
| strconv-unicode-of-string | `StrConv("abc", vbUnicode)` | [97 0 98 0 99 0] |
| strconv-unicode-odd | `StrConv("a" & ChrB(98), vbUnicode)` | [97 0 98] |
| strconv-unicode-high | `StrConv(ChrB(128) & ChrB(233), vbUnicode)` | [8364 233] |
| strconv-from-then-len | `LenB(StrConv("abc", vbFromUnicode))` | Long 3 |
| strconv-upper-from | `StrConv("ab", vbUpperCase Or vbFromUnicode)` | err 5 Invalid procedure call or argument |
| strconv-upper-lower | `StrConv("ab", vbUpperCase Or vbLowerCase)` | String Ab |
| strconv-0 | `StrConv("ab", 0)` | String ab |
| strconv-wide | `StrConv("ab", vbWide)` | err 5 Invalid procedure call or argument |
| strconv-narrow | `StrConv("ab", vbNarrow)` | err 5 Invalid procedure call or argument |
| strconv-katakana | `StrConv("ab", vbKatakana)` | err 5 Invalid procedure call or argument |
| strconv-hiragana | `StrConv("ab", vbHiragana)` | err 5 Invalid procedure call or argument |
| strconv-256 | `StrConv("ab", 256)` | err 5 Invalid procedure call or argument |
| strconv-512 | `StrConv("ab", 512)` | err 5 Invalid procedure call or argument |
| strconv-null | `StrConv(Null, vbUpperCase)` | Null |
| strconv-null-conv | `StrConv("ab", Null)` | err 94 Invalid use of Null |
| strconv-num | `StrConv(5.5, vbUpperCase)` | String 5.5 |
| strconv-bool | `StrConv(True, vbUpperCase)` | String TRUE |
| strconv-empty | `StrConv(Empty, vbUpperCase)` | String  |
| strconv-type | `StrConv("ab", vbUpperCase)` | String |
| strconv-type-from | `StrConv("ab", vbFromUnicode)` | String |
| strconv-conv-1.5 | `StrConv("ab", 1.5)` | String ab |
| strconv-conv-str | `StrConv("ab", "1")` | String AB |

- **The conversion is read first, as CLng reads it, and checked before the string.** So `StrConv(Null, Null)` is 94 and `StrConv(Null, 256)` is 5.
- **What each conversion does:**
  - 1 is upper case, 2 lower and 3 proper (`vbUpperCase Or vbLowerCase` is proper), and 0 leaves the string alone.
  - Wide, narrow, katakana and hiragana (4 to 32) are 5 on this Western system.
  - 256 and 512 are 5, and so is 64 or 128 with a case, or the two together.
- **The locale identifier.** `LocaleID` is checked when given: 0, 1, 1055 and 2057 are accepted, and 99999 and -1 are 5. An accepted one changes nothing, even Turkish's 1055.
- **Upper and lower case go through the ANSI code page:**
  - A character the code page lacks becomes its best fit, or "?" where there is none. 一 is "?", α and ā are "A" in upper case, and Greek capital Α is "?" in lower case.
  - A case the code page lacks is not taken. ƒ stays ƒ, Ƒ not being in 1252, although UCase makes it Ƒ.
  - A conversion of 0 does neither: ā and 一 come back as themselves.
- **Proper case starts a word after Chr(0), a tab, LF, VT, FF, CR or a space, and after nothing else.** A sweep of every `Chr(0)` to `Chr(255)` between two letters found exactly those seven. So `"o'neil's"` is `"O'neil's"`, `"jean-luc"` is `"Jean-luc"`, `"a.b,c;d"` is `"A.b,c;d"`, and the no-break space joins words.

### Environ

| Row | Call | vb6.exe |
|---|---|---|
| environ-path-len | `Len(Environ("PATH")) > 0` | Boolean True |
| environ-path-case | `Environ("path") = Environ("PATH")` | Boolean True |
| environ-missing | `Environ("HEXIDE_NO_SUCH_VARIABLE")` | String  |
| environ-empty | `Environ("")` | err 5 Invalid procedure call or argument |
| environ-type | `Environ("PATH")` | String |
| environ-dollar-type | `Environ$("PATH")` | String |
| environ-1-has-eq | `InStr(Environ(1), "=") > 1` | Boolean True |
| environ-1-type | `Environ(1)` | String |
| environ-0 | `Environ(0)` | err 5 Invalid procedure call or argument |
| environ-minus1 | `Environ(-1)` | err 5 Invalid procedure call or argument |
| environ-256 | `Len(Environ(256))` | err 5 Invalid procedure call or argument |
| environ-huge | `Environ(100000)` | err 6 Overflow |
| environ-str-1 | `Environ("1")` | String  |
| environ-1.5-is-2 | `Environ(1.5) = Environ(2)` | Boolean True |
| environ-2.5-is-2 | `Environ(2.5) = Environ(2)` | Boolean True |
| environ-null | `Environ(Null)` | err 94 Invalid use of Null |
| environ-name-eq | `Environ("PATH=")` | String  |
| environ-name-space-is-name | `Environ(" PATH") = Environ("PATH")` | Boolean True |
| environ-name-trailing-space | `Environ("PATH ") = Environ("PATH")` | Boolean True |
| environ-bool | `Environ(True)` | err 5 Invalid procedure call or argument |
| environ-int-var-is-number | `Environ(CInt(1)) = Environ(1)` | Boolean True |
| environ-str-num-is-name | `Environ("1") = Environ(1)` | Boolean False |
| environ-name-of-first | `Environ(Left(Environ(1), InStr(Environ(1), "=") - 1)) = Mid(Environ(1), InStr(Environ(1), "=") + 1)` | Boolean True |
| environ-200-is-empty | `Environ(200) = ""` | Boolean True |
| environ-255-is-empty | `Environ(255) = ""` | Boolean True |
| environ-empty-arg | `Environ(Empty)` | err 5 Invalid procedure call or argument |
| environ-order-inversions | `Inversions()` | Long 0 |
| environ-count-below-200 | `Count() < 200` | Boolean True |

- **A String is a name, even `"1"`.** A name ignores its case and spaces around it, so `"path"`, `" PATH"` and `"PATH "` are PATH. A missing name is `""`, as is one holding "=". `""` is 5.
- **A number is an index, read as CInt reads it.** 1.5 and 2.5 are 2, while 0, a negative, True and Empty are 5, and 100000 is 6.
- **255 is the bound, not the number of variables.** 200 and 255, both past the last variable, are `""`, and 256 is 5.
- **The entry it gives is `NAME=value`,** and the first one's "=" comes after a name.
- **The entries come in order of name.** `Inversions()` counts neighbouring entries whose names are out of text-compare order, over `Count()` entries, `Count()` being the first n for which `Environ(n + 1)` is `""`. It found none. That is one guest's environment, so it shows the order is by name there, not how VB6 orders names it was given out of order.
- **Null is 94.**

### Switch

| Row | Call | vb6.exe |
|---|---|---|
| switch-first | `Switch(True, "a", True, "b")` | String a |
| switch-second | `Switch(False, 1, True, 2)` | Integer 2 |
| switch-none | `Switch(False, 1, False, 2)` | Null |
| switch-odd | `Switch(True, 1, False)` | err 5 Invalid procedure call or argument |
| switch-one | `Switch(True)` | err 5 Invalid procedure call or argument |
| switch-num-cond | `Switch(0, "zero", 2, "two")` | String two |
| switch-str-cond | `Switch("True", "s")` | String s |
| switch-str-cond-bad | `Switch("x", "s")` | err 13 Type mismatch |
| switch-null-cond | `Switch(Null, 1, True, 2)` | Integer 2 |
| switch-empty-cond | `Switch(Empty, 1, True, 2)` | Integer 2 |
| switch-type | `Switch(True, 5)` | Integer |
| switch-null-value | `Switch(True, Null)` | Null |
| switch-evaluates-values | `Switch(True, F("v1", 1), False, F("v2", 2))` | Integer 1 |
| switch-evaluates-conds | `Switch(F("c1", True), 1, F("c2", True), 2)` | Integer 1 |
| switch-array-cond | `Switch(Array(1), 1)` | err 13 Type mismatch |
| switch-14-pairs | `Switch(False, 1, False, 2, False, 3, False, 4, False, 5, False, 6, False, 7, True, 8)` | Integer 8 |

- **Every argument is evaluated before Switch is called,** values and expressions alike. Both calls ran in each of the two rows that log them.
- **An odd count is 5,** one argument included.
- **Each expression is read as CBool reads it until one holds.**
  - "True", 2 and a Date hold, while "x" and an array are 13.
  - A Collection is 450, since it has no default value to read.
  - A Null or Empty expression does not hold, and raises nothing.
  - **An expression after the one that held is not read,** so `Switch(True, 1, "x", 2)` is 1, not 13.
- **The value comes back as it is,** an Integer as an Integer and Null as Null.
- **Eight pairs are fine.** The row is labelled `switch-14-pairs`, a slip in the probe: its call has eight.

### Error

| Row | Call | vb6.exe |
|---|---|---|
| error-6 | `Error(6)` | String Overflow |
| error-dollar-6 | `Error$(6)` | String Overflow |
| error-0 | `Error(0)` | String  |
| error-1 | `Error(1)` | String Application-defined or object-defined error |
| error-1000 | `Error(1000)` | String Application-defined or object-defined error |
| error-32767 | `Error(32767)` | String Application-defined or object-defined error |
| error-65535 | `Error(65535)` | String Application-defined or object-defined error |
| error-65536 | `Error(65536)` | err 6 Overflow |
| error-minus1 | `Error(-1)` | String Application-defined or object-defined error |
| error-6.6 | `Error(6.6)` | String Out of memory |
| error-5.5 | `Error(5.5)` | String Overflow |
| error-str | `Error("6")` | String Overflow |
| error-null | `Error(Null)` | err 94 Invalid use of Null |
| error-empty | `Error(Empty)` | String  |
| error-none-clean | `Error` | String  |
| error-none-clean-parens | `Error()` | String  |
| error-type | `Error(6)` | String |
| error-dollar-null | `Error$(Null)` | err 94 Invalid use of Null |
| error-vbObjectError | `Error(vbObjectError + 1)` | String Application-defined or object-defined error |

- **The number is read as CLng reads it,** so 65535.5 is 65536 and `"6"` is 6.
- **Past 65535 it is 6,** as it is past a Long. Any negative number is "Application-defined or object-defined error", True's -1 and `vbObjectError + 1` included.
- **Null is 94, an array 13, and Empty is 0,** whose text is "".
- **Error with no number** is the text of the error Err holds: its Description, or where none has been set, its number's text.
  - After `Err.Raise 11`, `Error` and `Error()` are "Division by zero".
  - After `Err.Raise 1000, , "custom text"`, `Error` is "custom text".
  - After `Err.Clear` and `Err.Number = 6`, `Error` is "Overflow".

### The text of every error number

`Error(n)` was called for every n from 0 to 65535. These 88 numbers have a text of their own; the other 65,448 are "Application-defined or object-defined error".

<details><summary>The 88 texts</summary>

| Number | Text |
|---|---|
| 0 | (empty) |
| 3 | Return without GoSub |
| 5 | Invalid procedure call or argument |
| 6 | Overflow |
| 7 | Out of memory |
| 9 | Subscript out of range |
| 10 | This array is fixed or temporarily locked |
| 11 | Division by zero |
| 13 | Type mismatch |
| 14 | Out of string space |
| 16 | Expression too complex |
| 17 | Can't perform requested operation |
| 18 | User interrupt occurred |
| 20 | Resume without error |
| 28 | Out of stack space |
| 35 | Sub or Function not defined |
| 47 | Too many DLL application clients |
| 48 | Error in loading DLL |
| 49 | Bad DLL calling convention |
| 51 | Internal error |
| 52 | Bad file name or number |
| 53 | File not found |
| 54 | Bad file mode |
| 55 | File already open |
| 57 | Device I/O error |
| 58 | File already exists |
| 59 | Bad record length |
| 61 | Disk full |
| 62 | Input past end of file |
| 63 | Bad record number |
| 67 | Too many files |
| 68 | Device unavailable |
| 70 | Permission denied |
| 71 | Disk not ready |
| 74 | Can't rename with different drive |
| 75 | Path/File access error |
| 76 | Path not found |
| 91 | Object variable or With block variable not set |
| 92 | For loop not initialized |
| 93 | Invalid pattern string |
| 94 | Invalid use of Null |
| 96 | Unable to sink events of object because the object is already firing events to the maximum number of event receivers that it supports |
| 97 | Can not call friend function on object which is not an instance of defining class |
| 98 | A property or method call cannot include a reference to a private object, either as an argument or as a return value |
| 321 | Invalid file format |
| 322 | Can't create necessary temporary file |
| 325 | Invalid format in resource file |
| 380 | Invalid property value |
| 381 | Invalid property array index |
| 382 | Set not supported at runtime |
| 383 | Set not supported (read-only property) |
| 385 | Need property array index |
| 387 | Set not permitted |
| 393 | Get not supported at runtime |
| 394 | Get not supported (write-only property) |
| 422 | Property not found |
| 423 | Property or method not found |
| 424 | Object required |
| 429 | ActiveX component can't create object |
| 430 | Class does not support Automation or does not support expected interface |
| 432 | File name or class name not found during Automation operation |
| 438 | Object doesn't support this property or method |
| 440 | Automation error |
| 442 | Connection to type library or object library for remote process has been lost. Press OK for dialog to remove reference. |
| 443 | Automation object does not have a default value |
| 445 | Object doesn't support this action |
| 446 | Object doesn't support named arguments |
| 447 | Object doesn't support current locale setting |
| 448 | Named argument not found |
| 449 | Argument not optional |
| 450 | Wrong number of arguments or invalid property assignment |
| 451 | Property let procedure not defined and property get procedure did not return an object |
| 452 | Invalid ordinal |
| 453 | Specified DLL function not found |
| 454 | Code resource not found |
| 455 | Code resource lock error |
| 457 | This key is already associated with an element of this collection |
| 458 | Variable uses an Automation type not supported in Visual Basic |
| 459 | Object or class does not support the set of events |
| 460 | Invalid clipboard format |
| 461 | Method or data member not found |
| 462 | The remote server machine does not exist or is unavailable |
| 463 | Class not registered on local machine |
| 481 | Invalid picture |
| 482 | Printer error |
| 735 | Can't save file to TEMP |
| 744 | Search text not found |
| 746 | Replacements too long |

</details>

**Err.Raise with no Description takes its text from the same table.** `Err.Raise 340`, `360`, `400` and `31004` are all "Application-defined or object-defined error", although a form or control raising 340 itself has words of its own. HexIDE had given those raise-time words to Err.Raise too.

**21 of HexIDE's own texts had other words** than vb6.exe's for the same number. They are recorded here because the guess each one made is the one the next reader will make:

| Number | vb6.exe | HexIDE had |
|---|---|---|
| 35 | Sub or Function not defined | Sub, Function, or Property not defined |
| 97 | Can not call friend function on object which is not an instance of defining class | A Friend procedure can only be called on an instance of the class that defines it |
| 98 | A property or method call cannot include a reference to a private object, either as an argument or as a return value | A private object can't be passed to, or returned from, a property or method call |
| 322 | Can't create necessary temporary file | Cant create necessary temporary file |
| 382 | Set not supported at runtime | Property Set can't be executed at run time |
| 383 | Set not supported (read-only property) | Property Set can't be used with a read-only property |
| 385 | Need property array index | Need property-array index |
| 387 | Set not permitted | Property Set not permitted |
| 393 | Get not supported at runtime | Property Get can't be executed at run time |
| 394 | Get not supported (write-only property) | Property Get can't be executed on write-only property |
| 429 | ActiveX component can't create object | Component can't create object or return reference to this object |
| 430 | Class does not support Automation or does not support expected interface | Class doesn't support Automation |
| 442 | Connection to type library or object library for remote process has been lost. Press OK for dialog to remove reference. | Lost the connection to the remote process's type or object library |
| 443 | Automation object does not have a default value | Automation object doesn't have a default value |
| 449 | Argument not optional | Argument not optional or invalid property assignment |
| 451 | Property let procedure not defined and property get procedure did not return an object | Object not a collection |
| 453 | Specified DLL function not found | Specified not found |
| 458 | Variable uses an Automation type not supported in Visual Basic | Variable uses a type not supported in Visual Basic |
| 459 | Object or class does not support the set of events | This component doesn't provide that set of events |
| 460 | Invalid clipboard format | Invalid Clipboard format |
| 735 | Can't save file to TEMP | Can't save file to TEMP directory |


### The case of a character

UCase, LCase and StrConv's upper and lower case were compared for every character `Chr(0)` to `Chr(255)` gives; these are the ones that change, letters A to Z aside:

<details><summary>The table</summary>

| Chr | Character | UCase | LCase | StrConv upper | StrConv lower |
|---|---|---|---|---|---|
| 131 | U+0192 | U+0191 | U+0192 | U+0192 | U+0192 |
| 138 | U+0160 | U+0160 | U+0161 | U+0160 | U+0161 |
| 140 | U+0152 | U+0152 | U+0153 | U+0152 | U+0153 |
| 142 | U+017D | U+017D | U+017E | U+017D | U+017E |
| 154 | U+0161 | U+0160 | U+0161 | U+0160 | U+0161 |
| 156 | U+0153 | U+0152 | U+0153 | U+0152 | U+0153 |
| 158 | U+017E | U+017D | U+017E | U+017D | U+017E |
| 159 | U+0178 | U+0178 | U+00FF | U+0178 | U+00FF |
| 192 | U+00C0 | U+00C0 | U+00E0 | U+00C0 | U+00E0 |
| 193 | U+00C1 | U+00C1 | U+00E1 | U+00C1 | U+00E1 |
| 194 | U+00C2 | U+00C2 | U+00E2 | U+00C2 | U+00E2 |
| 195 | U+00C3 | U+00C3 | U+00E3 | U+00C3 | U+00E3 |
| 196 | U+00C4 | U+00C4 | U+00E4 | U+00C4 | U+00E4 |
| 197 | U+00C5 | U+00C5 | U+00E5 | U+00C5 | U+00E5 |
| 198 | U+00C6 | U+00C6 | U+00E6 | U+00C6 | U+00E6 |
| 199 | U+00C7 | U+00C7 | U+00E7 | U+00C7 | U+00E7 |
| 200 | U+00C8 | U+00C8 | U+00E8 | U+00C8 | U+00E8 |
| 201 | U+00C9 | U+00C9 | U+00E9 | U+00C9 | U+00E9 |
| 202 | U+00CA | U+00CA | U+00EA | U+00CA | U+00EA |
| 203 | U+00CB | U+00CB | U+00EB | U+00CB | U+00EB |
| 204 | U+00CC | U+00CC | U+00EC | U+00CC | U+00EC |
| 205 | U+00CD | U+00CD | U+00ED | U+00CD | U+00ED |
| 206 | U+00CE | U+00CE | U+00EE | U+00CE | U+00EE |
| 207 | U+00CF | U+00CF | U+00EF | U+00CF | U+00EF |
| 208 | U+00D0 | U+00D0 | U+00F0 | U+00D0 | U+00F0 |
| 209 | U+00D1 | U+00D1 | U+00F1 | U+00D1 | U+00F1 |
| 210 | U+00D2 | U+00D2 | U+00F2 | U+00D2 | U+00F2 |
| 211 | U+00D3 | U+00D3 | U+00F3 | U+00D3 | U+00F3 |
| 212 | U+00D4 | U+00D4 | U+00F4 | U+00D4 | U+00F4 |
| 213 | U+00D5 | U+00D5 | U+00F5 | U+00D5 | U+00F5 |
| 214 | U+00D6 | U+00D6 | U+00F6 | U+00D6 | U+00F6 |
| 216 | U+00D8 | U+00D8 | U+00F8 | U+00D8 | U+00F8 |
| 217 | U+00D9 | U+00D9 | U+00F9 | U+00D9 | U+00F9 |
| 218 | U+00DA | U+00DA | U+00FA | U+00DA | U+00FA |
| 219 | U+00DB | U+00DB | U+00FB | U+00DB | U+00FB |
| 220 | U+00DC | U+00DC | U+00FC | U+00DC | U+00FC |
| 221 | U+00DD | U+00DD | U+00FD | U+00DD | U+00FD |
| 222 | U+00DE | U+00DE | U+00FE | U+00DE | U+00FE |
| 224 | U+00E0 | U+00C0 | U+00E0 | U+00C0 | U+00E0 |
| 225 | U+00E1 | U+00C1 | U+00E1 | U+00C1 | U+00E1 |
| 226 | U+00E2 | U+00C2 | U+00E2 | U+00C2 | U+00E2 |
| 227 | U+00E3 | U+00C3 | U+00E3 | U+00C3 | U+00E3 |
| 228 | U+00E4 | U+00C4 | U+00E4 | U+00C4 | U+00E4 |
| 229 | U+00E5 | U+00C5 | U+00E5 | U+00C5 | U+00E5 |
| 230 | U+00E6 | U+00C6 | U+00E6 | U+00C6 | U+00E6 |
| 231 | U+00E7 | U+00C7 | U+00E7 | U+00C7 | U+00E7 |
| 232 | U+00E8 | U+00C8 | U+00E8 | U+00C8 | U+00E8 |
| 233 | U+00E9 | U+00C9 | U+00E9 | U+00C9 | U+00E9 |
| 234 | U+00EA | U+00CA | U+00EA | U+00CA | U+00EA |
| 235 | U+00EB | U+00CB | U+00EB | U+00CB | U+00EB |
| 236 | U+00EC | U+00CC | U+00EC | U+00CC | U+00EC |
| 237 | U+00ED | U+00CD | U+00ED | U+00CD | U+00ED |
| 238 | U+00EE | U+00CE | U+00EE | U+00CE | U+00EE |
| 239 | U+00EF | U+00CF | U+00EF | U+00CF | U+00EF |
| 240 | U+00F0 | U+00D0 | U+00F0 | U+00D0 | U+00F0 |
| 241 | U+00F1 | U+00D1 | U+00F1 | U+00D1 | U+00F1 |
| 242 | U+00F2 | U+00D2 | U+00F2 | U+00D2 | U+00F2 |
| 243 | U+00F3 | U+00D3 | U+00F3 | U+00D3 | U+00F3 |
| 244 | U+00F4 | U+00D4 | U+00F4 | U+00D4 | U+00F4 |
| 245 | U+00F5 | U+00D5 | U+00F5 | U+00D5 | U+00F5 |
| 246 | U+00F6 | U+00D6 | U+00F6 | U+00D6 | U+00F6 |
| 248 | U+00F8 | U+00D8 | U+00F8 | U+00D8 | U+00F8 |
| 249 | U+00F9 | U+00D9 | U+00F9 | U+00D9 | U+00F9 |
| 250 | U+00FA | U+00DA | U+00FA | U+00DA | U+00FA |
| 251 | U+00FB | U+00DB | U+00FB | U+00DB | U+00FB |
| 252 | U+00FC | U+00DC | U+00FC | U+00DC | U+00FC |
| 253 | U+00FD | U+00DD | U+00FD | U+00DD | U+00FD |
| 254 | U+00FE | U+00DE | U+00FE | U+00DE | U+00FE |
| 255 | U+00FF | U+0178 | U+00FF | U+0178 | U+00FF |

</details>

Ten calls on characters beyond those letters settle the rule:

| Call | Character | vb6.exe | Its case maps back |
|---|---|---|---|
| `UCase(ChrW(&HFF41))` | fullwidth a, U+FF41 | U+FF21 | yes |
| `LCase(ChrW(&HFF21))` | fullwidth A, U+FF21 | U+FF41 | yes |
| `UCase(ChrW(&H3B1))` | alpha, U+03B1 | U+0391 | yes |
| `LCase(ChrW(&H391))` | capital alpha, U+0391 | U+03B1 | yes |
| `UCase(ChrW(&H3C2))` | final sigma, U+03C2 | U+03C2 (itself) | no |
| `UCase(ChrW(&HB5))` | micro sign, U+00B5 | U+00B5 (itself) | no |
| `UCase(ChrW(&H131))` | dotless i, U+0131 | U+0131 (itself) | no |
| `LCase(ChrW(&H130))` | capital I with dot, U+0130 | U+0130 (itself) | no |
| `UCase(ChrW(&H17F))` | long s, U+017F | U+017F (itself) | no |
| `LCase(ChrW(&H212A))` | Kelvin sign, U+212A | U+212A (itself) | no |

**A character takes its other case only where that case maps back to it.**
- **In upper case:** ƒ is Ƒ and ÿ is Ÿ, because each maps back. µ, ς, ſ and ı keep their case, because the upper case Unicode gives them (Μ, Σ, S, I) lowers to another character.
- **In lower case:** the Kelvin sign and İ keep theirs.

It is the rule VbLike's text fold was measured to follow (#1547). HexIDE's UCase and LCase used .NET's invariant casing, which maps all six.

### Named arguments

Measured as #1465 measured the rest, one project per spelling:
- **StrConv** takes `String`, `Conversion` and `LocaleID`, the first two required. `LCID`, the name its documentation gives, is "Named argument not found".
- **Error and Error$** take `ErrorNumber`, which is optional. `Number` is "Named argument not found".
- **Switch's** parameter is a ParamArray: `Switch(VarExpr:=True)` and `Switch(Expr:=True, Value:=1)` are "Argument in ParamArray may not be named".

### The edges

| Row | Call | vb6.exe |
|---|---|---|
| strconv-upper-nonansi | `Codes(StrConv(ChrW(&H4E00) & ChrW(&H3B1) & ChrW(&H101) & ChrW(&H20AC), vbUpperCase))` | String [63.65.65.8364] |
| strconv-lower-nonansi | `Codes(StrConv(ChrW(&H391) & ChrW(&H100), vbLowerCase))` | String [63.97] |
| strconv-proper-nonansi | `Codes(StrConv(ChrW(&H101) & "b c" & ChrW(&H3B1), vbProperCase))` | String [65.98.32.67.97] |
| strconv-0-nonansi | `Codes(StrConv(ChrW(&H101) & ChrW(&H4E00), 0))` | String [257.19968] |
| strconv-upper-unicode | `StrConv("ab", vbUpperCase Or vbUnicode)` | err 5 Invalid procedure call or argument |
| strconv-unicode-from | `StrConv("ab", vbUnicode Or vbFromUnicode)` | err 5 Invalid procedure call or argument |
| strconv-null-null | `StrConv(Null, Null)` | err 94 Invalid use of Null |
| strconv-null-bad | `StrConv(Null, 256)` | err 5 Invalid procedure call or argument |
| strconv-lcid-null | `StrConv("a", vbUpperCase, Null)` | err 94 Invalid use of Null |
| strconv-lcid-2057 | `StrConv("a", vbUpperCase, 2057)` | String A |
| strconv-lcid-1 | `StrConv("a", vbUpperCase, 1)` | String A |
| strconv-lcid-minus1 | `StrConv("a", vbUpperCase, -1)` | err 5 Invalid procedure call or argument |
| strconv-proper-after-nul-lead | `Codes(StrConv(Chr(0) & "ab", vbProperCase))` | String [0.65.98] |
| strconv-proper-mixed | `StrConv("mcDONALD o'BRIEN", vbProperCase)` | String Mcdonald O'brien |
| strconv-array | `StrConv(Array("a"), vbUpperCase)` | err 13 Type mismatch |
| strcomp-null-and-null-compare | `StrComp(Null, "a", Null)` | err 94 Invalid use of Null |
| strcomp-array-and-null | `StrComp(Array(1), Null)` | err 13 Type mismatch |
| strcomp-null-and-array | `StrComp(Null, Array(1))` | err 13 Type mismatch |
| error-minus-70000 | `Error(-70000)` | String Application-defined or object-defined error |
| error-long-max | `Error(2147483647)` | err 6 Overflow |
| error-long-min | `Error(-2147483648#)` | String Application-defined or object-defined error |
| error-past-long | `Error(2147483648#)` | err 6 Overflow |
| error-true | `Error(True)` | String Application-defined or object-defined error |
| error-65535-5 | `Error(65535.5)` | err 6 Overflow |
| error-array | `Error(Array(6))` | err 13 Type mismatch |
| switch-object-cond | `Switch(New Collection, 1)` | err 450 Wrong number of arguments or invalid property assignment |
| switch-date-cond | `Switch(#1/1/2000#, "d")` | String d |
| switch-cond-error-after-true | `Switch(True, 1, "x", 2)` | Integer 1 |

### In HexIDE (#1560)

All five functions are in the registry with their measured parameter names.
- **StrComp** uses `VbTextCompare` for a compare of 1.
- **StrConv's** cases go through the ANSI code page, best fit included, and use `VbCase`.
- **Environ** reads the process's environment, ignoring case. Its index counts the variables sorted by name, ignoring case, with those whose names start with "=" left out. .NET gives the environment in no order, so HexIDE sorts it.
- **Error** reads `VbErrorMessages`, as Err.Raise now does, and the interpreter's Err when given no number.
- **UCase and LCase** use `VbCase`.

Not done here, each with its issue:
- **vbUnicode and vbFromUnicode** make byte strings (#1510).
- **A compare of 3 and up** (#881).
- **StrComp given no compare in a Text module** (#1572).

