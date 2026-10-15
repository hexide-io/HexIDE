Attribute VB_Name = "TypeV"
Option Explicit
' p4 marker

Private Type Duo
Attribute Duo.VB_VarDescription = "Type, VB_VarDescription."
    One As Long
    Two As Long
End Type

Public Sub UseTypeV()
    Dim d As Duo
    Debug.Print d.One
End Sub
