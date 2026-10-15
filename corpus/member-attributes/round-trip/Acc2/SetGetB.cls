VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "SetGetB"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private mPSg As Variant

Public Property Set PSg(ByVal Value As Variant)
Attribute PSg.VB_Description = "SetGetB, caret in Get."
    Set mPSg = Value
End Property

Public Property Get PSg() As Variant
    PSg = mPSg
End Property

Public Sub OtherSetGetB()
    Debug.Print 0
End Sub
