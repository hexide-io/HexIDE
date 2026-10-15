VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "GetSetB"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private mPGs As Variant

Public Property Get PGs() As Variant
Attribute PGs.VB_Description = "GetSetB, picked from the list."
    PGs = mPGs
End Property

Public Property Set PGs(ByVal Value As Variant)
    Set mPGs = Value
End Property

Public Sub OtherGetSetB()
Attribute OtherGetSetB.VB_Description = "GetSetB, caret in Set."
    Debug.Print 0
End Sub
