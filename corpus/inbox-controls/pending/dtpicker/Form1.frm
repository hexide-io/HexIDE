VERSION 5.00
Object = "{86CF1D34-0C5F-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCT2.OCX"
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
   Begin MSComCtl2.DTPicker DTPicker1 
      Height          =   375
      Left            =   3000
      TabIndex        =   0
      Top             =   120
      Width           =   2400
      _ExtentX        =   4233
      _ExtentY        =   661
      _Version        =   393216
      Format          =   110100481
      CurrentDate     =   36494
   End
   Begin MSComCtl2.DTPicker DTPicker2 
      Height          =   375
      Left            =   3000
      TabIndex        =   1
      Top             =   720
      Width           =   2400
      _ExtentX        =   4233
      _ExtentY        =   661
      _Version        =   393216
      Format          =   110100481
      CurrentDate     =   36494
      MaxDate         =   47848
   End
   Begin MSComCtl2.DTPicker DTPicker3 
      Height          =   375
      Left            =   3000
      TabIndex        =   2
      Top             =   1320
      Width           =   2400
      _ExtentX        =   4233
      _ExtentY        =   661
      _Version        =   393216
      CustomFormat    =   "dd MMM yyyy"
      Format          =   110100483
      CurrentDate     =   40000.25
      MaxDate         =   47848
      MinDate         =   36526
   End
   Begin MSComCtl2.DTPicker DTPicker4 
      Height          =   375
      Left            =   3000
      TabIndex        =   3
      Top             =   1920
      Width           =   2400
      _ExtentX        =   4233
      _ExtentY        =   661
      _Version        =   393216
      MousePointer    =   11
      Enabled         =   0   'False
      OLEDropMode     =   1
      CalendarBackColor=   255
      CalendarForeColor=   65280
      CalendarTitleBackColor=   16711680
      CalendarTitleForeColor=   65535
      CalendarTrailingForeColor=   8421504
      CheckBox        =   -1  'True
      Format          =   110100481
      UpDown          =   -1  'True
      CurrentDate     =   36494
   End
   Begin MSComCtl2.DTPicker DTPicker5 
      Height          =   375
      Left            =   3000
      TabIndex        =   4
      Top             =   2520
      Width           =   2400
      _ExtentX        =   4233
      _ExtentY        =   661
      _Version        =   393216
      Format          =   110100481
      CurrentDate     =   37000
   End
   Begin MSComCtl2.DTPicker DTPicker6 
      Height          =   375
      Left            =   3000
      TabIndex        =   5
      Top             =   3120
      Width           =   2400
      _ExtentX        =   4233
      _ExtentY        =   661
      _Version        =   393216
      Format          =   110100480
      CurrentDate     =   36494
   End
   Begin MSComCtl2.DTPicker DTPicker7 
      Height          =   375
      Left            =   3000
      TabIndex        =   6
      Top             =   3720
      Width           =   2400
      _ExtentX        =   4233
      _ExtentY        =   661
      _Version        =   393216
      BeginProperty Font {0BE35203-8F91-11CE-9DE3-00AA004BB851} 
         Name            =   "Arial"
         Size            =   12
         Charset         =   0
         Weight          =   700
         Underline       =   0   'False
         Italic          =   -1  'True
         Strikethrough   =   0   'False
      EndProperty
      Format          =   110100481
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
