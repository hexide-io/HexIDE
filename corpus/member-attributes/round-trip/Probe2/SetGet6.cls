VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "SetGet6"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private mHolder As Object

Public Property Set Holder(ByVal Value As Object)
Attribute Holder.VB_Description = "SetGet6 Holder, Set first."
    Set mHolder = Value
End Property

Public Property Get Holder() As Object
    Set Holder = mHolder
End Property
