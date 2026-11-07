## GetSetting, SaveSetting, DeleteSetting and GetAllSettings (2026-10-23, #1562)

Measured with `/make` on vb6.exe 6.00.8176 (the release build) with the MSVBVM60 6.00.9848 (SP6) runtime, under en-GB, in five batches. Native and p-code agree on every row.

Each row ran under `On Error Resume Next` against the guest's own registry, with the probe's program name `HexProbe1562` (`A` in the tables). A row logs any error its setup statements raised, then what it read back. `Show` prints a value's type and text, and an array's bounds and every row with its two types. `BS` is a constant holding a backslash. Between batches, PowerShell on the guest read the registry directly, and seeded it with values of other registry types, as another program could write them.

**Where the settings live.** The location is the one the issue gave, confirmed by reading the guest's registry after a probe saved `"value"` and `42`: `HKEY_CURRENT_USER\Software\VB and VBA Program Settings\<appname>\<section>`. Each setting is a REG_SZ value named by its key, a number included (`42` is the text `"42"`).

**What the issue guessed, and what vb6.exe said:**
- **GetAllSettings gives a `String()` array, not a Variant array.** It is two-dimensional, 0 To the last key by 0 To 1, and assigns to a `Dim s() As String`. A missing section gives Empty, as guessed, and so does one whose last value was deleted.
- **DeleteSetting's section is optional.** `DeleteSetting appname` compiles, and deletes every section of the program.
- **Values and key names are Unicode.** `ChrW(&H4E00)` round-trips as a value and as a key name, and is not `?`. An embedded `Chr(0)` survives too: `"a" & Chr(0) & "b"` reads back with a `Len` of 3.

### The first batch

| Row | Statements | vb6.exe |
|---|---|---|
| get-missing-app | `r = GetSetting(A & "NoSuchApp", "S", "K")` | String [] |
| get-missing-app-default | `r = GetSetting(A & "NoSuchApp", "S", "K", "d")` | String [d] |
| get-default-num | `r = GetSetting(A & "NoSuchApp", "S", "K", 5)` | String [5] |
| get-default-double | `r = GetSetting(A & "NoSuchApp", "S", "K", 1.5)` | String [1.5] |
| get-default-null | `r = GetSetting(A & "NoSuchApp", "S", "K", Null)` | err 94 Invalid use of Null |
| get-default-empty | `r = GetSetting(A & "NoSuchApp", "S", "K", Empty)` | String [] |
| get-default-date | `r = GetSetting(A & "NoSuchApp", "S", "K", #1/2/2003#)` | String [02/01/2003] |
| get-default-bool | `r = GetSetting(A & "NoSuchApp", "S", "K", True)` | String [True] |
| save-then-get | `SaveSetting A, "S1", "K1", "v1"; r = GetSetting(A, "S1", "K1")` | String [v1] |
| get-missing-key | `r = GetSetting(A, "S1", "NoKey", "d")` | String [d] |
| get-missing-section | `r = GetSetting(A, "NoSec", "K1", "d")` | String [d] |
| get-case | `r = GetSetting(UCase(A), "s1", "k1")` | String [v1] |
| save-int | `SaveSetting A, "S1", "Int", 5; r = GetSetting(A, "S1", "Int")` | String [5] |
| save-double | `SaveSetting A, "S1", "Dbl", 1.5; r = GetSetting(A, "S1", "Dbl")` | String [1.5] |
| save-bool | `SaveSetting A, "S1", "Bool", True; r = GetSetting(A, "S1", "Bool")` | String [True] |
| save-date | `SaveSetting A, "S1", "Date", #1/2/2003#; r = GetSetting(A, "S1", "Date")` | String [02/01/2003] |
| save-null | `SaveSetting A, "S1", "Nul", Null; r = GetSetting(A, "S1", "Nul", "absent")` | setup err 94 Invalid use of Null; String [absent] |
| save-empty | `SaveSetting A, "S1", "Emp", Empty; r = GetSetting(A, "S1", "Emp", "absent")` | String [] |
| save-empty-string | `SaveSetting A, "S1", "EStr", ""; r = GetSetting(A, "S1", "EStr", "absent")` | String [] |
| save-empty-app | `SaveSetting "", "S1", "K", "v"` | setup err 5 Invalid procedure call or argument; ran |
| save-empty-section | `SaveSetting A, "", "K", "v"` | setup err 5 Invalid procedure call or argument; ran |
| save-empty-key | `SaveSetting A, "S1", "", "v"; r = GetSetting(A, "S1", "", "absent")` | setup err 5 Invalid procedure call or argument; err 5 Invalid procedure call or argument |
| save-backslash-key | `SaveSetting A, "S1", "a\b", "v"; r = GetSetting(A, "S1", "a\b", "absent")` | String [v] |
| save-backslash-section | `SaveSetting A, "S2\Sub", "K", "v"; r = GetSetting(A, "S2\Sub", "K", "absent")` | String [v] |
| backslash-section-parent | `r = GetAllSettings(A, "S2")` | Empty |
| save-backslash-app | `SaveSetting A & "\X", "S", "K", "v"; r = GetSetting(A, "X\S", "K", "absent")` | String [v] |
| save-numeric-names | `SaveSetting 1562, 2, 3, 4; r = GetSetting("1562", "2", "3", "absent")` | String [4] |
| get-numeric-names | `r = GetSetting(1562, 2, 3)` | String [4] |
| save-null-app | `SaveSetting Null, "S", "K", "v"` | setup err 94 Invalid use of Null; ran |
| save-null-key | `SaveSetting A, "S1", Null, "v"` | setup err 94 Invalid use of Null; ran |
| get-null-app | `r = GetSetting(Null, "S", "K")` | err 94 Invalid use of Null |
| get-null-key | `r = GetSetting(A, "S1", Null)` | err 94 Invalid use of Null |
| get-empty-app | `r = GetSetting("", "S", "K", "d")` | err 5 Invalid procedure call or argument |
| get-empty-section | `r = GetSetting(A, "", "K", "d")` | err 5 Invalid procedure call or argument |
| overwrite | `SaveSetting A, "S1", "K1", "v2"; r = GetSetting(A, "S1", "K1")` | String [v2] |
| long-value | `SaveSetting A, "S1", "Long", String(10000, "x"); r = Len(GetSetting(A, "S1", "Long"))` | Long [10000] |
| value-with-nul | `SaveSetting A, "S1", "Nul0", "a" & Chr(0) & "b"; r = Len(GetSetting(A, "S1", "Nul0"))` | Long [3] |
| value-unicode | `SaveSetting A, "S1", "Uni", ChrW(&H4E00) & ChrW(&H101) & "x"; r = AscW(Mid(GetSetting(A, "S1", "Uni"), 1, 1)) & "." & AscW(Mid(GetSetting(A, "S1", "Uni"), 2, 1)) & "." & Len(GetSetting(A, "S1", "Uni"))` | String [19968.257.3] |
| key-unicode | `SaveSetting A, "S1", ChrW(&H4E00), "u"; r = GetSetting(A, "S1", ChrW(&H4E00), "absent") & "\|" & GetSetting(A, "S1", "?", "absent")` | String [u\|absent] |
| getall-order | `SaveSetting A, "S3", "b", "2"; SaveSetting A, "S3", "a", "1"; SaveSetting A, "S3", "C", "3"; r = GetAllSettings(A, "S3")` | Array String() (0 To 2, 0 To 1) b=2[String,String] a=1[String,String] C=3[String,String] |
| getall-order-2 | `SaveSetting A, "S6", "z", "26"; SaveSetting A, "S6", "m", "13"; SaveSetting A, "S6", "a", "1"; SaveSetting A, "S6", "M", "x"; r = GetAllSettings(A, "S6")` | Array String() (0 To 2, 0 To 1) z=26[String,String] m=x[String,String] a=1[String,String] |
| getall-after-overwrite | `SaveSetting A, "S6", "z", "again"; r = GetAllSettings(A, "S6")` | Array String() (0 To 2, 0 To 1) z=again[String,String] m=x[String,String] a=1[String,String] |
| getall-missing-section | `r = GetAllSettings(A, "NoSec")` | Empty |
| getall-missing-app | `r = GetAllSettings(A & "NoSuchApp", "S")` | Empty |
| getall-case | `r = GetAllSettings(LCase(A), "s3")` | Array String() (0 To 2, 0 To 1) b=2[String,String] a=1[String,String] C=3[String,String] |
| getall-empty-value | `SaveSetting A, "S7", "e", ""; r = GetAllSettings(A, "S7")` | Array String() (0 To 0, 0 To 1) e=[String,String] |
| delete-key | `DeleteSetting A, "S3", "b"; r = GetSetting(A, "S3", "b", "gone")` | String [gone] |
| delete-missing-key | `DeleteSetting A, "S3", "nokey"` | setup err 5 Invalid procedure call or argument; ran |
| delete-missing-section | `DeleteSetting A, "NoSec"` | setup err 5 Invalid procedure call or argument; ran |
| delete-missing-key-missing-section | `DeleteSetting A, "NoSec", "K"` | setup err 5 Invalid procedure call or argument; ran |
| delete-missing-app | `DeleteSetting A & "NoSuchApp", "S"` | setup err 5 Invalid procedure call or argument; ran |
| delete-last-key | `SaveSetting A, "S4", "only", "1"; DeleteSetting A, "S4", "only"; r = GetAllSettings(A, "S4")` | Empty |
| delete-section-after-last-key | `DeleteSetting A, "S4"` | ran |
| delete-section | `DeleteSetting A, "S3"; r = GetSetting(A, "S3", "a", "gone")` | String [gone] |
| getall-deleted-section | `r = GetAllSettings(A, "S3")` | Empty |
| delete-section-with-subkey | `DeleteSetting A, "S2"; r = GetSetting(A, "S2\Sub", "K", "gone")` | setup err 5 Invalid procedure call or argument; String [v] |
| delete-case | `SaveSetting A, "S5", "Key", "v"; DeleteSetting UCase(A), "s5", "KEY"; r = GetSetting(A, "S5", "Key", "gone")` | String [gone] |
| delete-empty-section | `DeleteSetting A, ""` | setup err 5 Invalid procedure call or argument; ran |
| delete-empty-key | `DeleteSetting A, "S1", ""; r = GetSetting(A, "S1", "", "gone")` | setup err 5 Invalid procedure call or argument; err 5 Invalid procedure call or argument |
| delete-null-key | `DeleteSetting A, "S1", Null` | setup err 94 Invalid use of Null; ran |
| delete-null-section | `DeleteSetting A, Null` | setup err 94 Invalid use of Null; ran |
| get-type | `r = TypeName(GetSetting(A, "S1", "K1"))` | String [String] |

- **GetSetting:**
  - A missing program, section or key gives the default, or `""` without one. The answer is always a String: a default of `5` is `"5"`, `True` is `"True"`, and a Date is its short date.
  - **The default is converted only when it is the answer.** A Null default is 94 where the key is missing, and no error where it is present (fourth batch).
- **Names:**
  - Names are compared without case, and keep the case they were made with: saving `M` over `m` changes `m`'s value and keeps the name `m`.
  - An empty program, section or key name is 5, for all four statements, and Null is 94.
  - Numbers are names too: `SaveSetting 1562, 2, 3, 4` is read back by `GetSetting("1562", "2", "3")`.
- **SaveSetting:**
  - The setting is stored as its text, as a store into a String makes it, so 1.5 is `"1.5"`.
  - A Null setting is 94 and writes nothing. Empty writes `""`.
- **A backslash makes a path.** In a section name it nests a key: `S2\Sub` is the key `Sub` under `S2`, and `GetAllSettings(A, "S2")` on the empty parent gives Empty. In a program name it does the same: `SaveSetting A & "\X", "S"` is read by `GetSetting(A, "X\S")`. In a key name it is just a character.
- **GetAllSettings:**
  - Keys are listed in the order they were made. Overwriting one keeps its place.
  - An empty value is listed as `""`.
- **DeleteSetting:**
  - A missing program, section or key is 5.
  - **Deleting is not recursive.** A section with a section under it is 5, and both are left as they were.
  - **Deleting a section's last key leaves the section.** GetAllSettings on it is Empty, but `DeleteSetting A, "S4"` afterwards raises nothing, so it was still there.

### The second batch

| Row | Statements | vb6.exe |
|---|---|---|
| delete-app | `SaveSetting A, "T1", "k", "v"; SaveSetting A, "T2", "k", "v"; DeleteSetting A; r = GetSetting(A, "T1", "k", "gone") & "\|" & GetSetting(A, "T2", "k", "gone")` | String [gone\|gone] |
| delete-app-again | `DeleteSetting A` | setup err 5 Invalid procedure call or argument; ran |
| delete-app-with-empty-section | `SaveSetting A & "B", "T", "k", "v"; DeleteSetting A & "B", "T", "k"; DeleteSetting A & "B"; r = GetAllSettings(A & "B", "T")` | Empty |
| delete-app-with-nested-section | `SaveSetting A & "C", "T\U", "k", "v"; DeleteSetting A & "C"; r = GetSetting(A & "C", "T\U", "k", "gone")` | setup err 5 Invalid procedure call or argument; String [v] |
| delete-app-null | `DeleteSetting Null` | setup err 94 Invalid use of Null; ran |
| delete-app-empty | `DeleteSetting ""` | setup err 5 Invalid procedure call or argument; ran |
| section-leading-backslash | `SaveSetting A & "D", BS & "S", "k", "lead"; r = GetSetting(A & "D", BS & "S", "k", "absent") & "\|" & GetSetting(A & "D", "S", "k", "absent")` | String [lead\|lead] |
| section-trailing-backslash | `SaveSetting A & "D", "S" & BS, "k", "trail"; r = GetSetting(A & "D", "S" & BS, "k", "absent") & "\|" & GetSetting(A & "D", "S", "k", "absent")` | String [trail\|trail] |
| section-double-backslash | `SaveSetting A & "D", "S" & BS & BS & "T", "k", "double"; r = GetSetting(A & "D", "S" & BS & BS & "T", "k", "absent") & "\|" & GetSetting(A & "D", "S" & BS & "T", "k", "absent")` | String [double\|double] |
| section-only-backslash | `SaveSetting A & "D", BS, "k", "only"; r = GetSetting(A & "D", BS, "k", "absent")` | String [only] |
| app-trailing-space | `SaveSetting A & " ", "S", "k", "space"; r = GetSetting(A, "S", "k", "absent") & "\|" & GetSetting(A & " ", "S", "k", "absent")` | String [absent\|space] |
| key-spaces | `SaveSetting A, "S", " k ", "spaced"; r = GetSetting(A, "S", "k", "absent") & "\|" & GetSetting(A, "S", " k ", "absent")` | String [absent\|spaced] |
| long-key | `SaveSetting A, "S", String(300, "k"), "long"; r = GetSetting(A, "S", String(300, "k"), "absent")` | String [long] |
| long-section | `SaveSetting A, String(300, "s"), "k", "long"; r = GetSetting(A, String(300, "s"), "k", "absent")` | setup err 5 Invalid procedure call or argument; err 5 Invalid procedure call or argument |
| types-sz | `r = Len(GetSetting(A & "Types", "S", "Sz")) & " " & GetSetting(A & "Types", "S", "Sz")` | String [3 abc] |
| types-dword | `r = GetSetting(A & "Types", "S", "Dword", "absent")` | err 5 Invalid procedure call or argument |
| types-qword | `r = GetSetting(A & "Types", "S", "Qword", "absent")` | err 5 Invalid procedure call or argument |
| types-expand | `r = GetSetting(A & "Types", "S", "Expand", "absent")` | err 5 Invalid procedure call or argument |
| types-multi | `r = Len(GetSetting(A & "Types", "S", "Multi", "absent"))` | err 5 Invalid procedure call or argument |
| types-binary | `r = Len(GetSetting(A & "Types", "S", "Binary", "absent"))` | err 5 Invalid procedure call or argument |
| types-getall | `r = GetAllSettings(A & "Types", "S")` | err 5 Invalid procedure call or argument |
| types-default-value | `r = GetSetting(A & "Types", "S", "(default)", "absent")` | String [absent] |
| save-over-dword | `SaveSetting A & "Types", "S", "Dword", "now text"; r = GetSetting(A & "Types", "S", "Dword", "absent")` | String [now text] |
| save-after-delete-app | `DeleteSetting A; SaveSetting A, "S", "k", "back"; r = GetSetting(A, "S", "k", "absent")` | String [back] |

- **DeleteSetting with no section deletes each section, then the program.** One section with a section under it stops the delete with 5, and the nested key survives. Whether sections before it went is not measured. A program whose only section is empty is deleted.
- **A section's backslashes are passed over at its ends and where doubled.** `"\S"`, `"S\"` and `"S"` are one section, as are `"S\\T"` and `"S\T"`, and `"\"` alone is the program's own key.
- **Names are not trimmed.** `A & " "` is another program, and `" k "` another key.
- **A section name of 300 characters is 5,** past the registry's limit of 255 for a key name, while a key name of 300 is fine. The boundary itself was not probed; HexIDE takes it to be the registry's 255.
- **A value that is not REG_SZ is 5.** That covers DWORD, QWORD, EXPAND_SZ, MULTI_SZ and BINARY, for GetSetting, and for GetAllSettings over a section holding any of them. SaveSetting over one replaces it with text. The key `"(default)"` is not the unnamed default value.

### The third batch

| Row | Statements | vb6.exe |
|---|---|---|
| getall-sz-and-default | `r = GetAllSettings(A & "Types", "Sd")` | Array String() (0 To 2, 0 To 1) Sz=abc[String,String] Empty=[String,String] =dv[String,String] |
| getall-sz-and-expand | `r = GetAllSettings(A & "Types", "Se")` | err 5 Invalid procedure call or argument |
| getall-app-key-values | `r = GetAllSettings(A & "Types", "\")` | Array String() (0 To 0, 0 To 1) AppVal=top[String,String] |
| get-app-key-value | `r = GetSetting(A & "Types", "\", "AppVal", "absent")` | String [top] |
| get-empty-sz | `r = GetSetting(A & "Types", "Sd", "Empty", "absent")` | String [] |

- **GetAllSettings lists an unnamed default value**, as the key `""`, in its place in the registry's order.
- **A section of `"\"` reads the values on the program's own key.**

### The fourth batch

| Row | Statements | vb6.exe |
|---|---|---|
| null-default-key-exists | `SaveSetting A, "S1", "K", "v"; r = GetSetting(A, "S1", "K", Null)` | String [v] |
| array-default-missing | `r = GetSetting(A, "S1", "NoKey", Array(1))` | err 13 Type mismatch |
| array-default-key-exists | `r = GetSetting(A, "S1", "K", Array(1))` | String [v] |
| object-default | `r = GetSetting(A, "S1", "NoKey", New Collection)` | err 450 Wrong number of arguments or invalid property assignment |
| array-app | `r = GetSetting(Array(1), "S1", "K")` | err 13 Type mismatch |
| array-setting | `SaveSetting A, "S1", "Arr", Array(1); r = GetSetting(A, "S1", "Arr", "absent")` | setup err 13 Type mismatch; String [absent] |
| empty-app-null-setting | `SaveSetting "", "S1", "K", Null` | setup err 94 Invalid use of Null; ran |
| null-app-empty-section | `SaveSetting Null, "", "K", "v"` | setup err 94 Invalid use of Null; ran |
| empty-app-null-key-get | `r = GetSetting("", "S1", Null)` | err 94 Invalid use of Null |
| missing-section-null-default | `r = GetSetting(A, "NoSec", "K", Null)` | err 94 Invalid use of Null |
| call-save | `Call SaveSetting(A, "S1", "Called", "c"); r = GetSetting(A, "S1", "Called", "absent")` | String [c] |
| call-delete | `Call DeleteSetting(A, "S1", "Called"); r = GetSetting(A, "S1", "Called", "gone")` | String [gone] |
| vba-save | `VBA.SaveSetting A, "S1", "Qual", "q"; r = VBA.GetSetting(A, "S1", "Qual", "absent")` | String [q] |
| vba-getall | `r = UBound(VBA.GetAllSettings(A, "S1"), 1)` | Long [1] |
| vba-delete | `VBA.DeleteSetting A, "S1", "Qual"; r = GetSetting(A, "S1", "Qual", "gone")` | String [gone] |
| interaction-get | `r = VBA.Interaction.GetSetting(A, "S1", "K", "absent")` | String [v] |
| getall-into-string-array | `Dim s() As String; s = GetAllSettings(A, "S1"); r = UBound(s, 1) & " " & s(0, 0)` | String [0 K] |
| getall-missing-into-string-array | `Dim t() As String; t = GetAllSettings(A, "NoSec"); r = UBound(t, 1)` | setup err 13 Type mismatch; err 9 Subscript out of range |
| getall-two-dims | `r = UBound(GetAllSettings(A, "S1"), 2)` | Long [1] |

- **The names are converted before any is checked.** Null is 94 and an array 13, so `SaveSetting "", "S1", "K", Null` is 94, not 5.
- **The default is converted only when it is the answer.** An array default is 13 where the key is missing, and no error where it is present. So too for an object, measured in a fifth batch: a Collection default is 450 and Nothing 91 where the key is missing, and either is no error where the key has a value.
- **`Call SaveSetting(...)`, `Call DeleteSetting(...)`, `VBA.SaveSetting`, `VBA.GetAllSettings` and `VBA.Interaction.GetSetting` all run.** The four are intrinsics of the VBA library as well as statements in the grammar.
- **Empty assigned to a `String` array is 13, and leaves the array unallocated.** That is array assignment, not these four: the rule is measured in full under "A Let into an array variable" (#998, #1588).

### Named arguments, and arguments left out

Each statement is one project:

| Statement | vb6.exe |
|---|---|
| `DeleteSetting "HexProbe1562Nope"` | ran: 5 Empty |
| `v = GetSetting("a", "s")` | Argument not optional |
| `v = GetSetting(AppName:="HexProbe1562Nope", Section:="s", Key:="k", Default:="d")` | ran: 0 String [d] |
| `v = GetSetting(AppName:="HexProbe1562Nope", Section:="s", Key:="k", DefaultValue:="d")` | Named argument not found |
| `v = GetSetting(AppName:="HexProbe1562Nope", Key:="k")` | Argument not optional |
| `v = GetAllSettings(AppName:="HexProbe1562Nope", Section:="s")` | ran: 0 Empty |
| `v = GetAllSettings("HexProbe1562Nope")` | Argument not optional |
| `SaveSetting AppName:="HexProbe1562Named", Section:="s", Key:="k", Setting:="v"` | ran: 0 Empty |
| `SaveSetting AppName:="HexProbe1562Named", Section:="s", Key:="k", Value:="v"` | Named argument not found |
| `SaveSetting "HexProbe1562Named", "s", "k"` | Argument not optional |
| `DeleteSetting AppName:="HexProbe1562Named", Section:="s", Key:="k"` | ran: 5 Empty |
| `SaveSetting("HexProbe1562Named", "s", "k", "v")` | Syntax error |
| `GetSetting "HexProbe1562Nope", "s", "k"` | ran: 0 Empty |

The parameters are `AppName`, `Section`, `Key` and `Default` for GetSetting, the first three required. GetAllSettings has `AppName` and `Section`, both required. SaveSetting has `AppName`, `Section`, `Key` and `Setting`, not `Value`, all required. DeleteSetting has `AppName`, `Section` and `Key`, only the first required. GetSetting as a statement runs.

### In HexIDE (#1562)

The four run as registered intrinsics. The two statements, and `Call` and `VBA.`, all go through them.
- **VB6's rules sit in one place,** `VB6BuiltIns.Settings`. The store under them only keeps keys (`IVbSettingsStore`), so every rule above is pinned once, over a store in memory, by `SettingsProgramTests`.
- **On Windows the store is the registry, under VB6's own key,** so a program run in HexIDE and the same program compiled by VB6 share their settings. `VbSettingsStoreTests` checks that each value is REG_SZ, and that the embedded `Chr(0)` and the Unicode survive.
- **Elsewhere the store is one file in HexIDE's user-data directory,** `program-settings.json`, which `--user-data-dir` moves. It is one file rather than one per program, because a program's name can hold `..` and must never become part of a path.
- **Not measured, and so chosen:**
  - A program and section that name no key at all, `"\"` and `"\"`, are 5 here.
  - A program whose second section cannot be deleted loses none of its sections here.
- **Refused in other words:** `SaveSetting` with three arguments, and `SaveSetting(...)` in parentheses, are refused as the program loads, by the grammar, in the parser's words. vb6.exe says "Argument not optional" and "Syntax error".

