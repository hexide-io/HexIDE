## A class with VB_PredeclaredId = True (2026-10-28, #2555)

Measured on vb6.exe SP6, every probe built native and as p-code, which logged the same lines. Each probe is a Standard EXE `Sub Main` under `On Error Resume Next`, logging each statement with `Err.Number`; `Show(v)` logs a value as `[value] TypeName/VarType`. The class, in the probe's Standard EXE project:

```vb
Attribute VB_Name = "Class1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit
Public Value As Long
Private Sub Class_Initialize()
    L "Class1 Initialize"
End Sub
Private Sub Class_Terminate()
    L "Class1 Terminate"
End Sub
```

**What the rows say:**
- The class's name is a default instance, made at its **first use**, not when the program starts: `Initialize` logs after "before first use".
- `TypeName(Class1)` is `Class1`. `New Class1` makes a separate instance, with its own `Value`.
- `Set Class1 = Nothing` terminates the default instance. **The next use makes a new one:** even `Class1 Is Nothing` does, so it answers False, after a second `Initialize`, and `Value` is 0 again.
- The instance lives until the program ends: its `Terminate` logs after Main's last line.
- In the log, the two `Terminate`s after "default 5, new 7" are `c`'s, at `Set c = Nothing`, and then the default instance's, at `Set Class1 = Nothing`.

```
before first use
Class1 Initialize
Class1.Value = 5 err 0
Value 5 Class1
Class1 Initialize
default 5, new 7
Class1 Terminate
Class1 Terminate
Set Class1 = Nothing err 0
Class1 Initialize
Is Nothing after: False err 0
Value after Nothing: 0 err 0
end of Main
predeclared: done
Class1 Terminate
```
