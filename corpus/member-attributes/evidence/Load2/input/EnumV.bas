Attribute VB_Name = "EnumV"
Option Explicit
' p4 marker

Public Enum Shades
Attribute Shades.VB_VarDescription = "Enum, VB_VarDescription."
    Cyan = 1
    Magenta = 2
End Enum

Public Sub UseEnumV()
    Debug.Print Cyan
End Sub
