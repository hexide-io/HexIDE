Attribute VB_Name = "ConstV"
Option Explicit
' p4 marker2

Public Const Limit = 5
Attribute Limit.VB_VarDescription = "Const, VB_VarDescription."

Public Sub UseConstV()
    Debug.Print Limit
End Sub
