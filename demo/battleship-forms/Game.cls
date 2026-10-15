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
Option Explicit

Private mGrid As Grid
Private mFleet(0 To 4) As Ship
Private mCount As Integer
Private mSunk As Integer

Public Event Result(ByVal r As Integer, ByVal c As Integer, ByVal outcome As String)

Public Sub Init(ByVal n As Integer)
    Set mGrid = New Grid
    mGrid.Init n
    mCount = 0
    mSunk = 0
End Sub

' A fleet for the board: two ships on the smallest, the classic five on 10 x 10.
Public Sub PlaceFleet()
    Select Case mGrid.Size
    Case Is >= 10
        AddRandomShip 5
        AddRandomShip 4
        AddRandomShip 3
        AddRandomShip 3
        AddRandomShip 2
    Case Is >= 7
        AddRandomShip 4
        AddRandomShip 3
        AddRandomShip 2
    Case Else
        AddRandomShip 3
        AddRandomShip 2
    End Select
End Sub

Private Sub AddRandomShip(ByVal size As Integer)
    Dim r As Integer, c As Integer, across As Boolean
    Do
        across = (Rnd < 0.5)
        r = Int(Rnd * mGrid.Size)
        c = Int(Rnd * mGrid.Size)
    Loop Until Fits(size, r, c, across)
    AddShip size, r, c, across
End Sub

Private Function Fits(ByVal size As Integer, ByVal r As Integer, ByVal c As Integer, ByVal across As Boolean) As Boolean
    Dim i As Integer
    For i = 0 To size - 1
        If across Then
            If c + i >= mGrid.Size Then Exit Function
            If mGrid.ShipAt(r, c + i) >= 0 Then Exit Function
        Else
            If r + i >= mGrid.Size Then Exit Function
            If mGrid.ShipAt(r + i, c) >= 0 Then Exit Function
        End If
    Next i
    Fits = True
End Function

Public Sub AddShip(ByVal size As Integer, ByVal r As Integer, ByVal c As Integer, ByVal across As Boolean)
    Dim s As Ship
    Set s = New Ship
    s.Size = size
    Set mFleet(mCount) = s
    Dim i As Integer
    For i = 0 To size - 1
        If across Then
            mGrid.Place mCount, r, c + i
        Else
            mGrid.Place mCount, r + i, c
        End If
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

Public Function Fired(ByVal r As Integer, ByVal c As Integer) As Boolean
    Fired = (mGrid.StateAt(r, c) <> 0)
End Function

Public Function ShipAt(ByVal r As Integer, ByVal c As Integer) As Integer
    ShipAt = mGrid.ShipAt(r, c)
End Function

Public Property Get ShipsLeft() As Integer
    ShipsLeft = mCount - mSunk
End Property

Public Property Get AllSunk() As Boolean
    AllSunk = (mSunk >= mCount)
End Property
