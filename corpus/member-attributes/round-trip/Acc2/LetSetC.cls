VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "LetSetC"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private mPLsC As Variant

Public Property Let PLsC(ByVal Value As Variant)
Attribute PLsC.VB_Description = "LetSetC, picked from the list."
    mPLsC = Value
End Property

Public Property Set PLsC(ByVal Value As Variant)
    Set mPLsC = Value
End Property

Public Sub OtherLetSetC()
    Debug.Print 0
End Sub
