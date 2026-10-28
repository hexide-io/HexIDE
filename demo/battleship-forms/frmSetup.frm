VERSION 5.00
Begin VB.Form frmSetup 
   BorderStyle     =   3  'Fixed Dialog
   Caption         =   "New game"
   ClientHeight    =   2160
   ClientLeft      =   45
   ClientTop       =   330
   ClientWidth     =   3960
   MaxButton       =   0   'False
   MinButton       =   0   'False
   ScaleHeight     =   2160
   ScaleWidth      =   3960
   ShowInTaskbar   =   0   'False
   StartUpPosition =   2  'CenterScreen
   Begin VB.CommandButton cmdCancel 
      Cancel          =   -1  'True
      Caption         =   "Cancel"
      Height          =   375
      Left            =   2640
      TabIndex        =   7
      Top             =   1560
      Width           =   1095
   End
   Begin VB.CommandButton cmdOK 
      Caption         =   "OK"
      Default         =   -1  'True
      Height          =   375
      Left            =   1440
      TabIndex        =   6
      Top             =   1560
      Width           =   1095
   End
   Begin VB.OptionButton optSize 
      Caption         =   "10 x 10"
      Height          =   255
      Index           =   2
      Left            =   2640
      TabIndex        =   5
      Top             =   1080
      Width           =   1095
   End
   Begin VB.OptionButton optSize 
      Caption         =   "7 x 7"
      Height          =   255
      Index           =   1
      Left            =   1440
      TabIndex        =   4
      Top             =   1080
      Width           =   1095
   End
   Begin VB.OptionButton optSize 
      Caption         =   "5 x 5"
      Height          =   255
      Index           =   0
      Left            =   240
      TabIndex        =   3
      Top             =   1080
      Value           =   -1  'True
      Width           =   1095
   End
   Begin VB.TextBox txtName 
      Height          =   315
      Left            =   1440
      TabIndex        =   1
      Top             =   240
      Width           =   2295
   End
   Begin VB.Label lblSize 
      Caption         =   "Board size:"
      Height          =   255
      Left            =   240
      TabIndex        =   2
      Top             =   720
      Width           =   1095
   End
   Begin VB.Label lblName 
      Caption         =   "Your &name:"
      Height          =   255
      Left            =   240
      TabIndex        =   0
      Top             =   285
      Width           =   1095
   End
End
Attribute VB_Name = "frmSetup"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' The board size the chosen option button stands for.
Public Property Get BoardSize() As Integer
    If optSize(2).Value Then
        BoardSize = 10
    ElseIf optSize(1).Value Then
        BoardSize = 7
    Else
        BoardSize = 5
    End If
End Property

Private Sub cmdOK_Click()
    If Len(Trim$(txtName.Text)) = 0 Then
        MsgBox "Please enter your name.", vbExclamation, "New game"
        Exit Sub
    End If
    gCancelled = False
    ' Hidden, not unloaded, so Main can still read the name and the size.
    Me.Hide
End Sub

Private Sub cmdCancel_Click()
    Unload Me
End Sub
