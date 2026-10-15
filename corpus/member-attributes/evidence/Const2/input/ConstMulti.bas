Attribute VB_Name = "ConstMulti"
Option Explicit
' p4 marker

Public Const C1 = 1, C2 = 2
Attribute C2.VB_VarDescription = "Second of two consts."

Public Sub UseC()
    Debug.Print C1 + C2
End Sub
