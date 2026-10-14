VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "LetSet6"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private mSlot As Variant

Public Property Let Slot(ByVal Value As Variant)
Attribute Slot.VB_Description = "LetSet6 Slot, Let first."
    mSlot = Value
End Property

Public Property Set Slot(ByVal Value As Object)
    Set mSlot = Value
End Property
