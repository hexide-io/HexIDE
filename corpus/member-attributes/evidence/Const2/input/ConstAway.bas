Attribute VB_Name = "ConstAway"
Option Explicit
' p4 marker

Public Const Far = 1

Public Sub UseFar()
Attribute Far.VB_VarDescription = "Away from its const."
    Debug.Print Far
End Sub
