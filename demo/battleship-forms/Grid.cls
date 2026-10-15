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
Option Explicit

' Each cell's state, 0 for open sea, 2 for a hit and 3 for a miss, and the ship on it, -1 for none.
Private mSize As Integer
Private mState() As Integer
Private mShipId() As Integer

Public Sub Init(ByVal n As Integer)
    Dim r As Integer, c As Integer
    mSize = n
    ReDim mState(0 To n - 1, 0 To n - 1)
    ReDim mShipId(0 To n - 1, 0 To n - 1)
    For r = 0 To n - 1
        For c = 0 To n - 1
            mShipId(r, c) = -1
        Next c
    Next r
End Sub

Public Property Get Size() As Integer
    Size = mSize
End Property

Public Sub Place(ByVal id As Integer, ByVal r As Integer, ByVal c As Integer)
    mShipId(r, c) = id
End Sub

Public Function ShipAt(ByVal r As Integer, ByVal c As Integer) As Integer
    ShipAt = mShipId(r, c)
End Function

Public Sub Mark(ByVal r As Integer, ByVal c As Integer, ByVal v As Integer)
    mState(r, c) = v
End Sub

Public Function StateAt(ByVal r As Integer, ByVal c As Integer) As Integer
    StateAt = mState(r, c)
End Function
