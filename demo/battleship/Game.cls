VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "Game"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Private mGrid As Grid
Private mFleet(0 To 2) As Ship
Private mCount As Integer
Private mSunk As Integer
Public Event Result(ByVal r As Integer, ByVal c As Integer, ByVal outcome As String)
Private Sub Class_Initialize()
Set mGrid = New Grid
mGrid.Init
mCount = 0
mSunk = 0
End Sub
Public Sub AddShip(ByVal size As Integer, ByVal r As Integer, ByVal c As Integer)
Dim s As Ship
Set s = New Ship
s.Size = size
Set mFleet(mCount) = s
Dim i As Integer
For i = 0 To size - 1
mGrid.Place mCount, r, c + i
Next i
mCount = mCount + 1
End Sub
Public Sub Fire(ByVal r As Integer, ByVal c As Integer)
Dim id As Integer
id = mGrid.ShipAt(r, c)
If id < 0 Then
mGrid.Mark r, c, 3
RaiseEvent Result(r, c, "MISS")
Else
mGrid.Mark r, c, 2
Dim s As Ship
Set s = mFleet(id)
s.RegisterHit
If s.IsSunk Then
mSunk = mSunk + 1
RaiseEvent Result(r, c, "SUNK")
Else
RaiseEvent Result(r, c, "HIT")
End If
End If
End Sub
Public Property Get AllSunk() As Boolean
AllSunk = (mSunk >= mCount)
End Property
