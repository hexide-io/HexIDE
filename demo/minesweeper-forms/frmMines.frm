VERSION 5.00
Begin VB.Form frmMines
   BorderStyle     =   1  'Fixed Single
   Caption         =   "Minesweeper"
   ClientHeight    =   3420
   ClientLeft      =   45
   ClientTop       =   330
   ClientWidth     =   2940
   MaxButton       =   0   'False
   ScaleHeight     =   3420
   ScaleWidth      =   2940
   StartUpPosition =   2  'CenterScreen
   Begin VB.Timer tmrClock
      Enabled         =   0   'False
      Interval        =   1000
      Left            =   0
      Top             =   0
   End
   Begin VB.CommandButton cmdFace
      Caption         =   ":)"
      Height          =   360
      Left            =   1230
      TabIndex        =   0
      Top             =   120
      Width           =   480
   End
   Begin VB.Label lblTime
      Alignment       =   1  'Right Justify
      Caption         =   "000"
      ForeColor       =   &H000000C0&
      Height          =   255
      Left            =   2220
      TabIndex        =   2
      Top             =   180
      Width           =   600
   End
   Begin VB.Label lblMines
      Caption         =   "010"
      ForeColor       =   &H000000C0&
      Height          =   255
      Left            =   120
      TabIndex        =   1
      Top             =   180
      Width           =   600
   End
   Begin VB.Label lblCell
      Alignment       =   2  'Center
      BackColor       =   &H00C0C0C0&
      BorderStyle     =   1  'Fixed Single
      BeginProperty Font
         Name            =   "MS Sans Serif"
         Size            =   8.25
         Charset         =   0
         Weight          =   700
         Underline       =   0   'False
         Italic          =   0   'False
         Strikethrough   =   0   'False
      EndProperty
      Height          =   300
      Index           =   0
      Left            =   120
      TabIndex        =   3
      Top             =   600
      Width           =   300
   End
End
Attribute VB_Name = "frmMines"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' The classic beginner's field: 9 by 9, with 10 mines.
Private Const ROWS As Integer = 9
Private Const COLS As Integer = 9
Private Const MINES As Integer = 10

' A cell, the margin round the field, and where the field starts below the face, all in twips.
Private Const CELL As Integer = 300
Private Const MARGIN As Integer = 120
Private Const FIELD_TOP As Integer = 600

' A cell not yet dug, one dug, and the mine that went off.
Private Const COVERED As Long = &HC0C0C0
Private Const DUG As Long = &HE0E0E0
Private Const BLAST As Long = vbRed

' What each cell holds, and what the player knows of it.
Private mMine(ROWS - 1, COLS - 1) As Boolean
Private mNear(ROWS - 1, COLS - 1) As Integer
Private mDug(ROWS - 1, COLS - 1) As Boolean
Private mFlag(ROWS - 1, COLS - 1) As Boolean

Private mPlaced As Boolean
Private mOver As Boolean
Private mDugCount As Integer
Private mFlags As Integer
Private mSeconds As Integer

Private Sub Form_Load()
    Randomize
    BuildField
    NewGame
End Sub

' lblCell(0) is on the form, and one more is loaded for each other cell.
Private Sub BuildField()
    Dim i As Integer
    For i = 1 To ROWS * COLS - 1
        Load lblCell(i)
    Next i
    For i = 0 To ROWS * COLS - 1
        lblCell(i).Move MARGIN + (i Mod COLS) * CELL, FIELD_TOP + (i \ COLS) * CELL, CELL, CELL
        lblCell(i).Visible = True
    Next i
End Sub

Private Sub NewGame()
    Dim r As Integer, c As Integer
    For r = 0 To ROWS - 1
        For c = 0 To COLS - 1
            mMine(r, c) = False
            mNear(r, c) = 0
            mDug(r, c) = False
            mFlag(r, c) = False
            With lblCell(r * COLS + c)
                .Caption = ""
                .BackColor = COVERED
            End With
        Next c
    Next r
    mPlaced = False
    mOver = False
    mDugCount = 0
    mFlags = 0
    mSeconds = 0
    tmrClock.Enabled = False
    cmdFace.Caption = ":)"
    lblTime.Caption = Format$(mSeconds, "000")
    ShowMinesLeft
End Sub

' The mines go down on the first dig, round the cell dug, so that it is never one.
Private Sub PlaceMines(ByVal safeRow As Integer, ByVal safeCol As Integer)
    Dim placed As Integer, r As Integer, c As Integer, dr As Integer, dc As Integer
    Do While placed < MINES
        r = Int(Rnd * ROWS)
        c = Int(Rnd * COLS)
        If Not mMine(r, c) And Not (r = safeRow And c = safeCol) Then
            mMine(r, c) = True
            placed = placed + 1
        End If
    Loop
    For r = 0 To ROWS - 1
        For c = 0 To COLS - 1
            For dr = -1 To 1
                For dc = -1 To 1
                    If OnField(r + dr, c + dc) Then
                        If mMine(r + dr, c + dc) Then mNear(r, c) = mNear(r, c) + 1
                    End If
                Next dc
            Next dr
        Next c
    Next r
    mPlaced = True
End Sub

Private Function OnField(ByVal r As Integer, ByVal c As Integer) As Boolean
    OnField = r >= 0 And r < ROWS And c >= 0 And c < COLS
End Function

Private Sub lblCell_MouseDown(Index As Integer, Button As Integer, Shift As Integer, X As Single, Y As Single)
    If mOver Then Exit Sub
    If Button = vbRightButton Then
        ToggleFlag Index \ COLS, Index Mod COLS
    ElseIf Button = vbLeftButton Then
        Dig Index \ COLS, Index Mod COLS
    End If
End Sub

Private Sub ToggleFlag(ByVal r As Integer, ByVal c As Integer)
    If mDug(r, c) Then Exit Sub
    mFlag(r, c) = Not mFlag(r, c)
    With lblCell(r * COLS + c)
        If mFlag(r, c) Then
            .Caption = "F"
            .ForeColor = vbRed
            mFlags = mFlags + 1
        Else
            .Caption = ""
            mFlags = mFlags - 1
        End If
    End With
    ShowMinesLeft
End Sub

Private Sub Dig(ByVal r As Integer, ByVal c As Integer)
    If mFlag(r, c) Or mDug(r, c) Then Exit Sub
    If Not mPlaced Then
        PlaceMines r, c
        tmrClock.Enabled = True
    End If
    If mMine(r, c) Then
        Lose r, c
    Else
        Uncover r, c
        If mDugCount = ROWS * COLS - MINES Then Win
    End If
End Sub

' Digs a cell, and every cell round one with no mine near it, and round those: the flood fill.
Private Sub Uncover(ByVal r As Integer, ByVal c As Integer)
    Dim dr As Integer, dc As Integer
    If Not OnField(r, c) Then Exit Sub
    If mDug(r, c) Or mFlag(r, c) Then Exit Sub
    mDug(r, c) = True
    mDugCount = mDugCount + 1
    With lblCell(r * COLS + c)
        .BackColor = DUG
        If mNear(r, c) > 0 Then
            .Caption = CStr(mNear(r, c))
            .ForeColor = NumberColor(mNear(r, c))
        End If
    End With
    If mNear(r, c) = 0 Then
        For dr = -1 To 1
            For dc = -1 To 1
                If dr <> 0 Or dc <> 0 Then Uncover r + dr, c + dc
            Next dc
        Next dr
    End If
End Sub

Private Function NumberColor(ByVal n As Integer) As Long
    Select Case n
        Case 1: NumberColor = vbBlue
        Case 2: NumberColor = RGB(0, 128, 0)
        Case 3: NumberColor = vbRed
        Case 4: NumberColor = RGB(0, 0, 128)
        Case 5: NumberColor = RGB(128, 0, 0)
        Case 6: NumberColor = RGB(0, 128, 128)
        Case 7: NumberColor = vbBlack
        Case Else: NumberColor = RGB(128, 128, 128)
    End Select
End Function

Private Sub Lose(ByVal r As Integer, ByVal c As Integer)
    Dim i As Integer
    mOver = True
    tmrClock.Enabled = False
    cmdFace.Caption = ":("
    For i = 0 To ROWS * COLS - 1
        If mMine(i \ COLS, i Mod COLS) And Not mFlag(i \ COLS, i Mod COLS) Then
            lblCell(i).Caption = "*"
            lblCell(i).ForeColor = vbBlack
        End If
    Next i
    lblCell(r * COLS + c).BackColor = BLAST
    MsgBox "Boom! You hit a mine after " & mSeconds & " seconds.", vbExclamation, "Minesweeper"
End Sub

Private Sub Win()
    Dim i As Integer
    mOver = True
    tmrClock.Enabled = False
    cmdFace.Caption = "B)"
    ' Every cell left covered is a mine, so it is flagged for you.
    For i = 0 To ROWS * COLS - 1
        If Not mDug(i \ COLS, i Mod COLS) Then
            lblCell(i).Caption = "F"
            lblCell(i).ForeColor = vbRed
        End If
    Next i
    mFlags = MINES
    ShowMinesLeft
    MsgBox "You cleared the field in " & mSeconds & " seconds.", vbInformation, "Minesweeper"
End Sub

Private Sub ShowMinesLeft()
    lblMines.Caption = Format$(MINES - mFlags, "000")
End Sub

Private Sub tmrClock_Timer()
    mSeconds = mSeconds + 1
    lblTime.Caption = Format$(mSeconds, "000")
End Sub

Private Sub cmdFace_Click()
    NewGame
End Sub
