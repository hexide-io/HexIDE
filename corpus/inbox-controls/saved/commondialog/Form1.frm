VERSION 5.00
Object = "{F9043C88-F6F2-101A-A3C9-08002B2F49FB}#1.2#0"; "COMDLG32.OCX"
Begin VB.Form Form1 
   Caption         =   "Form1"
   ClientHeight    =   4500
   ClientLeft      =   60
   ClientTop       =   345
   ClientWidth     =   6000
   LinkTopic       =   "Form1"
   ScaleHeight     =   4500
   ScaleWidth      =   6000
   StartUpPosition =   3  'Windows Default
   Begin MSComDlg.CommonDialog CommonDialog4 
      Left            =   2760
      Top             =   2010
      _ExtentX        =   847
      _ExtentY        =   847
      _Version        =   393216
   End
   Begin MSComDlg.CommonDialog CommonDialog1 
      Left            =   600
      Top             =   600
      _ExtentX        =   847
      _ExtentY        =   847
      _Version        =   393216
      CancelError     =   -1  'True
      Color           =   255
      Copies          =   2
      DefaultExt      =   "txt"
      DialogTitle     =   "Pick"
      FileName        =   "a.txt"
      Filter          =   "Text (*.txt)|*.txt|All (*.*)|*.*"
      FilterIndex     =   2
      Flags           =   524800
      FontBold        =   -1  'True
      FontItalic      =   -1  'True
      FontName        =   "Arial"
      FontSize        =   12
      FontStrikeThru  =   -1  'True
      FontUnderLine   =   -1  'True
      FromPage        =   3
      HelpCommand     =   3
      HelpContext     =   5
      HelpFile        =   "x.hlp"
      HelpKey         =   "key"
      InitDir         =   "C:\Temp"
      Max             =   9
      Min             =   2
      MaxFileSize     =   1024
      PrinterDefault  =   0   'False
      ToPage          =   4
      Orientation     =   2
   End
   Begin MSComDlg.CommonDialog CommonDialog2 
      Left            =   1500
      Top             =   600
      _ExtentX        =   847
      _ExtentY        =   847
      _Version        =   393216
   End
   Begin MSComDlg.CommonDialog CommonDialog3 
      Left            =   2400
      Top             =   600
      _ExtentX        =   847
      _ExtentY        =   847
      _Version        =   393216
   End
End
Attribute VB_Name = "Form1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' saveds
