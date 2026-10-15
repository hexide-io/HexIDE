Attribute VB_Name = "ConstAway"
Option Explicit
' p4 marker2

Public Const Far = 1
Attribute Far.VB_VarDescription = "Away from its const."

Public Sub UseFar()
    Debug.Print Far
End Sub
