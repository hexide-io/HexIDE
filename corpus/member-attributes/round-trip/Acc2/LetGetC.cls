VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "LetGetC"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private mPLgC As Variant

Public Property Let PLgC(ByVal Value As Variant)
Attribute PLgC.VB_Description = "LetGetC, picked from the list."
    mPLgC = Value
End Property

Public Property Get PLgC() As Variant
    PLgC = mPLgC
End Property

Public Sub OtherLetGetC()
    Debug.Print 0
End Sub
