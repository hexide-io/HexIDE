VERSION 5.00
Begin VB.Form Main 
   Caption         =   "Main"
   ClientHeight    =   3090
   ClientLeft      =   60
   ClientTop       =   450
   ClientWidth     =   4680
   LinkTopic       =   "Main"
   ScaleHeight     =   3090
   ScaleWidth      =   4680
   StartUpPosition =   3  'Windows Default
End
Attribute VB_Name = "Main"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit
Private WithEvents mLedger As Ledger
Attribute mLedger.VB_VarHelpID = -1

Public Sub ShowTotal()
Attribute ShowTotal.VB_Description = "Shows the total."
    Caption = "Total"
End Sub
