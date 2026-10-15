Attribute VB_Name = "TypeV"
Option Explicit
' p4 marker2

Private Type Duo
    One As Long
    Two As Long
End Type

Public Sub UseTypeV()
    Dim d As Duo
    Debug.Print d.One
End Sub
