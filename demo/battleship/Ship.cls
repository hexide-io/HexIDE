VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "Ship"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Private mSize As Integer
Private mHits As Integer
Public Property Get Size() As Integer
Size = mSize
End Property
Public Property Let Size(ByVal v As Integer)
mSize = v
End Property
Public Sub RegisterHit()
mHits = mHits + 1
End Sub
Public Property Get IsSunk() As Boolean
IsSunk = (mHits >= mSize)
End Property
