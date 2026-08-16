VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "Announcer"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Private WithEvents mGame As Game
Public Sub Watch(ByVal g As Game)
Set mGame = g
End Sub
Private Sub mGame_Result(ByVal r As Integer, ByVal c As Integer, ByVal outcome As String)
Module1.Log "(" & r & "," & c & ") " & outcome
End Sub
