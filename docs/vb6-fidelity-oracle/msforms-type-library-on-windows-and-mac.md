## MSForms' type library on 32-bit Windows, 64-bit Windows and the Mac (2026-11-22, #5150)

**The question.** #5150 asks what the MSForms type library declares on each platform Office VBA runs on, before
#1115 pins the controls: every coclass, interface, member, parameter and constant, and where the platforms differ.
The decision on #5150 is to implement the union, flagging any member where the platforms' declarations can't both
be honoured.

**Where each library came from:**

| Platform | Office | The file |
|---|---|---|
| 32-bit Windows | Microsoft 365 x86, 16.0.20527.20196, Current Channel | `FM20.DLL` 16.0.20527.20196, in Office's `root\vfs\SystemX86` |
| 64-bit Windows | Microsoft 365 x64, 16.0.20527.20112, Current Channel | `FM20.DLL` 16.0.20527.20016, in Office's `root\vfs\System` |
| Mac | Excel 16.115 (16.115.26110837), macOS 27.2.1, arm64 | `fm20.tlb` and `fm20xc.tlb`, in the app bundle's `Contents/SharedSupport/Type Libraries` |

**How they were read.**
- **The reader** used `LoadTypeLibEx` with `REGKIND_NONE`, so nothing was registered. It wrote out each type's kind, GUID
  and flags, each implemented interface, and each member: its dispatch id, invoke kind, return type, every parameter's
  type, flags and default, and every constant's value. Only names and types were read: no help strings, and nothing
  was disassembled or run.
- **Where:** the 32-bit DLL was read in 32-bit PowerShell and the 64-bit DLL in 64-bit PowerShell, each on the host
  from a copy of the guest's file. The Mac's `.tlb` files were read on Windows, from copies.
- **The comparison** matched types by name, members by type, name and invoke kind, and constants by type and name.

**Each Windows `FM20.DLL` holds two type libraries,** both named MSForms, version 2.0:
- `{0D452EE1-E08F-101A-852E-02608C4D0BB4}`, as resource 1, is the one registered under `HKCR\TypeLib`.
- `{AC2DE821-36A2-11CF-8053-00AA006009FA}`, as resource 2, isn't registered on either guest.
- There's no resource 3.

The Mac ships the same two as `fm20.tlb` and `fm20xc.tlb`. **Every one of the six reports `SYS_WIN32`,** the 64-bit
DLL's and the Mac's included. What the second library is for wasn't established. Its control interfaces carry more
members: `IMdcCheckBox` has 133 functions there against 70 in the first.

### 32-bit and 64-bit Windows: one surface

**Both libraries are identical** between the two DLLs: every type, implemented interface, member, parameter type and
flag, default and constant.

| Library | Types | Functions | Constants and fields |
|---|---|---|---|
| `{0D452EE1…}` | 171 | 1757 | 199 |
| `{AC2DE821…}` | 131 | 2569 | 199 |

- **Nothing in either is pointer-sized.** No member uses `LongPtr`, `LongLong` or any other 8-byte integer. The only
  handle type is `OLE_HANDLE`, an alias of `I4`, used only by the `IFont` interface the library carries (`hFont`,
  `AddRefHfont` and `ReleaseHfont`). No member is a window handle.
- **The one difference a reader sees is computed:** an interface's instance size reads 4 from the 32-bit DLL and 8 from
  the 64-bit one. `LoadTypeLibEx` works that out for the process doing the reading, so it isn't in either file.

### Windows and the Mac

**No member is declared with different types on the two.** Where a member exists on both, its return type, parameter
types, dispatch id and invoke kind agree, in both libraries. Where they differ, it's in whether a type or member exists,
in its flags, or in the case of a parameter name:

| Library | Types: Windows, Mac, both | Functions: Windows, Mac, both | Constants and fields: Windows, Mac, both |
|---|---|---|---|
| `{0D452EE1…}` | 171, 118, 115 | 1757, 1590, 1574 | 199, 204, 199 |
| `{AC2DE821…}` | 131, 118, 115 | 2569, 2575, 2559 | 199, 204, 199 |

Every difference:

| What | Windows, 32-bit and 64-bit | Mac |
|---|---|---|
| **The hidden twins:** `CheckBox2`, `ComboBox2`, `CommandButton2`, `DataObject2`, `Frame2`, `Image2`, `Label2`, `ListBox2`, `MultiPage2`, `OptionButton2`, `ScrollBar2`, `SpinButton2`, `TabStrip2`, `TextBox2`, `ToggleButton2`, `UserForm2` | In both libraries, all flagged hidden. Each implements the same interfaces as the coclass without the 2 | **Absent** from both |
| **The HTML controls,** first library only: `HTMLCheckbox`, `HTMLHidden`, `HTMLImage`, `HTMLOption`, `HTMLPassword`, `HTMLReset`, `HTMLSelect`, `HTMLSubmit`, `HTMLText` and `HTMLTextArea` | The ten coclasses and their ten hidden `*2` twins, the ten `IWHTML…` interfaces, and ten `WHTMLControlEvents…` event interfaces (`WHTMLControlEvents`, then `1` to `7`, `9` and `10`) | **Absent** |
| **`TextAlign`,** get and let, on `IMdcCheckBox`, `IMdcList`, `IMdcOptionButton` and `IMdcToggleButton`: the CheckBox, ListBox, OptionButton and ToggleButton | Present in both libraries | **Absent** from both |
| **`FormEvents`' `Enter` and `Exit`,** second library only | Present | **Absent** |
| **`IPicture`, `Picture` and `StdPicture`** | Not in either library | In both: 16 functions and 5 fields (`Handle`, `hPal`, `Type`, `Width`, `Height`) |
| **`ControlSource`,** get and let | `IControl`, flagged bindable and displaybind | Flagged bindable and **hidden**. In the second library the same on every control interface and `_UserForm` |
| **`RowSource` and `RowSourceType`,** get and let | `IControl`, bindable and displaybind | Bindable, **hidden and nonbrowsable**. In the second library the same on every control interface and `_UserForm` |
| **`VerticalScrollBarSide`,** get and let, on `_UserForm`, `IOptionFrame` and `IPage` | Bindable and nonbrowsable | Bindable and displaybind |
| **`ICommandButton.Value`,** get and let | Bindable, displaybind, hidden | Bindable, hidden |
| **`IPage.Accelerator`,** get and let | Bindable, displaybind | Bindable, displaybind, **hidden** |
| **`ImageEvents`' `Click` and `DblClick`** | `Click` hidden and uidefault; `DblClick` hidden | `Click` uidefault; `DblClick` with no flags |
| **`List` and `Column`,** let, on `IMdcCombo` and `IMdcList` | `cParamsOpt` 2 | `cParamsOpt` 0. Both mark the two index parameters optional, so the count is all that differs |
| **Parameter names** | 69 members in each library: `X` and `Y` in the mouse and drag events, for instance | The same names in another case, `x` and `y` |

**In the second library the function counts cross:** the Mac has more (2575 against 2569). It lacks 10 (`Enter`,
`Exit` and the eight `TextAlign` functions), and adds 16 in `IPicture` and `Picture`.

**What it shows:**
- **For MSForms, 32-bit and 64-bit VBA7 are one surface,** so a re-implementation has no declared type to choose
  between them.
- **The Mac's surface is Windows' less some types and members, plus three picture types.**
- **The union has no member whose declarations conflict.** The decision on #5150 asked for any such member to be
  brought back; none was found. The only disagreements inside the union are flags, which govern what the Object Browser
  and completion show rather than how a call binds, and `List` and `Column`'s optional-parameter count.

**Unexplained:**
- why every library, the 64-bit DLL's and the Mac's included, reports `SYS_WIN32`;
- what the second library, `{AC2DE821…}`, is for, and which host loads it;
- why `List` and `Column`'s `cParamsOpt` differs when the parameters' own flags agree.

**Not measured:**
- **Behaviour.** A type library says what's declared, not what happens when it's called. Whether a member present on
  the Mac does nothing or raises an error there needs VBA probes on the Mac.
- **Late binding to what the Mac doesn't declare,** such as `TextAlign` on a Mac CheckBox.
- **What each Object Browser shows.**

**In HexIDE:** there's no MSForms implementation yet (#1114, #1115).
