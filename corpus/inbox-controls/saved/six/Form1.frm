VERSION 5.00
Object = "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCTL.OCX"
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
   Begin MSComctlLib.ProgressBar pbDef 
      Height          =   375
      Left            =   120
      TabIndex        =   0
      Top             =   120
      Width           =   2655
      _ExtentX        =   2646
      _ExtentY        =   1323
      _Version        =   393216
      Appearance      =   1
   End
   Begin MSComctlLib.ProgressBar pbOdd 
      Height          =   211
      Left            =   120
      TabIndex        =   1
      Top             =   600
      Width           =   1234
      _ExtentX        =   2646
      _ExtentY        =   1323
      _Version        =   393216
      Appearance      =   1
   End
   Begin MSComctlLib.ProgressBar pbRange 
      Height          =   375
      Left            =   120
      TabIndex        =   2
      Top             =   960
      Width           =   2655
      _ExtentX        =   2646
      _ExtentY        =   1323
      _Version        =   393216
      Appearance      =   1
      Min             =   10
      Max             =   50
   End
   Begin MSComctlLib.ProgressBar pbFrac 
      Height          =   375
      Left            =   120
      TabIndex        =   3
      Top             =   1440
      Width           =   2655
      _ExtentX        =   2646
      _ExtentY        =   1323
      _Version        =   393216
      Appearance      =   1
      Min             =   0.5
      Max             =   10.5
   End
   Begin MSComctlLib.ProgressBar pbLook 
      Height          =   375
      Left            =   120
      TabIndex        =   4
      Top             =   1920
      Width           =   2655
      _ExtentX        =   2646
      _ExtentY        =   1323
      _Version        =   393216
      BorderStyle     =   1
      Appearance      =   0
   End
   Begin MSComctlLib.ProgressBar pbMisc 
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
      _ExtentX        =   2646
      _ExtentY        =   1323
      _Version        =   393216
      Appearance      =   1
      MousePointer    =   11
      Enabled         =   0   'False
      OLEDropMode     =   1
   End
   Begin MSComctlLib.ProgressBar pbBig 
      Height          =   375
      Left            =   120
      TabIndex        =   6
      Top             =   2880
      Width           =   2655
      _ExtentX        =   2646
      _ExtentY        =   1323
      _Version        =   393216
      Appearance      =   1
      Max             =   40000
   End
   Begin MSComctlLib.ProgressBar pbOrient 
      Height          =   1500
      Left            =   3000
      TabIndex        =   7
      Top             =   120
      Width           =   375
      _ExtentX        =   2646
      _ExtentY        =   1323
      _Version        =   393216
      Appearance      =   1
      Orientation     =   1
      Scrolling       =   1
   End
   Begin MSComctlLib.ProgressBar pbAlign 
      Align           =   2  'Align Bottom
      Height          =   750
      Left            =   0
      TabIndex        =   8
      Top             =   2850
      Width           =   4560
      _ExtentX        =   8043
      _ExtentY        =   1323
      _Version        =   393216
      Appearance      =   1
   End
End
Attribute VB_Name = "Form1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
