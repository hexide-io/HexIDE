VERSION 5.00
Begin VB.Form frmBillOfFare
   Caption         =   "Bill of Fare — a tour of VB6 menus"
   ClientHeight    =   2760
   ClientLeft      =   120
   ClientTop       =   420
   ClientWidth     =   6240
   Begin VB.TextBox txtScratch
      Height          =   375
      Left            =   240
      Text            =   "type here, then press Ctrl+S"
      Top             =   1080
      Width           =   3255
   End
   Begin VB.Label lblChosen
      Caption         =   "nothing chosen yet"
      Height          =   375
      Left            =   240
      Top             =   1800
      Width           =   5775
   End
   Begin VB.Label lblPrompt
      Caption         =   "Open the File menu, or press Alt+F. Every item below reports itself here."
      Height          =   375
      Left            =   240
      Top             =   360
      Width           =   5775
   End
   Begin VB.Menu mnuFile
      Caption         =   "&File"
      Begin VB.Menu mnuFileNew
         Caption         =   "&New"
         Shortcut        =   ^N
      End
      Begin VB.Menu mnuFileOpen
         Caption         =   "&Open..."
         Shortcut        =   ^O
      End
      Begin VB.Menu mnuFileBar1
         Caption         =   "-"
      End
      Begin VB.Menu mnuFileSave
         Caption         =   "&Save"
         Shortcut        =   ^S
      End
      Begin VB.Menu mnuFileNotYet
         Caption         =   "Save &As..."
         Enabled         =   0   'False
      End
      Begin VB.Menu mnuFileBar2
         Caption         =   "-"
      End
      Begin VB.Menu mnuFileExit
         Caption         =   "E&xit"
      End
   End
   Begin VB.Menu mnuView
      Caption         =   "&View"
      Begin VB.Menu mnuViewZoom
         Caption         =   "&Zoom"
         Begin VB.Menu mnuViewZoomIn
            Caption         =   "Zoom &In"
         End
         Begin VB.Menu mnuViewZoomOut
            Caption         =   "Zoom &Out"
         End
      End
      Begin VB.Menu mnuViewBar1
         Caption         =   "-"
      End
      Begin VB.Menu mnuViewRefresh
         Caption         =   "&Refresh"
         Shortcut        =   {F5}
      End
   End
   Begin VB.Menu mnuHelp
      Caption         =   "&Help"
      Begin VB.Menu mnuHelpAbout
         Caption         =   "&About Bill of Fare"
         Shortcut        =   {F1}
      End
   End
End
Attribute VB_Name = "frmBillOfFare"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' Every handler does the same thing: say which item ran it. The point of the demo is that the menu
' works at all — that the bar is populated, that submenus nest, that a separator is a rule rather
' than a gap, that a disabled item cannot be chosen, and that a shortcut fires from wherever you are.

Private Sub Form_Load()
    lblChosen.Caption = "nothing chosen yet"
End Sub

Private Sub mnuFileNew_Click()
    Report "File > New"
End Sub

Private Sub mnuFileOpen_Click()
    Report "File > Open..."
End Sub

Private Sub mnuFileSave_Click()
    Report "File > Save  (try it with Ctrl+S while the caret is in the text box)"
End Sub

Private Sub mnuFileNotYet_Click()
    ' Unreachable: the item is disabled. Here so that a regression which re-enables it is loud.
    Report "Save As... should not have been reachable"
End Sub

Private Sub mnuFileExit_Click()
    Unload Me
End Sub

Private Sub mnuViewZoomIn_Click()
    Report "View > Zoom > Zoom In  (a submenu, two levels deep)"
End Sub

Private Sub mnuViewZoomOut_Click()
    Report "View > Zoom > Zoom Out"
End Sub

Private Sub mnuViewRefresh_Click()
    Report "View > Refresh  (F5, a shortcut with no modifier)"
End Sub

Private Sub mnuHelpAbout_Click()
    Report "Help > About  (F1)"
End Sub

Private Sub Report(ByVal Chosen As String)
    lblChosen.Caption = "you chose: " & Chosen
End Sub
