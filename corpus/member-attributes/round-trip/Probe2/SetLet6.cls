VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "SetLet6"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private mSpot As Variant

Public Property Set Spot(ByVal Value As Object)
Attribute Spot.VB_Description = "SetLet6 Spot, Set first."
    Set mSpot = Value
End Property

Public Property Let Spot(ByVal Value As Variant)
    mSpot = Value
End Property
