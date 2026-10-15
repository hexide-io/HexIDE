VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "SetLetB"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private mPSl As Variant

Public Property Set PSl(ByVal Value As Variant)
Attribute PSl.VB_Description = "SetLetB, caret in Let."
    Set mPSl = Value
End Property

Public Property Let PSl(ByVal Value As Variant)
    mPSl = Value
End Property

Public Sub OtherSetLetB()
    Debug.Print 0
End Sub
