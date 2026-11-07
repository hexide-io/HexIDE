VERSION 5.00
Object = "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCTL.OCX"
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
   Begin MSComctlLib.Slider Slider5 
      Height          =   675
      Left            =   4050
      TabIndex        =   4
      Top             =   3255
      Width           =   1500
      _ExtentX        =   2646
      _ExtentY        =   1191
      _Version        =   393216
   End
   Begin MSComctlLib.Slider Slider4 
      Height          =   675
      Left            =   120
      TabIndex        =   3
      Top             =   2700
      Width           =   4500
      _ExtentX        =   7938
      _ExtentY        =   1191
      _Version        =   393216
      Value           =   6
   End
   Begin MSComctlLib.Slider Slider3 
      Height          =   3000
      Left            =   6000
      TabIndex        =   2
      Top             =   120
      Width           =   675
      _ExtentX        =   1191
      _ExtentY        =   5292
      _Version        =   393216
      Orientation     =   1
      Value           =   4
   End
   Begin MSComctlLib.Slider Slider2 
      Height          =   675
      Left            =   120
      TabIndex        =   1
      Top             =   1500
      Width           =   4500
      _ExtentX        =   7938
      _ExtentY        =   1191
      _Version        =   393216
   End
   Begin MSComctlLib.Slider Slider1 
      Height          =   675
      Left            =   120
      TabIndex        =   0
      Top             =   120
      Width           =   4500
      _ExtentX        =   7938
      _ExtentY        =   1191
      _Version        =   393216
      BorderStyle     =   1
      MousePointer    =   2
      Enabled         =   0   'False
      OLEDropMode     =   1
      LargeChange     =   10
      SmallChange     =   2
      Min             =   -5
      Max             =   50
      SelectRange     =   -1  'True
      SelStart        =   3
      SelLength       =   12
      TickStyle       =   2
      TickFrequency   =   5
      Value           =   30
      TextPosition    =   1
   End
End
Attribute VB_Name = "Form1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' saved
