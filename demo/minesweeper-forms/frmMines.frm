VERSION 5.00
Begin VB.Form frmMines 
   BorderStyle     =   1  'Fixed Single
   Caption         =   "Minesweeper"
   ClientHeight    =   3420
   ClientLeft      =   45
   ClientTop       =   615
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
      BeginProperty Font 
         Name            =   "MS Sans Serif"
         Size            =   12
         Charset         =   0
         Weight          =   700
         Underline       =   0   'False
         Italic          =   0   'False
         Strikethrough   =   0   'False
      EndProperty
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
   Begin VB.Menu mnuGame 
      Caption         =   "&Game"
      Begin VB.Menu mnuNew 
         Caption         =   "&New"
         Shortcut        =   {F2}
      End
      Begin VB.Menu mnuSep1 
         Caption         =   "-"
      End
      Begin VB.Menu mnuLevel 
         Caption         =   "&Beginner"
         Checked         =   -1  'True
         Index           =   0
      End
      Begin VB.Menu mnuLevel 
         Caption         =   "&Intermediate"
         Index           =   1
      End
      Begin VB.Menu mnuLevel 
         Caption         =   "&Expert"
         Index           =   2
      End
      Begin VB.Menu mnuSep2 
         Caption         =   "-"
      End
      Begin VB.Menu mnuExit 
         Caption         =   "E&xit"
      End
   End
End
Attribute VB_Name = "frmMines"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' A cell, the margin round the field, and where the field starts below the face, all in twips.
Private Const CELL As Integer = 300
Private Const MARGIN As Integer = 120
Private Const FIELD_TOP As Integer = 600

' A cell not yet dug, one dug, and the mine that went off.
Private Const COVERED As Long = &HC0C0C0
Private Const DUG As Long = &HE0E0E0
Private Const BLAST As Long = vbRed

' The flag, the mine and the faces are Wingdings: P is a flag, M a bomb, and J, K and L a smiling, a straight and a
' frowning face. On a machine without Wingdings, setting FontName to it raises 380, and the game draws letters instead.
Private Const SYMBOL_FONT As String = "Wingdings"
Private Const TEXT_FONT As String = "MS Sans Serif"
Private mGlyphs As Boolean

' The field's size, set by the level.
Private mRows As Integer
Private mCols As Integer
Private mMines As Integer

' What each cell holds, and what the player knows of it.
Private mMine() As Boolean
Private mNear() As Integer
Private mDug() As Boolean
Private mFlag() As Boolean

' How many lblCell elements are loaded, lblCell(0) included.
Private mLoaded As Integer

Private mPlaced As Boolean
Private mOver As Boolean
Private mDugCount As Integer
Private mFlags As Integer
Private mSeconds As Integer

Private Sub Form_Load()
    Randomize
    On Error Resume Next
    cmdFace.FontName = SYMBOL_FONT
    mGlyphs = (Err.Number = 0)
    On Error GoTo 0
    mLoaded = 1
    SetLevel 0
End Sub

' The face: "J" smiles, "K" holds its breath and "L" has lost. Without Wingdings, ":)", ":o" and ":(".
Private Sub Face(ByVal glyph As String, ByVal text As String)
    If mGlyphs Then
        cmdFace.Caption = glyph
    Else
        cmdFace.Caption = text
    End If
End Sub

' What a cell shows: a number in letters, or a symbol in Wingdings where the machine has it.
Private Sub Mark(ByVal i As Integer, ByVal glyph As String, ByVal text As String, ByVal colour As Long)
    With lblCell(i)
        If mGlyphs And glyph <> "" Then
            If .FontName <> SYMBOL_FONT Then .FontName = SYMBOL_FONT
            .Caption = glyph
        Else
            If .FontName <> TEXT_FONT Then .FontName = TEXT_FONT
            .Caption = text
        End If
        .ForeColor = colour
    End With
End Sub

' Beginner, Intermediate and Expert, as Windows has them.
Private Sub SetLevel(ByVal level As Integer)
    Select Case level
        Case 0: mRows = 9: mCols = 9: mMines = 10
        Case 1: mRows = 16: mCols = 16: mMines = 40
        Case 2: mRows = 16: mCols = 30: mMines = 99
    End Select
    ReDim mMine(mRows - 1, mCols - 1)
    ReDim mNear(mRows - 1, mCols - 1)
    ReDim mDug(mRows - 1, mCols - 1)
    ReDim mFlag(mRows - 1, mCols - 1)
    BuildField
    NewGame
End Sub

' lblCell(0) is on the form. The field loads one more for each other cell it needs, and unloads any it no longer does.
Private Sub BuildField()
    Dim i As Integer, cells As Integer
    cells = mRows * mCols
    For i = mLoaded To cells - 1
        Load lblCell(i)
    Next i
    For i = mLoaded - 1 To cells Step -1
        Unload lblCell(i)
    Next i
    mLoaded = cells
    For i = 0 To cells - 1
        lblCell(i).Move MARGIN + (i Mod mCols) * CELL, FIELD_TOP + (i \ mCols) * CELL, CELL, CELL
        lblCell(i).Visible = True
    Next i
    cmdFace.Left = MARGIN + (mCols * CELL - cmdFace.Width) \ 2
    lblTime.Left = MARGIN + mCols * CELL - lblTime.Width
    ' The form fits the field. What Width has and ScaleWidth doesn't is the border, and the menu bar too for Height.
    Width = (Width - ScaleWidth) + MARGIN * 2 + mCols * CELL
    Height = (Height - ScaleHeight) + FIELD_TOP + mRows * CELL + MARGIN
End Sub

Private Sub NewGame()
    Dim r As Integer, c As Integer
    For r = 0 To mRows - 1
        For c = 0 To mCols - 1
            mMine(r, c) = False
            mNear(r, c) = 0
            mDug(r, c) = False
            mFlag(r, c) = False
            Mark r * mCols + c, "", "", vbBlack
            lblCell(r * mCols + c).BackColor = COVERED
        Next c
    Next r
    mPlaced = False
    mOver = False
    mDugCount = 0
    mFlags = 0
    mSeconds = 0
    tmrClock.Enabled = False
    Face "J", ":)"
    lblTime.Caption = Format$(mSeconds, "000")
    ShowMinesLeft
End Sub

' The mines go down on the first dig, so that it is never one.
Private Sub PlaceMines(ByVal safeRow As Integer, ByVal safeCol As Integer)
    Dim placed As Integer, r As Integer, c As Integer, dr As Integer, dc As Integer
    Do While placed < mMines
        r = Int(Rnd * mRows)
        c = Int(Rnd * mCols)
        If Not mMine(r, c) And Not (r = safeRow And c = safeCol) Then
            mMine(r, c) = True
            placed = placed + 1
        End If
    Loop
    For r = 0 To mRows - 1
        For c = 0 To mCols - 1
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
    OnField = r >= 0 And r < mRows And c >= 0 And c < mCols
End Function

' The face holds its breath while a button is down on a cell.
Private Sub lblCell_MouseDown(Index As Integer, Button As Integer, Shift As Integer, X As Single, Y As Single)
    If mOver Then Exit Sub
    If Button = vbRightButton Then
        ToggleFlag Index \ mCols, Index Mod mCols
    Else
        Face "K", ":o"
    End If
End Sub

' A left click digs a cell. A middle click on a number whose mines are all flagged digs the cells round it.
Private Sub lblCell_MouseUp(Index As Integer, Button As Integer, Shift As Integer, X As Single, Y As Single)
    If mOver Then Exit Sub
    If Button = vbLeftButton Then
        Dig Index \ mCols, Index Mod mCols
    ElseIf Button = vbMiddleButton Then
        Chord Index \ mCols, Index Mod mCols
    End If
    If Not mOver Then Face "J", ":)"
End Sub

Private Sub ToggleFlag(ByVal r As Integer, ByVal c As Integer)
    If mDug(r, c) Then Exit Sub
    mFlag(r, c) = Not mFlag(r, c)
    If mFlag(r, c) Then
        Mark r * mCols + c, "P", "F", vbRed
        mFlags = mFlags + 1
    Else
        Mark r * mCols + c, "", "", vbBlack
        mFlags = mFlags - 1
    End If
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
        If mDugCount = mRows * mCols - mMines Then Win
    End If
End Sub

Private Sub Chord(ByVal r As Integer, ByVal c As Integer)
    Dim dr As Integer, dc As Integer, flags As Integer
    If Not mDug(r, c) Then Exit Sub
    For dr = -1 To 1
        For dc = -1 To 1
            If OnField(r + dr, c + dc) Then
                If mFlag(r + dr, c + dc) Then flags = flags + 1
            End If
        Next dc
    Next dr
    If flags <> mNear(r, c) Then Exit Sub
    For dr = -1 To 1
        For dc = -1 To 1
            If OnField(r + dr, c + dc) And Not mOver Then Dig r + dr, c + dc
        Next dc
    Next dr
End Sub

' Digs a cell, and every cell round one with no mine near it, and round those: the flood fill.
Private Sub Uncover(ByVal r As Integer, ByVal c As Integer)
    Dim dr As Integer, dc As Integer
    If Not OnField(r, c) Then Exit Sub
    If mDug(r, c) Or mFlag(r, c) Then Exit Sub
    mDug(r, c) = True
    mDugCount = mDugCount + 1
    lblCell(r * mCols + c).BackColor = DUG
    If mNear(r, c) > 0 Then Mark r * mCols + c, "", CStr(mNear(r, c)), NumberColor(mNear(r, c))
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
    Face "L", ":("
    For i = 0 To mRows * mCols - 1
        If mMine(i \ mCols, i Mod mCols) And Not mFlag(i \ mCols, i Mod mCols) Then Mark i, "M", "*", vbBlack
    Next i
    lblCell(r * mCols + c).BackColor = BLAST
    MsgBox "Boom! You hit a mine after " & mSeconds & " seconds.", vbExclamation, "Minesweeper"
End Sub

Private Sub Win()
    Dim i As Integer
    mOver = True
    tmrClock.Enabled = False
    Face "J", "B)"
    ' Every cell left covered is a mine, so it is flagged for you.
    For i = 0 To mRows * mCols - 1
        If Not mDug(i \ mCols, i Mod mCols) Then Mark i, "P", "F", vbRed
    Next i
    mFlags = mMines
    ShowMinesLeft
    MsgBox "You cleared the field in " & mSeconds & " seconds.", vbInformation, "Minesweeper"
End Sub

' Flagging more cells than there are mines takes the count below zero, as Windows does.
Private Sub ShowMinesLeft()
    lblMines.Caption = Format$(mMines - mFlags, "000")
End Sub

Private Sub tmrClock_Timer()
    If mSeconds < 999 Then mSeconds = mSeconds + 1
    lblTime.Caption = Format$(mSeconds, "000")
End Sub

Private Sub cmdFace_Click()
    NewGame
End Sub

Private Sub mnuNew_Click()
    NewGame
End Sub

Private Sub mnuLevel_Click(Index As Integer)
    Dim i As Integer
    For i = mnuLevel.LBound To mnuLevel.UBound
        mnuLevel(i).Checked = (i = Index)
    Next i
    SetLevel Index
End Sub

Private Sub mnuExit_Click()
    Unload Me
End Sub
