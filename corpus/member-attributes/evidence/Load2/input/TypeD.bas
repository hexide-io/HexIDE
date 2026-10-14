Attribute VB_Name = "TypeD"
Option Explicit
' p4 marker

Private Type Pair
Attribute Pair.VB_Description = "Type, VB_Description."
    First As Long
    Second As Long
End Type

Public Sub UseTypeD()
    Dim p As Pair
    Debug.Print p.First
End Sub
