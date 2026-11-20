## A Public Type in a UserControl, and in an ActiveX project's modules (2026-11-20, #5042)

**The question.** A Standard EXE's class or form can't declare a `Public Type`. That is measured in the section "A Public
Type in a class or a form" of [a-public-array-in-an-object-module.md](a-public-array-in-an-object-module.md), from oracle
PR #5043. VB6's message there names a *private* object module. Is a UserControl one, and does a public class or
UserControl in an ActiveX project accept the Type?

**The probe.** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, on the Win10 guest, in session 0.
- **Builds:** one project per row, each built with `/make` as native code and as p-code. The two builds agreed on every
  row.
- **The Type:** `T`, with one field, `a(1 To 3) As Long`. Each module declares it, then `Private x As T`.
- **Built, not run:** no client loaded a DLL or an OCX. The Standard EXE rows have a `Sub Main` that writes one line, and
  U0's ran.

| Row | Project | The module, and its `Attribute VB_Exposed` | The Type | vb6.exe |
|---|---|---|---|---|
| U0 | Standard EXE | a UserControl, False | `Private Type T` | builds and runs |
| U1 | Standard EXE | a UserControl, False | `Public Type T` | **refused:** "Cannot define a Public user-defined type within a private object module" |
| D1 | ActiveX DLL | a MultiUse class, True | `Public Type T` | builds |
| D3 | ActiveX DLL | a MultiUse class, True | `Type T`, with no `Public` or `Private` | builds |
| D4 | ActiveX DLL | a Private class, False, beside a MultiUse class without a Type | `Public Type T` | refused, the same |
| A1 | ActiveX Control | a public UserControl, True | `Public Type T` | builds |
| A2 | ActiveX Control | a private UserControl, False, beside a public UserControl without a Type | `Public Type T` | refused, the same |
| D2 | ActiveX DLL | its only class, Private, False | `Public Type T` | refused for another reason: "No creatable public component detected." |

- **Where each refusal points:** the declaring file, at the line of the `Type` statement (`Line 2`), as in the earlier
  section.
- **Why D4 and A2 carry a second module:** D2 shows that an ActiveX DLL with no public class is refused before the Type
  is checked.

What these add:
- **The module's `VB_Exposed` decides.** A Public Type, or a Type with no keyword, is refused in a module whose
  `Attribute VB_Exposed` is False, whatever the project type. It is accepted where it is True.
- **A UserControl is an object module too.** In a Standard EXE it is private, and it refuses the Type as a class or a form
  does.

**Not measured:**
- a Standard EXE class whose file says `Attribute VB_Exposed = True`;
- `Friend Type` or `Global Type`;
- a PropertyPage, a DataReport or a UserDocument;
- a client using the DLL's or the OCX's Type.

**In HexIDE**, at main 589aaf4db, read in the code, not run:
- **What the interpreter loads:** standard modules, classes, forms, and a UserControl that has a form part
  (`VBLoader.InterpreterModules`, and `VBProgram`'s constructor).
- **What it reads:** no module's `VB_Exposed`, and not the project's type.
- **What runs:** a class's or a form's Public Type loads and runs, as the earlier section records.

#5042 tracks the refusal.
