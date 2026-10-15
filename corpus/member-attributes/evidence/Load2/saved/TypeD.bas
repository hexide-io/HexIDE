Attribute VB_Name = "TypeD"
Option Explicit
' p4 marker2

Private Type Pair
    First As Long
    Second As Long
End Type

Public Sub UseTypeD()
    Dim p As Pair
    Debug.Print p.First
End Sub
