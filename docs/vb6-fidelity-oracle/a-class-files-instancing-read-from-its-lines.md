## A class file's Instancing, read from its lines (2026-11-21, #21, IDE, SP6)

**The question.** A `.cls` states its instancing twice:
- `MultiUse` in its `BEGIN` block;
- the `VB_GlobalNameSpace`, `VB_Creatable` and `VB_Exposed` attribute lines.

#21's findings list asks, as Q16, what VB6 does with a class whose lines say PublicNotCreatable in one place and
something else in another. Its example is the shape an early HexIDE wrote: `MultiUse = -1`, `VB_Creatable = True`,
`VB_Exposed = False`.

**The probe.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM, driven on its console. The project was an ActiveX
DLL (`Type=OleDll`) of four classes. Each class had VB6's usual `BEGIN` block and `VB_PredeclaredId = False`, and only
these lines varied:

| Class | `MultiUse` | `VB_GlobalNameSpace` | `VB_Creatable` | `VB_Exposed` | The Properties window's Instancing |
|---|---|---|---|---|---|
| K1 | -1 | False | False | True | `2 - PublicNotCreatable` |
| K2 | -1 | False | True | False | `1 - Private` |
| K3 | 0 | False | True | True | `5 - MultiUse`, changed at load |
| K4 | -1 | True | True | True | `6 - GlobalMultiUse` |

- **The attributes decide Exposed and Creatable.** K2's `MultiUse = -1` didn't make it public; `VB_Exposed = False`
  made it Private. K2's Properties window then listed Instancing and none of the rows a public class has, such as
  `MTSTransactionMode` and `Persistable`. So the early HexIDE shape turns a PublicNotCreatable class into a Private one.
- **`MultiUse = 0` with exposed, creatable attributes is SingleUse,** which an ActiveX DLL can't have. VB6 changed it
  to MultiUse.
  - **At load** it said `The current value of 'K4' instancing property is not valid in this type of project.  The
    property value has been changed.`, with OK and Help, and no `.log`.
  - **On closing,** VB6 asked to save the project file and `K3.cls` only. Its save wrote K3's `BEGIN` block as
    `MultiUse = -1  'True`. Nothing else in K3 changed, and K1, K2 and K4 were not written.

**The message named the wrong class.** It said `'K4'`, but K3 was the class changed. To tell whether it names the last
class loaded, a second DLL held A1 with K3's lines, then A2 and A3 with K4's lines less `VB_GlobalNameSpace`. Its load
said `The current value of 'A3' instancing property ...`, while A1 showed `5 - MultiUse` and the save list named only
`A1.cls`. In both projects the message named the project's last class, not the one it changed.

### Not measured

- **An ActiveX EXE,** where SingleUse is valid, and a Standard EXE, where classes are Private only.
- **A Control project's classes.**
- **The release build.**
