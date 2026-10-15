VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "Ledger"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Public Event Changed(ByVal Amount As Long)
Attribute Changed.VB_Description = "Raised after an entry."

Public Owner As String
Attribute Owner.VB_VarDescription = "Name of the ""owner""."
Public A As Long, B As Long
Attribute B.VB_VarDescription = "Second counter."

Private mTotal As Long
Private mRate As Double

Public Function Total() As Long
Attribute Total.VB_Description = "Sum of every entry."
Attribute Total.VB_UserMemId = 0
    Total = mTotal
End Function

Public Property Get Rate() As Double
Attribute Rate.VB_Description = "Rate applied to each entry."
    Rate = mRate
End Property

Public Property Let Rate(ByVal Value As Double)
    mRate = Value
End Property

Public Function Average(ByVal First As Long, _
                        ByVal Second As Long) As Double
Attribute Average.VB_Description = "Mean of two values."
    Average = (First + Second) / 2
End Function

Public Sub Reset()
Attribute Reset.VB_HelpID = 42
Attribute Reset.VB_MemberFlags = "40"
    mTotal = 0
    RaiseEvent Changed(0)
End Sub
