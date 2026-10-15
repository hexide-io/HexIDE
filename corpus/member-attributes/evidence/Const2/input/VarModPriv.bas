Attribute VB_Name = "VarModPriv"
Option Explicit
' p4 marker

Private Hidden2 As Long
Attribute Hidden2.VB_VarDescription = "Private module variable."

Public Sub UseHidden2()
    Debug.Print Hidden2
End Sub
