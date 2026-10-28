VERSION 5.00
Begin VB.Form frmFind 
   BorderStyle     =   3  'Fixed Dialog
   Caption         =   "Find"
   ClientHeight    =   1335
   ClientLeft      =   45
   ClientTop       =   330
   ClientWidth     =   5415
   MaxButton       =   0   'False
   MinButton       =   0   'False
   ScaleHeight     =   1335
   ScaleWidth      =   5415
   ShowInTaskbar   =   0   'False
   StartUpPosition =   1  'CenterOwner
   Begin VB.CommandButton cmdCancel 
      Cancel          =   -1  'True
      Caption         =   "Cancel"
      Height          =   345
      Left            =   4080
      TabIndex        =   5
      Top             =   570
      Width           =   1215
   End
   Begin VB.CommandButton cmdFindNext 
      Caption         =   "&Find Next"
      Default         =   -1  'True
      Enabled         =   0   'False
      Height          =   345
      Left            =   4080
      TabIndex        =   4
      Top             =   120
      Width           =   1215
   End
   Begin VB.Frame fraDirection 
      Caption         =   "Direction"
      Height          =   615
      Left            =   2160
      TabIndex        =   3
      Top             =   600
      Width           =   1785
      Begin VB.OptionButton optDown 
         Caption         =   "&Down"
         Height          =   255
         Left            =   900
         TabIndex        =   7
         Top             =   270
         Value           =   -1  'True
         Width           =   795
      End
      Begin VB.OptionButton optUp 
         Caption         =   "&Up"
         Height          =   255
         Left            =   120
         TabIndex        =   6
         Top             =   270
         Width           =   675
      End
   End
   Begin VB.CheckBox chkMatchCase 
      Caption         =   "Match &case"
      Height          =   255
      Left            =   120
      TabIndex        =   2
      Top             =   870
      Width           =   1455
   End
   Begin VB.TextBox txtFind 
      Height          =   315
      Left            =   1080
      TabIndex        =   1
      Top             =   135
      Width           =   2865
   End
   Begin VB.Label lblFind 
      Caption         =   "Fi&nd what:"
      Height          =   255
      Left            =   120
      TabIndex        =   0
      Top             =   180
      Width           =   915
   End
End
Attribute VB_Name = "frmFind"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' Modeless, owned by the Notepad window: the text stays editable while the dialog is open, and the dialog stays above it.

Private Sub Form_Load()
    txtFind.Text = frmNotepad.LastFind
    If frmNotepad.LastMatchCase Then chkMatchCase.Value = vbChecked
    If frmNotepad.LastFindDown Then optDown.Value = True Else optUp.Value = True
    cmdFindNext.Enabled = Len(txtFind.Text) > 0
End Sub

Private Sub txtFind_Change()
    cmdFindNext.Enabled = Len(txtFind.Text) > 0
End Sub

Private Sub cmdFindNext_Click()
    frmNotepad.FindText txtFind.Text, chkMatchCase.Value = vbChecked, optDown.Value
End Sub

Private Sub cmdCancel_Click()
    Unload Me
End Sub
