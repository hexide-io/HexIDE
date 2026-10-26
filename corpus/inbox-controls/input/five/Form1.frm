VERSION 5.00
Object = "{6B7E6392-850A-101B-AFC0-4210102A8DA7}#1.3#0"; "COMCTL32.OCX"
Begin VB.Form Form1 
   Caption         =   "Form1"
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
      Top             =   120
      Width           =   2655
   End
   Begin ComctlLib.ProgressBar pbOdd 
      Height          =   211
      Left            =   120
      Top             =   600
      Width           =   1234
   End
   Begin ComctlLib.ProgressBar pbRange 
      Height          =   375
      Left            =   120
      Top             =   960
      Width           =   2655
      Max             =   50
      Min             =   10
      Value           =   20
   End
   Begin ComctlLib.ProgressBar pbFrac 
      Height          =   375
      Left            =   120
      Top             =   1440
      Width           =   2655
      Max             =   10.5
      Min             =   0.5
      Value           =   2.25
   End
   Begin ComctlLib.ProgressBar pbLook 
      Height          =   375
      Left            =   120
      Top             =   1920
      Width           =   2655
      Appearance      =   0
      BorderStyle     =   1
   End
   Begin ComctlLib.ProgressBar pbMisc 
      Height          =   375
      Left            =   120
      Top             =   2400
      Width           =   2655
      Enabled         =   0
      MousePointer    =   11
      OLEDropMode     =   1
      Visible         =   0
      Tag             =   "t"
      ToolTipText     =   "tip"
      DragMode        =   1
      WhatsThisHelpID =   5
   End
   Begin ComctlLib.ProgressBar pbBig 
      Height          =   375
      Left            =   120
      Top             =   2880
      Width           =   2655
      Max             =   40000
      Value           =   30000
   End
   Begin ComctlLib.ProgressBar pbAlign 
      Height          =   375
      Left            =   0
      Top             =   0
      Width           =   0
      Align           =   2
   End
End
Attribute VB_Name = "Form1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
