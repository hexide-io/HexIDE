VERSION 5.00
Object = "{C932BA88-4374-101B-A56C-00AA003668DC}#1.1#0"; "MSMASK32.OCX"
Begin VB.Form Form1 
   Caption         =   "Form1"
   ClientHeight    =   3600
   ClientLeft      =   60
   ClientTop       =   345
   ClientWidth     =   4800
   LinkTopic       =   "Form1"
   ScaleHeight     =   3600
   ScaleWidth      =   4800
   Begin MSMask.MaskEdBox m1 
      Height          =   1125
      Left            =   240
      TabIndex        =   0
      Top             =   120
      Width           =   3000
      _ExtentX        =   5292
      _ExtentY        =   1984
      _Version        =   393216
      PromptChar      =   "_"
   End
   Begin MSMask.MaskEdBox m2 
      Height          =   375
      Left            =   240
      TabIndex        =   1
      Top             =   720
      Width           =   2415
      _ExtentX        =   4260
      _ExtentY        =   661
      _Version        =   393216
      BorderStyle     =   0
      ClipMode        =   1
      MousePointer    =   3
      Appearance      =   0
      BackColor       =   12648447
      ForeColor       =   16711680
      PromptInclude   =   0   'False
      AllowPrompt     =   -1  'True
      AutoTab         =   -1  'True
      HideSelection   =   0   'False
      Enabled         =   0   'False
      MaxLength       =   5
      OLEDragMode     =   1
      OLEDropMode     =   1
      BeginProperty Font {0BE35203-8F91-11CE-9DE3-00AA004BB851} 
         Name            =   "Arial"
         Size            =   12
         Charset         =   0
         Weight          =   700
         Underline       =   0   'False
         Italic          =   0   'False
         Strikethrough   =   0   'False
      EndProperty
      Format          =   "#,##0.00"
      Mask            =   "##-##"
      PromptChar      =   "*"
      OLEDragMode     =   1
      OLEDropMode     =   1
   End
   Begin MSMask.MaskEdBox m3 
      Height          =   375
      Left            =   240
      TabIndex        =   2
      Top             =   1320
      Width           =   2415
      _ExtentX        =   4260
      _ExtentY        =   661
      _Version        =   393216
      MaxLength       =   10
      PromptChar      =   "_"
   End
   Begin MSMask.MaskEdBox m4 
      Height          =   375
      Left            =   240
      TabIndex        =   3
      Top             =   1920
      Width           =   2415
      _ExtentX        =   4260
      _ExtentY        =   661
      _Version        =   393216
      Mask            =   "(###) ###-####"
      PromptChar      =   "_"
   End
End
Attribute VB_Name = "Form1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' saved
