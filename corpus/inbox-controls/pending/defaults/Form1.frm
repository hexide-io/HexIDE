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
   Begin MSComctlLib.ImageCombo s 
      Height          =   330
      Left            =   2250
      TabIndex        =   7
      Top             =   1875
      Width           =   1500
      _ExtentX        =   2646
      _ExtentY        =   582
      _Version        =   393216
      ForeColor       =   -2147483640
      BackColor       =   -2147483643
      Text            =   "ImageCombo1"
   End
   Begin MSComctlLib.Slider Slider1 
      Height          =   675
      Left            =   2250
      TabIndex        =   6
      Top             =   1905
      Width           =   1500
      _ExtentX        =   2646
      _ExtentY        =   1191
      _Version        =   393216
   End
   Begin MSComctlLib.ImageList ImageList1 
      Left            =   2715
      Top             =   1965
      _ExtentX        =   1005
      _ExtentY        =   1005
      BackColor       =   -2147483643
      MaskColor       =   12632256
      _Version        =   393216
   End
   Begin MSComctlLib.ListView ListView1 
      Height          =   750
      Left            =   2250
      TabIndex        =   5
      Top             =   1875
      Width           =   1500
      _ExtentX        =   2646
      _ExtentY        =   1323
      LabelWrap       =   -1  'True
      HideSelection   =   -1  'True
      _Version        =   393217
      ForeColor       =   -2147483640
      BackColor       =   -2147483643
      BorderStyle     =   1
      Appearance      =   1
      NumItems        =   0
   End
   Begin MSComctlLib.TreeView TreeView1 
      Height          =   750
      Left            =   2250
      TabIndex        =   4
      Top             =   1875
      Width           =   1500
      _ExtentX        =   2646
      _ExtentY        =   1323
      _Version        =   393217
      Style           =   7
      Appearance      =   1
   End
   Begin MSComctlLib.ProgressBar ProgressBar1 
      Height          =   750
      Left            =   2250
      TabIndex        =   3
      Top             =   1875
      Width           =   1500
      _ExtentX        =   2646
      _ExtentY        =   1323
      _Version        =   393216
      Appearance      =   1
   End
   Begin MSComctlLib.StatusBar StatusBar1 
      Align           =   2  'Align Bottom
      Height          =   375
      Left            =   0
      TabIndex        =   2
      Top             =   2055
      Width           =   6000
      _ExtentX        =   10583
      _ExtentY        =   661
      _Version        =   393216
      BeginProperty Panels {8E3867A5-8586-11D1-B16A-00C0F0283628} 
         NumPanels       =   1
         BeginProperty Panel1 {8E3867AB-8586-11D1-B16A-00C0F0283628} 
         EndProperty
      EndProperty
   End
   Begin MSComctlLib.Toolbar Toolbar1 
      Align           =   1  'Align Top
      Height          =   660
      Left            =   0
      TabIndex        =   1
      Top             =   0
      Width           =   6000
      _ExtentX        =   10583
      _ExtentY        =   1164
      ButtonWidth     =   609
      ButtonHeight    =   1005
      Appearance      =   1
      _Version        =   393216
   End
   Begin MSComctlLib.TabStrip TabStrip1 
      Height          =   750
      Left            =   2250
      TabIndex        =   0
      Top             =   1875
      Width           =   1500
      _ExtentX        =   2646
      _ExtentY        =   1323
      _Version        =   393216
      BeginProperty Tabs {1EFB6598-857C-11D1-B16A-00C0F0283628} 
         NumTabs         =   1
         BeginProperty Tab1 {1EFB659A-857C-11D1-B16A-00C0F0283628} 
            ImageVarType    =   2
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
