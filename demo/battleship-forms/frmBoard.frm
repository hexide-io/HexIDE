VERSION 5.00
Begin VB.Form frmBoard 
   BorderStyle     =   1  'Fixed Single
   Caption         =   "Battleship"
   ClientHeight    =   2520
   ClientLeft      =   45
   ClientTop       =   330
   ClientWidth     =   2520
   MaxButton       =   0   'False
   ScaleHeight     =   2520
   ScaleWidth      =   2520
   StartUpPosition =   2  'CenterScreen
   Begin VB.Label lblStatus 
      Height          =   255
      Left            =   120
      TabIndex        =   1
      Top             =   2160
      Width           =   2280
   End
   Begin VB.Label lblCell 
      Alignment       =   2  'Center
      BackColor       =   &H00C08000&
      BorderStyle     =   1  'Fixed Single
      ForeColor       =   &H00FFFFFF&
      Height          =   360
      Index           =   0
      Left            =   120
      TabIndex        =   0
      Top             =   120
      Width           =   360
   End
End
Attribute VB_Name = "frmBoard"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' A cell of the board, the margin round the grid and the status line's height, all in twips.
Private Const CELL As Integer = 360
Private Const MARGIN As Integer = 120
Private Const LINE_HEIGHT As Integer = 255

' The colours of open sea, a miss, a hit, a ship sunk, and a ship nobody found.
Private Const SEA As Long = &HC08000
Private Const SPLASH As Long = &HFFC0C0
Private Const FLAME As Long = vbRed
Private Const WRECK As Long = &H80&
Private Const HULL As Long = &H808080

Private WithEvents mGame As Game
Private mShotsLeft As Integer

Private Sub Form_Load()
    Caption = "Battleship - " & gPlayer
    BuildBoard
    NewGame
End Sub

' The grid: lblCell(0) is on the form, and one more is loaded for each other cell.
Private Sub BuildBoard()
    Dim i As Integer
    For i = 1 To gSize * gSize - 1
        Load lblCell(i)
    Next i
    For i = 0 To gSize * gSize - 1
        lblCell(i).Move MARGIN + (i Mod gSize) * CELL, MARGIN + (i \ gSize) * CELL, CELL, CELL
        lblCell(i).Visible = True
    Next i
    lblStatus.Move MARGIN, MARGIN * 2 + gSize * CELL, gSize * CELL, LINE_HEIGHT
    ' The form fits the grid. What Width has and ScaleWidth doesn't is the border.
    Width = (Width - ScaleWidth) + MARGIN * 2 + gSize * CELL
    Height = (Height - ScaleHeight) + MARGIN * 3 + gSize * CELL + LINE_HEIGHT
End Sub

Private Sub NewGame()
    Dim i As Integer
    Set mGame = New Game
    mGame.Init gSize
    mGame.PlaceFleet
    mShotsLeft = gSize * gSize \ 2
    For i = 0 To gSize * gSize - 1
        lblCell(i).BackColor = SEA
        lblCell(i).Caption = ""
    Next i
    ShowStatus
End Sub

' Short enough for the 5 x 5 board, the narrowest.
Private Sub ShowStatus()
    lblStatus.Caption = "Shots " & mShotsLeft & ", ships " & mGame.ShipsLeft
End Sub

' A press on a cell fires at it. A cell already fired at costs nothing.
Private Sub lblCell_MouseDown(Index As Integer, Button As Integer, Shift As Integer, X As Single, Y As Single)
    Dim r As Integer, c As Integer
    If Button <> vbLeftButton Then Exit Sub
    r = Index \ gSize
    c = Index Mod gSize
    If mGame.Fired(r, c) Then Exit Sub
    mShotsLeft = mShotsLeft - 1
    mGame.Fire r, c
    ShowStatus
    If mGame.AllSunk Then
        GameOver "You sank the fleet with " & mShotsLeft & " shots to spare, " & gPlayer & "!"
    ElseIf mShotsLeft = 0 Then
        RevealFleet
        GameOver "Out of shots, " & gPlayer & ". The fleet got away."
    End If
End Sub

' The game's verdict on a shot, painted on its cell. A ship sunk is painted whole.
Private Sub mGame_Result(ByVal r As Integer, ByVal c As Integer, ByVal outcome As String)
    Dim i As Integer, id As Integer
    Select Case outcome
    Case "MISS"
        lblCell(r * gSize + c).BackColor = SPLASH
    Case "HIT"
        lblCell(r * gSize + c).BackColor = FLAME
        lblCell(r * gSize + c).Caption = "X"
    Case "SUNK"
        lblCell(r * gSize + c).Caption = "X"
        id = mGame.ShipAt(r, c)
        For i = 0 To gSize * gSize - 1
            If mGame.ShipAt(i \ gSize, i Mod gSize) = id Then lblCell(i).BackColor = WRECK
        Next i
    End Select
End Sub

' When the shots run out, the ships nobody found.
Private Sub RevealFleet()
    Dim i As Integer
    For i = 0 To gSize * gSize - 1
        If mGame.ShipAt(i \ gSize, i Mod gSize) >= 0 Then
            If Not mGame.Fired(i \ gSize, i Mod gSize) Then lblCell(i).BackColor = HULL
        End If
    Next i
End Sub

Private Sub GameOver(ByVal verdict As String)
    If MsgBox(verdict & vbCrLf & vbCrLf & "Play again?", vbYesNo + vbQuestion, "Battleship") = vbYes Then
        NewGame
    Else
        ' The board is the last form, so the run ends with it.
        Unload Me
    End If
End Sub

Private Sub Form_Unload(Cancel As Integer)
    Set mGame = Nothing
End Sub
