VERSION 5.00
Object = "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCTL.OCX"
Begin VB.Form Form1 
   Caption         =   "look"
   ClientHeight    =   4245
   ClientLeft      =   60
   ClientTop       =   345
   ClientWidth     =   4860
   LinkTopic       =   "Form1"
   ScaleHeight     =   4245
   ScaleWidth      =   4860
   StartUpPosition =   0  'Manual
   Begin MSComctlLib.ProgressBar pbQ 
      Height          =   300
      Left            =   120
      TabIndex        =   0
      Top             =   120
      Width           =   3000
      _ExtentX        =   5292
      _ExtentY        =   529
      _Version        =   393216
      Appearance      =   1
      Scrolling       =   1
   End
   Begin MSComctlLib.ProgressBar pbThird 
      Height          =   300
      Left            =   120
      TabIndex        =   1
      Top             =   540
      Width           =   3000
      _ExtentX        =   5292
      _ExtentY        =   529
      _Version        =   393216
      Appearance      =   1
      Scrolling       =   1
      Max             =   3
   End
   Begin MSComctlLib.ProgressBar pbFrac 
      Height          =   300
      Left            =   120
      TabIndex        =   2
      Top             =   960
      Width           =   3000
      _ExtentX        =   5292
      _ExtentY        =   529
      _Version        =   393216
      Appearance      =   1
      Scrolling       =   1
      Min             =   0.5
      Max             =   10.5
   End
   Begin MSComctlLib.ProgressBar pbBig 
      Height          =   300
      Left            =   120
      TabIndex        =   3
      Top             =   1380
      Width           =   3000
      _ExtentX        =   5292
      _ExtentY        =   529
      _Version        =   393216
      Appearance      =   1
      Scrolling       =   1
      Max             =   40000
   End
   Begin MSComctlLib.ProgressBar pbFracV 
      Height          =   300
      Left            =   120
      TabIndex        =   4
      Top             =   1800
      Width           =   3000
      _ExtentX        =   5292
      _ExtentY        =   529
      _Version        =   393216
      Appearance      =   1
      Scrolling       =   1
      Min             =   0
      Max             =   1
   End
   Begin MSComctlLib.ProgressBar pbChunkQ 
      Height          =   300
      Left            =   120
      TabIndex        =   5
      Top             =   2220
      Width           =   3000
      _ExtentX        =   5292
      _ExtentY        =   529
      _Version        =   393216
      Appearance      =   1
   End
   Begin MSComctlLib.ProgressBar pbVFull 
      Height          =   2400
      Left            =   3360
      TabIndex        =   6
      Top             =   120
      Width           =   300
      _ExtentX        =   529
      _ExtentY        =   4233
      _Version        =   393216
      Appearance      =   1
      Orientation     =   1
   End
   Begin MSComctlLib.ProgressBar pbVQ 
      Height          =   2400
      Left            =   3840
      TabIndex        =   7
      Top             =   120
      Width           =   300
      _ExtentX        =   529
      _ExtentY        =   4233
      _Version        =   393216
      Appearance      =   1
      Orientation     =   1
      Scrolling       =   1
   End
End
Attribute VB_Name = "Form1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Private Sub Form_Load()
    pbQ.Value = 25: pbThird.Value = 1: pbFrac.Value = 5.5: pbBig.Value = 10000: pbFracV.Value = 0.3
    pbChunkQ.Value = 25: pbVFull.Value = 100: pbVQ.Value = 25
End Sub
