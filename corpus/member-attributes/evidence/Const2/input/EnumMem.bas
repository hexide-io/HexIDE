Attribute VB_Name = "EnumMem"
Option Explicit
' p4 marker

Public Enum Sizes
    Small = 1
Attribute Small.VB_VarDescription = "Enum member."
    Large = 2
End Enum

Public Sub UseSizes()
    Debug.Print Small
End Sub
