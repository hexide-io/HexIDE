Attribute VB_Name = "ConstTyped"
Option Explicit
' p4 marker

Public Const Typed As Long = 1
Attribute Typed.VB_VarDescription = "Typed module const."

Public Sub UseTyped()
    Debug.Print Typed
End Sub
