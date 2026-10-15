VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "LetGetA"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private mPLgA As Variant

Public Property Let PLgA(ByVal Value As Variant)
Attribute PLgA.VB_Description = "LetGetA, picked from the list."
    mPLgA = Value
End Property

Public Property Get PLgA() As Variant
    PLgA = mPLgA
End Property

Public Sub OtherLetGetA()
Attribute OtherLetGetA.VB_Description = "LetGetA, caret in Let."
    Debug.Print 0
End Sub
