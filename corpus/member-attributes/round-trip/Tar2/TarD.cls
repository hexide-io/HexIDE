VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "TarD"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private mRateD As Double

Public Property Let RateD(ByVal Value As Double)
Attribute RateD.VB_Description = "Cleared, then description only."
    mRateD = Value
End Property

Public Property Get RateD() As Double
    RateD = mRateD
End Property
