Attribute VB_Name = "ConstPriv"
Option Explicit
' p4 marker2

Private Const Hidden = 5
Attribute Hidden.VB_VarDescription = "Private module const."

Public Sub UseHidden()
    Debug.Print Hidden
End Sub
