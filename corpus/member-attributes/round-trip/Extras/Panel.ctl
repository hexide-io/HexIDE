VERSION 5.00
Begin VB.UserControl Panel 
   ClientHeight    =   1200
   ClientLeft      =   0
   ClientTop       =   0
   ClientWidth     =   2400
   ScaleHeight     =   1200
   ScaleWidth      =   2400
End
Attribute VB_Name = "Panel"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Public Event Clicked()
Attribute Clicked.VB_Description = "Raised on click."
Attribute Clicked.VB_MemberFlags = "200"

Public Spare As Long
Attribute Spare.VB_VarMemberFlags = "40"
Attribute Spare.VB_VarUserMemId = 0
Attribute Spare.VB_VarProcData = ";Behavior"
Attribute Spare.VB_VarHelpID = 12
Attribute Spare.VB_VarDescription = "A spare."
Public A As Long: Public B As Long
Attribute B.VB_VarDescription = "After a colon."

Private mFull As Long

Public Property Get Full() As Long
Attribute Full.VB_Description = "Everything set."
Attribute Full.VB_HelpID = 11
Attribute Full.VB_ProcData.VB_Invoke_Property = "StandardColor;Appearance"
Attribute Full.VB_UserMemId = -520
Attribute Full.VB_MemberFlags = "127c"
    Full = mFull
End Property

Public Property Let Full(ByVal Value As Long)
    mFull = Value
End Property

Public Property Let WriteOnly(ByVal Value As Long)
Attribute WriteOnly.VB_Description = "No getter."
    mFull = Value
End Property

Public Function Longest() As Long
Attribute Longest.VB_Description = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|0100aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|0200aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    Longest = 0
End Function
