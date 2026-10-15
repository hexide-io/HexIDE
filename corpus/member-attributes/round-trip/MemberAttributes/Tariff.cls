VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "Tariff"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private mRate As Double
Private WithEvents mLedger As Ledger
Attribute mLedger.VB_VarHelpID = -1

Public Property Let Rate(ByVal Value As Double)
    mRate = Value
End Property

Public Property Get Rate() As Double
Attribute Rate.VB_Description = "Tariff rate."
Attribute Rate.VB_UserMemId = 0
    Rate = mRate
End Property
