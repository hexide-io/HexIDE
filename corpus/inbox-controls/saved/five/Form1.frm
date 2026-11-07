VERSION 5.00
Object = "{6B7E6392-850A-101B-AFC0-4210102A8DA7}#1.3#0"; "COMCTL32.OCX"
Begin VB.Form Form1 
   Caption         =   "Saved by VB6"
   ClientHeight    =   3600
   ClientLeft      =   60
   ClientTop       =   345
   ClientWidth     =   4560
   LinkTopic       =   "Form1"
   ScaleHeight     =   3600
   ScaleWidth      =   4560
   StartUpPosition =   3  'Windows Default
   Begin ComctlLib.ProgressBar pbDef 
      Height          =   375
      Left            =   120
      TabIndex        =   0
      Top             =   120
      Width           =   2655
      _ExtentX        =   4683
      _ExtentY        =   661
      _Version        =   327682
      Appearance      =   1
   End
   Begin ComctlLib.ProgressBar pbOdd 
      Height          =   211
      Left            =   120
      TabIndex        =   1
      Top             =   600
      Width           =   1234
      _ExtentX        =   2170
      _ExtentY        =   370
      _Version        =   327682
      Appearance      =   1
   End
   Begin ComctlLib.ProgressBar pbRange 
      Height          =   375
      Left            =   120
      TabIndex        =   2
      Top             =   960
      Width           =   2655
      _ExtentX        =   4683
      _ExtentY        =   661
      _Version        =   327682
      Appearance      =   1
      Min             =   10
      Max             =   50
   End
   Begin ComctlLib.ProgressBar pbFrac 
      Height          =   375
      Left            =   120
      TabIndex        =   3
      Top             =   1440
      Width           =   2655
      _ExtentX        =   4683
      _ExtentY        =   661
      _Version        =   327682
      Appearance      =   1
      Min             =   0.5
      Max             =   10.5
   End
   Begin ComctlLib.ProgressBar pbLook 
      Height          =   375
      Left            =   120
      TabIndex        =   4
      Top             =   1920
      Width           =   2655
      _ExtentX        =   4683
      _ExtentY        =   661
      _Version        =   327682
      BorderStyle     =   1
      Appearance      =   0
   End
   Begin ComctlLib.ProgressBar pbMisc 
      DragMode        =   1  'Automatic
      Height          =   375
      Left            =   120
      TabIndex        =   5
      Tag             =   "t"
      ToolTipText     =   "tip"
      Top             =   2400
      Visible         =   0   'False
      WhatsThisHelpID =   5
      Width           =   2655
      _ExtentX        =   4683
      _ExtentY        =   661
      _Version        =   327682
      Appearance      =   1
      MousePointer    =   11
      Enabled         =   0   'False
      OLEDropMode     =   1
   End
   Begin ComctlLib.ProgressBar pbBig 
      Height          =   375
      Left            =   120
      TabIndex        =   6
      Top             =   2880
      Width           =   2655
      _ExtentX        =   4683
      _ExtentY        =   661
      _Version        =   327682
      Appearance      =   1
      Max             =   40000
   End
   Begin ComctlLib.ProgressBar pbAlign 
      Align           =   2  'Align Bottom
      Height          =   750
      Left            =   0
      TabIndex        =   7
      Top             =   2850
      Width           =   4560
      _ExtentX        =   8043
      _ExtentY        =   1323
      _Version        =   327682
      Appearance      =   1
   End
End
Attribute VB_Name = "Form1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
