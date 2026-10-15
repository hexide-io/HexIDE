Attribute VB_Name = "PasteMod4"
Option Explicit

Public Sub Existing4m()
    Debug.Print 4
End Sub

Public Function Pasted() As Long
Attribute Pasted.VB_Description = "From the clipboard."
    Pasted = 1
End Function

