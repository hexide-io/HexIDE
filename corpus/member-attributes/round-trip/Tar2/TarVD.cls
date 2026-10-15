VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "TarVD"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private mRateVD As Variant

Public Property Let RateVD(ByVal Value As Variant)
Attribute RateVD.VB_Description = "TarVD, Variant, with (Default)."
Attribute RateVD.VB_UserMemId = 0
    mRateVD = Value
End Property

Public Property Get RateVD() As Variant
    RateVD = mRateVD
End Property
