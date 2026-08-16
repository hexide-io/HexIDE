VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "Grid"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Private mState(0 To 9, 0 To 9) As Integer
Private mShipId(0 To 9, 0 To 9) As Integer
Public Sub Init()
Dim r As Integer, c As Integer
For r = 0 To 9
For c = 0 To 9
mState(r, c) = 0
mShipId(r, c) = -1
Next c
Next r
End Sub
Public Sub Place(ByVal id As Integer, ByVal r As Integer, ByVal c As Integer)
mShipId(r, c) = id
End Sub
Public Function ShipAt(ByVal r As Integer, ByVal c As Integer) As Integer
ShipAt = mShipId(r, c)
End Function
Public Sub Mark(ByVal r As Integer, ByVal c As Integer, ByVal v As Integer)
mState(r, c) = v
End Sub
