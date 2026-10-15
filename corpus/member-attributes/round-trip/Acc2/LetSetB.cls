VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "LetSetB"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private mPLs As Variant

Public Property Let PLs(ByVal Value As Variant)
Attribute PLs.VB_Description = "LetSetB, caret in Set."
    mPLs = Value
End Property

Public Property Set PLs(ByVal Value As Variant)
    Set mPLs = Value
End Property

Public Sub OtherLetSetB()
    Debug.Print 0
End Sub
