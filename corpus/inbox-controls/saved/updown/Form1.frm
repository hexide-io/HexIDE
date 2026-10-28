VERSION 5.00
Object = "{86CF1D34-0C5F-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCT2.OCX"
Begin VB.Form Form1 
   Caption         =   "Form1"
   ClientHeight    =   7200
   ClientLeft      =   60
   ClientTop       =   345
   ClientWidth     =   9600
   LinkTopic       =   "Form1"
   ScaleHeight     =   7200
   ScaleWidth      =   9600
   StartUpPosition =   3  'Windows Default
   Begin MSComCtl2.UpDown UpDown4 
      Height          =   750
      Left            =   4665
      TabIndex        =   5
      Top             =   3225
      Width           =   255
      _ExtentX        =   450
      _ExtentY        =   1323
      _Version        =   393216
   End
   Begin VB.TextBox Text1 
      Height          =   315
      Left            =   120
      TabIndex        =   0
      Text            =   "Text1"
      Top             =   120
      Width           =   1200
   End
   Begin MSComCtl2.UpDown UpDown1 
      Height          =   255
      Left            =   3000
      TabIndex        =   1
      Top             =   120
      Width           =   495
      _ExtentX        =   873
      _ExtentY        =   450
      _Version        =   393216
      Value           =   5
      Alignment       =   0
      Increment       =   2
      Max             =   50
      Min             =   -5
      Orientation     =   1
      Wrap            =   -1  'True
      Enabled         =   0   'False
      OLEDropMode     =   1
   End
   Begin MSComCtl2.UpDown UpDown2 
      Height          =   495
      Left            =   3000
      TabIndex        =   2
      Top             =   900
      Width           =   255
      _ExtentX        =   450
      _ExtentY        =   873
      _Version        =   393216
   End
   Begin VB.TextBox Text2 
      Height          =   315
      Left            =   120
      TabIndex        =   3
      Text            =   "Text2"
      Top             =   1800
      Width           =   945
   End
   Begin MSComCtl2.UpDown UpDown3 
      Height          =   315
      Left            =   1066
      TabIndex        =   4
      Top             =   1800
      Width           =   255
      _ExtentX        =   450
      _ExtentY        =   556
      _Version        =   393216
      AutoBuddy       =   -1  'True
      BuddyControl    =   "Text2"
      BuddyDispid     =   196610
      OrigLeft        =   3000
      OrigTop         =   1800
      OrigRight       =   3255
      OrigBottom      =   2295
      SyncBuddy       =   -1  'True
      BuddyProperty   =   0
   End
End
Attribute VB_Name = "Form1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' saved
