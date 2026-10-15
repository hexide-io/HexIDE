VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "TarDD"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private mRateDD As Double

Public Property Let RateDD(ByVal Value As Double)
    mRateDD = Value
End Property

Public Property Get RateDD() As Double
Attribute RateDD.VB_Description = "Tariff replica, both at once."
Attribute RateDD.VB_UserMemId = 0
    RateDD = mRateDD
End Property
