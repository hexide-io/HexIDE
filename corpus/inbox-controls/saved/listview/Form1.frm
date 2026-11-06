VERSION 5.00
Object = "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCTL.OCX"
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
   Begin MSComctlLib.ListView ListView1 
      Height          =   2500
      Left            =   120
      TabIndex        =   0
      Top             =   120
      Width           =   5000
      _ExtentX        =   8811
      _ExtentY        =   4419
      SortKey         =   1
      View            =   3
      Arrange         =   2
      LabelEdit       =   1
      SortOrder       =   -1  'True
      Sorted          =   -1  'True
      MultiSelect     =   -1  'True
      LabelWrap       =   0   'False
      HideSelection   =   0   'False
      HideColumnHeaders=   -1  'True
      OLEDragMode     =   1
      OLEDropMode     =   1
      AllowReorder    =   -1  'True
      Checkboxes      =   -1  'True
      FlatScrollBar   =   -1  'True
      FullRowSelect   =   -1  'True
      GridLines       =   -1  'True
      HotTracking     =   -1  'True
      HoverSelection  =   -1  'True
      PictureAlignment=   5
      TextBackground  =   -1  'True
      _Version        =   393217
      Icons           =   "ImageList1"
      SmallIcons      =   "ImageList1"
      ColHdrIcons     =   "ImageList1"
      ForeColor       =   255
      BackColor       =   12648447
      BorderStyle     =   1
      Appearance      =   0
      MousePointer    =   2
      Enabled         =   0   'False
      BeginProperty Font {0BE35203-8F91-11CE-9DE3-00AA004BB851} 
         Name            =   "Arial"
         Size            =   9.75
         Charset         =   0
         Weight          =   700
         Underline       =   0   'False
         Italic          =   0   'False
         Strikethrough   =   0   'False
      EndProperty
      OLEDragMode     =   1
      OLEDropMode     =   1
      NumItems        =   2
      BeginProperty ColumnHeader(1) {BDD1F052-858B-11D1-B16A-00C0F0283628} 
         Key             =   "name"
         Object.Tag             =   "first"
         Text            =   "Name"
         Object.Width           =   2000
         ImageIndex      =   1
      EndProperty
      BeginProperty ColumnHeader(2) {BDD1F052-858B-11D1-B16A-00C0F0283628} 
         Alignment       =   1
         SubItemIndex    =   1
         Key             =   "size"
         Object.Tag             =   "second"
         Text            =   "Size"
         Object.Width           =   1000
         ImageIndex      =   3
      EndProperty
   End
   Begin MSComctlLib.ListView ListView2 
      Height          =   1200
      Left            =   120
      TabIndex        =   1
      Top             =   2760
      Width           =   2800
      _ExtentX        =   4948
      _ExtentY        =   2117
      LabelWrap       =   -1  'True
      HideSelection   =   -1  'True
      _Version        =   393217
      ForeColor       =   -2147483630
      Appearance      =   1
      NumItems        =   0
   End
   Begin MSComctlLib.ListView ListView3 
      Height          =   1200
      Left            =   3060
      TabIndex        =   2
      Top             =   2760
      Width           =   2800
      _ExtentX        =   4948
      _ExtentY        =   2117
      LabelWrap       =   -1  'True
      HideSelection   =   -1  'True
      _Version        =   393217
      ForeColor       =   -2147483640
      BackColor       =   -2147483643
      BorderStyle     =   1
      Appearance      =   0
      NumItems        =   0
   End
   Begin MSComctlLib.ImageList ImageList1 
      Left            =   2715
      Top             =   1965
      _ExtentX        =   1005
      _ExtentY        =   1005
      BackColor       =   -2147483643
      ImageWidth      =   16
      ImageHeight     =   16
      MaskColor       =   12632256
      _Version        =   393216
      BeginProperty Images {2C247F25-8591-11D1-B16A-00C0F0283628} 
         NumListImages   =   3
         BeginProperty ListImage1 {2C247F27-8591-11D1-B16A-00C0F0283628} 
            Picture         =   "Form1.frx":0000
            Key             =   "red"
            Object.Tag             =   "t1"
         EndProperty
         BeginProperty ListImage2 {2C247F27-8591-11D1-B16A-00C0F0283628} 
            Picture         =   "Form1.frx":0352
            Key             =   "blue"
         EndProperty
         BeginProperty ListImage3 {2C247F27-8591-11D1-B16A-00C0F0283628} 
            Picture         =   "Form1.frx":06A4
            Key             =   "ico"
            Object.Tag             =   "t3"
         EndProperty
      EndProperty
   End
End
Attribute VB_Name = "Form1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' saved
