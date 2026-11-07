VERSION 5.00
Object = "{86CF1D34-0C5F-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCT2.OCX"
Begin VB.Form Form1 
   Caption         =   "Form1"
   ClientHeight    =   7800
   ClientLeft      =   60
   ClientTop       =   345
   ClientWidth     =   9000
   LinkTopic       =   "Form1"
   ScaleHeight     =   7800
   ScaleWidth      =   9000
   StartUpPosition =   3  'Windows Default
   Begin MSComCtl2.MonthView MonthView9 
      Height          =   2370
      Left            =   3150
      TabIndex        =   8
      Top             =   2715
      Width           =   2700
      _ExtentX        =   4763
      _ExtentY        =   4180
      _Version        =   393216
      Appearance      =   1
      StartOfWeek     =   110493698
      CurrentDate     =   46322
   End
   Begin MSComCtl2.MonthView MonthView1 
      Height          =   2370
      Left            =   120
      TabIndex        =   0
      Top             =   120
      Width           =   2700
      _ExtentX        =   4763
      _ExtentY        =   4180
      _Version        =   393216
      Appearance      =   1
      StartOfWeek     =   110493697
      CurrentDate     =   36494
   End
   Begin MSComCtl2.MonthView MonthView2 
      Height          =   2370
      Left            =   3020
      TabIndex        =   1
      Top             =   120
      Width           =   2700
      _ExtentX        =   4763
      _ExtentY        =   4180
      _Version        =   393216
      Appearance      =   1
      StartOfWeek     =   110493697
      CurrentDate     =   36494
      MaxDate         =   47848
   End
   Begin MSComCtl2.MonthView MonthView3 
      Height          =   2370
      Left            =   5920
      TabIndex        =   2
      Top             =   120
      Width           =   2700
      _ExtentX        =   4763
      _ExtentY        =   4180
      _Version        =   393216
      Appearance      =   1
      MaxSelCount     =   14
      MultiSelect     =   -1  'True
      StartOfWeek     =   110493698
      CurrentDate     =   37057
      MaxDate         =   47848
      MinDate         =   36526
   End
   Begin MSComCtl2.MonthView MonthView4 
      Height          =   2310
      Left            =   120
      TabIndex        =   3
      Top             =   2625
      Width           =   3045
      _ExtentX        =   5371
      _ExtentY        =   4075
      _Version        =   393216
      BorderStyle     =   1
      Appearance      =   0
      MousePointer    =   11
      Enabled         =   0   'False
      OLEDropMode     =   1
      ScrollRate      =   3
      ShowToday       =   0   'False
      ShowWeekNumbers =   -1  'True
      StartOfWeek     =   110493697
      CurrentDate     =   36494
   End
   Begin MSComCtl2.MonthView MonthView5 
      Height          =   4515
      Left            =   3015
      TabIndex        =   4
      Top             =   2625
      Width           =   5430
      _ExtentX        =   9578
      _ExtentY        =   7964
      _Version        =   393216
      Appearance      =   1
      MonthColumns    =   2
      MonthRows       =   2
      StartOfWeek     =   110493697
      CurrentDate     =   36494
   End
   Begin MSComCtl2.MonthView MonthView6 
      Height          =   2370
      Left            =   5920
      TabIndex        =   5
      Top             =   2620
      Width           =   2700
      _ExtentX        =   4763
      _ExtentY        =   4180
      _Version        =   393216
      ForeColor       =   65280
      BackColor       =   255
      Appearance      =   1
      MonthBackColor  =   16711680
      StartOfWeek     =   110493697
      TitleBackColor  =   65535
      TitleForeColor  =   16711935
      TrailingForeColor=   8421504
      CurrentDate     =   36494
   End
   Begin MSComCtl2.MonthView MonthView7 
      Height          =   2820
      Left            =   120
      TabIndex        =   6
      Top             =   5115
      Width           =   3330
      _ExtentX        =   5874
      _ExtentY        =   4974
      _Version        =   393216
      Appearance      =   1
      BeginProperty Font {0BE35203-8F91-11CE-9DE3-00AA004BB851} 
         Name            =   "Arial"
         Size            =   9.75
         Charset         =   0
         Weight          =   700
         Underline       =   0   'False
         Italic          =   0   'False
         Strikethrough   =   0   'False
      EndProperty
      StartOfWeek     =   110493697
      CurrentDate     =   36494
   End
   Begin MSComCtl2.MonthView MonthView8 
      Height          =   2370
      Left            =   3020
      TabIndex        =   7
      Top             =   5120
      Width           =   2700
      _ExtentX        =   4763
      _ExtentY        =   4180
      _Version        =   393216
      Appearance      =   1
      StartOfWeek     =   110493697
      CurrentDate     =   36494
   End
End
Attribute VB_Name = "Form1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' saved
