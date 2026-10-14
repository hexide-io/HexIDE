VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "TarV"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private mRateV As Variant
Private mExtra As Long

Public Property Let RateV(ByVal Value As Variant)
Attribute RateV.VB_Description = "TarV, Variant, description only."
    mRateV = Value
End Property

Public Property Get RateV() As Variant
    RateV = mRateV
End Property
