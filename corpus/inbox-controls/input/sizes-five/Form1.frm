VERSION 5.00
Object = "{6B7E6392-850A-101B-AFC0-4210102A8DA7}#1.3#0"; "COMCTL32.OCX"
Begin VB.Form Form1 
   Caption         =   "Form1"
   ClientHeight    =   3015
   ClientLeft      =   60
   ClientTop       =   345
   ClientWidth     =   4560
   LinkTopic       =   "Form1"
   ScaleHeight     =   3015
   ScaleWidth      =   4560
   StartUpPosition =   3  'Windows Default
   Begin ComctlLib.ProgressBar pbAll 
      Height          =   375
      Left            =   120
      Top             =   120
      Width           =   2655
      _ExtentX        =   4683
      _ExtentY        =   661
      _Version        =   327682
      OLEDropMode     =   1
      Enabled         =   0
      MousePointer    =   11
      Max             =   50
      Min             =   10
      Appearance      =   0
      BorderStyle     =   1
   End
   Begin ComctlLib.ProgressBar pbKeep 
      Height          =   375
      Left            =   120
      Top             =   720
      Width           =   2655
      _ExtentX        =   4683
      _ExtentY        =   661
      _Version        =   327682
      Appearance      =   1
   End
End
Attribute VB_Name = "Form1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
