VERSION 5.00
Object = "{BDC217C8-ED16-11CD-956C-0000C04E4C0A}#1.1#0"; "TABCTL32.OCX"
Begin VB.Form Form1 
   Caption         =   "Form1"
   ClientHeight    =   4500
   ClientLeft      =   60
   ClientTop       =   345
   ClientWidth     =   6000
   LinkTopic       =   "Form1"
   ScaleHeight     =   4500
   ScaleWidth      =   6000
   StartUpPosition =   3  'Windows Default
   Begin TabDlg.SSTab SSTab1 
      Height          =   3195
      Left            =   120
      TabIndex        =   0
      Top             =   120
      Width           =   4995
      _ExtentX        =   8811
      _ExtentY        =   5636
      _Version        =   393216
      Tab             =   1
      TabHeight       =   520
      TabCaption(0)   =   "Tab 0"
      TabPicture(0)   =   "Form1.frx":0000
      Tab(0).ControlEnabled=   0   'False
      Tab(0).Control(0)=   "Command1"
      Tab(0).Control(0).Enabled=   0   'False
      Tab(0).ControlCount=   1
      TabCaption(1)   =   "Tab 1"
      TabPicture(1)   =   "Form1.frx":001C
      Tab(1).ControlEnabled=   -1  'True
      Tab(1).Control(0)=   "Text1"
      Tab(1).Control(0).Enabled=   0   'False
      Tab(1).ControlCount=   1
      TabCaption(2)   =   "Tab 2"
      TabPicture(2)   =   "Form1.frx":0038
      Tab(2).ControlEnabled=   0   'False
      Tab(2).Control(0)=   "Check1"
      Tab(2).Control(0).Enabled=   0   'False
      Tab(2).ControlCount=   1
      Begin VB.CheckBox Check1 
         Caption         =   "Check1"
         Height          =   315
         Left            =   -74370
         TabIndex        =   3
         Top             =   1830
         Width           =   1815
      End
      Begin VB.TextBox Text1 
         Height          =   390
         Left            =   480
         TabIndex        =   2
         Text            =   "Text1"
         Top             =   1230
         Width           =   1815
      End
      Begin VB.CommandButton Command1 
         Caption         =   "Command1"
         Height          =   465
         Left            =   -74670
         TabIndex        =   1
         Top             =   630
         Width           =   1515
      End
   End
End
Attribute VB_Name = "Form1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' saved
