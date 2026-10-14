Attribute VB_Name = "EnumD"
Option Explicit
' p4 marker

Public Enum Colours
Attribute Colours.VB_Description = "Enum, VB_Description."
    Red = 1
    Green = 2
End Enum

Public Sub UseEnumD()
    Debug.Print Red
End Sub
