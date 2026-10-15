VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "DeclShapes"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit
' p4 marker2

Private Declare Function GetTickCount Lib "kernel32" () As Long
Attribute GetTickCount.VB_Description = "Under a Declare."
Public Const K As Long = 1

Private Sub Hidden()
Attribute Hidden.VB_Description = "Under a Private Sub."
End Sub

Public Sub Noted() ' trailing comment
Attribute Noted.VB_Description = "Under a commented declaration."
End Sub

Friend Function Pal() As Long
Attribute Pal.VB_Description = "Under a Friend Function."
End Function

Public Static Function Kept() As Long
Attribute Kept.VB_Description = "Under a Static Function."
End Function

Public Sub Indented()
Attribute Indented.VB_Description = "Indented."
End Sub

Public Sub Spaced()
Attribute Spaced.VB_Description = "Extra spaces."
End Sub
