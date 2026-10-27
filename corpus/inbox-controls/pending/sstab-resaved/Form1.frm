VERSION 5.00
Object = "{BDC217C8-ED16-11CD-956C-0000C04E4C0A}#1.1#0"; "TABCTL32.OCX"
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
   Begin TabDlg.SSTab SSTab4 
      Height          =   750
      Left            =   4050
      TabIndex        =   11
      Top             =   3225
      Width           =   1845
      _ExtentX        =   3254
      _ExtentY        =   1323
      _Version        =   393216
      TabHeight       =   520
      TabCaption(0)   =   "Tab 0"
      TabPicture(0)   =   "Form1.frx":0000
      Tab(0).ControlEnabled=   -1  'True
      Tab(0).ControlCount=   0
      TabCaption(1)   =   "Tab 1"
      Tab(1).ControlEnabled=   0   'False
      Tab(1).ControlCount=   0
      TabCaption(2)   =   "Tab 2"
      Tab(2).ControlEnabled=   0   'False
      Tab(2).ControlCount=   0
   End
   Begin TabDlg.SSTab SSTab3 
      Height          =   2600
      Left            =   5000
      TabIndex        =   4
      Top             =   120
      Width           =   4400
      _ExtentX        =   7752
      _ExtentY        =   4577
      _Version        =   393216
      Tab             =   1
      TabHeight       =   520
      TabCaption(0)   =   "S0"
      Tab(0).ControlEnabled=   -1  'True
      Tab(0).Control(0)=   "Label2"
      Tab(0).Control(0).Enabled=   0   'False
      Tab(0).Control(1)=   "Text2"
      Tab(0).Control(1).Enabled=   0   'False
      Tab(0).ControlCount=   2
      TabCaption(1)   =   "S1"
      Tab(1).ControlEnabled=   -1  'True
      Tab(1).Control(0)=   "Label2"
      Tab(1).Control(0).Enabled=   0   'False
      Tab(1).Control(1)=   "Text2"
      Tab(1).Control(1).Enabled=   0   'False
      Tab(1).ControlCount=   2
      TabCaption(2)   =   "S2"
      Tab(2).ControlEnabled=   0   'False
      Tab(2).Control(0)=   "Command2"
      Tab(2).Control(0).Enabled=   0   'False
      Tab(2).ControlCount=   1
      Begin VB.OptionButton Option2 
         Caption         =   "Option2"
         Height          =   255
         Left            =   -74000
         TabIndex        =   5
         Top             =   1800
         Width           =   1200
      End
      Begin VB.CheckBox Check2 
         Caption         =   "Check2"
         Height          =   315
         Left            =   -74370
         TabIndex        =   7
         Top             =   1000
         Width           =   1200
      End
      Begin VB.TextBox Text2 
         Height          =   315
         Left            =   480
         TabIndex        =   8
         Text            =   "Text2"
         Top             =   600
         Width           =   1200
      End
      Begin VB.CommandButton Command2 
         Caption         =   "Command2"
         Height          =   375
         Left            =   -74670
         TabIndex        =   9
         Top             =   2000
         Width           =   1200
      End
      Begin VB.Label Label2 
         Caption         =   "Label2"
         Height          =   255
         Left            =   480
         TabIndex        =   6
         Top             =   1400
         Width           =   1200
      End
   End
   Begin TabDlg.SSTab SSTab2 
      Height          =   2000
      Left            =   120
      TabIndex        =   10
      Top             =   4800
      Width           =   4000
      _ExtentX        =   7064
      _ExtentY        =   3519
      _Version        =   393216
      TabHeight       =   520
      TabCaption(0)   =   "Tab 0"
      Tab(0).ControlEnabled=   -1  'True
      Tab(0).ControlCount=   0
      TabCaption(1)   =   "Tab 1"
      Tab(1).ControlEnabled=   -1  'True
      Tab(1).ControlCount=   0
      TabCaption(2)   =   "Tab 2"
      Tab(2).ControlEnabled=   -1  'True
      Tab(2).ControlCount=   0
   End
   Begin TabDlg.SSTab SSTab1 
      Height          =   4000
      Left            =   120
      TabIndex        =   0
      Top             =   120
      Width           =   4400
      _ExtentX        =   7752
      _ExtentY        =   7064
      _Version        =   393216
      TabOrientation  =   1
      Style           =   1
      MousePointer    =   2
      Tabs            =   4
      Tab             =   2
      TabsPerRow      =   2
      TabHeight       =   900
      TabMaxWidth     =   1500
      WordWrap        =   0   'False
      ShowFocusRect   =   0   'False
      BackColor       =   12648447
      ForeColor       =   16711680
      OLEDropMode     =   1
      BeginProperty Font {0BE35203-8F91-11CE-9DE3-00AA004BB851} 
         Name            =   "Arial"
         Size            =   9.75
         Charset         =   0
         Weight          =   700
         Underline       =   0   'False
         Italic          =   0   'False
         Strikethrough   =   0   'False
      EndProperty
      TabCaption(0)   =   "Alpha"
      Tab(0).ControlEnabled=   0   'False
      Tab(0).Control(0)=   "Command1"
      Tab(0).Control(0).Enabled=   0   'False
      Tab(0).ControlCount=   1
      TabCaption(1)   =   "Beta"
      Tab(1).ControlEnabled=   0   'False
      Tab(1).ControlCount=   0
      TabCaption(2)   =   "Gamma"
      TabPicture(2)   =   "Form1.frx":001C
      Tab(2).ControlEnabled=   -1  'True
      Tab(2).Control(0)=   "Text1"
      Tab(2).Control(0).Enabled=   0   'False
      Tab(2).ControlCount=   1
      TabCaption(3)   =   "Delta"
      Tab(3).ControlEnabled=   0   'False
      Tab(3).Control(0)=   "Check1"
      Tab(3).Control(0).Enabled=   0   'False
      Tab(3).ControlCount=   1
      Begin VB.CheckBox Check1 
         Caption         =   "Check1"
         Height          =   315
         Left            =   -74400
         TabIndex        =   3
         Top             =   1500
         Width           =   1200
      End
      Begin VB.TextBox Text1 
         Height          =   315
         Left            =   480
         TabIndex        =   2
         Text            =   "Text1"
         Top             =   900
         Width           =   1200
      End
      Begin VB.CommandButton Command1 
         Caption         =   "Command1"
         Height          =   375
         Left            =   -74550
         TabIndex        =   1
         Top             =   600
         Width           =   1200
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
