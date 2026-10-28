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
      Left            =   240
      TabIndex        =   0
      Top             =   120
      _Version        =   393216
   End
   Begin MSMask.MaskEdBox m2
      BeginProperty Font {0BE35203-8F91-11CE-9DE3-00AA004BB851} 
         Name            =   "Arial"
         Size            =   12
         Charset         =   0
         Weight          =   700
         Underline       =   0   'False
         Italic          =   0   'False
         Strikethrough   =   0   'False
      EndProperty
      Text            =   "12-34"
      PromptChar      =   "*"
      Mask            =   "##-##"
      Format          =   "#,##0.00"
      MaxLength       =   5
      OLEDropMode     =   1
      OLEDragMode     =   1
      MousePointer    =   3
      HideSelection   =   0   'False
      Enabled         =   0   'False
      BorderStyle     =   0
      Appearance      =   0
      ForeColor       =   16711680
      BackColor       =   12648447
      AutoTab         =   -1  'True
      AllowPrompt     =   -1  'True
      PromptInclude   =   0   'False
      ClipMode        =   1
      _Version        =   393216
      _ExtentY        =   661
      _ExtentX        =   4260
      Width           =   2415
      Top             =   720
      TabIndex        =   1
      Left            =   240
      Height          =   375
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
      Text            =   "hello"
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
   End
End
Attribute VB_Name = "Form1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit
