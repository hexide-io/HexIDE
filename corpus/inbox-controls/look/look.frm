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
   Begin MSComctlLib.ProgressBar pb0 
      Height          =   300
      Left            =   120
      TabIndex        =   0
      Top             =   120
      Width           =   3000
      _ExtentX        =   5292
      _ExtentY        =   529
      _Version        =   393216
      Appearance      =   1
   End
   Begin MSComctlLib.ProgressBar pb50 
      Height          =   300
      Left            =   120
      TabIndex        =   1
      Top             =   540
      Width           =   3000
      _ExtentX        =   5292
      _ExtentY        =   529
      _Version        =   393216
      Appearance      =   1
   End
   Begin MSComctlLib.ProgressBar pbSmooth 
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
   End
   Begin MSComctlLib.ProgressBar pbFlat 
      Height          =   300
      Left            =   120
      TabIndex        =   3
      Top             =   1380
      Width           =   3000
      _ExtentX        =   5292
      _ExtentY        =   529
      _Version        =   393216
      Appearance      =   0
   End
   Begin MSComctlLib.ProgressBar pbBorder 
      Height          =   300
      Left            =   120
      TabIndex        =   4
      Top             =   1800
      Width           =   3000
      _ExtentX        =   5292
      _ExtentY        =   529
      _Version        =   393216
      BorderStyle     =   1
      Appearance      =   1
   End
   Begin MSComctlLib.ProgressBar pbFlatBorder 
      Height          =   300
      Left            =   120
      TabIndex        =   5
      Top             =   2220
      Width           =   3000
      _ExtentX        =   5292
      _ExtentY        =   529
      _Version        =   393216
      BorderStyle     =   1
      Appearance      =   0
   End
   Begin MSComctlLib.ProgressBar pbOff 
      Height          =   300
      Left            =   120
      TabIndex        =   6
      Top             =   2640
      Width           =   3000
      _ExtentX        =   5292
      _ExtentY        =   529
      _Version        =   393216
      Appearance      =   1
      Enabled         =   0
   End
   Begin MSComctlLib.ProgressBar pbFull 
      Height          =   300
      Left            =   120
      TabIndex        =   7
      Top             =   3060
      Width           =   3000
      _ExtentX        =   5292
      _ExtentY        =   529
      _Version        =   393216
      Appearance      =   1
   End
   Begin MSComctlLib.ProgressBar pbTall 
      Height          =   600
      Left            =   120
      TabIndex        =   8
      Top             =   3480
      Width           =   3000
      _ExtentX        =   5292
      _ExtentY        =   1058
      _Version        =   393216
      Appearance      =   1
   End
   Begin MSComctlLib.ProgressBar pbV 
      Height          =   2400
      Left            =   3360
      TabIndex        =   9
      Top             =   120
      Width           =   300
      _ExtentX        =   529
      _ExtentY        =   4233
      _Version        =   393216
      Appearance      =   1
      Orientation     =   1
   End
   Begin MSComctlLib.ProgressBar pbVSmooth 
      Height          =   2400
      Left            =   3840
      TabIndex        =   10
      Top             =   120
      Width           =   300
      _ExtentX        =   529
      _ExtentY        =   4233
      _Version        =   393216
      Appearance      =   1
      Orientation     =   1
      Scrolling       =   1
   End
   Begin MSComctlLib.ProgressBar pbV25 
      Height          =   2400
      Left            =   4320
      TabIndex        =   11
      Top             =   120
      Width           =   300
      _ExtentX        =   529
      _ExtentY        =   4233
      _Version        =   393216
      Appearance      =   1
      Orientation     =   1
   End
End
Attribute VB_Name = "Form1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Private Sub Form_Load()
    pb50.Value = 50: pbSmooth.Value = 50: pbFlat.Value = 50: pbBorder.Value = 50: pbFlatBorder.Value = 50
    pbOff.Value = 50: pbFull.Value = 100: pbTall.Value = 50: pbV.Value = 50: pbVSmooth.Value = 50: pbV25.Value = 25
End Sub
