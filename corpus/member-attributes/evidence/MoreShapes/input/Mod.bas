Attribute VB_Name = "Mod"
Option Explicit
' p4 marker

Public Counter As Long
Attribute Counter.VB_VarDescription = "A module variable."

Public Sub Main()
Attribute Main.VB_Description = "Entry point."
    Debug.Print "start"
End Sub
