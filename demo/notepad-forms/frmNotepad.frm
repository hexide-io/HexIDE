VERSION 5.00
Object = "{F9043C88-F6F2-101A-A3C9-08002B2F49FB}#1.2#0"; "COMDLG32.OCX"
Begin VB.Form frmNotepad 
   Caption         =   "Untitled - Notepad"
   ClientHeight    =   4500
   ClientLeft      =   60
   ClientTop       =   630
   ClientWidth     =   6600
   ScaleHeight     =   4500
   ScaleWidth      =   6600
   StartUpPosition =   3  'Windows Default
   Begin MSComDlg.CommonDialog dlgCommon 
      Left            =   0
      Top             =   0
      _ExtentX        =   847
      _ExtentY        =   847
      _Version        =   393216
   End
   Begin VB.TextBox txtEdit 
      BeginProperty Font 
         Name            =   "Lucida Console"
         Size            =   9.75
         Charset         =   0
         Weight          =   400
         Underline       =   0   'False
         Italic          =   0   'False
         Strikethrough   =   0   'False
      EndProperty
      Height          =   4500
      HideSelection   =   0   'False
      Index           =   1
      Left            =   0
      MultiLine       =   -1  'True
      ScrollBars      =   2  'Vertical
      TabIndex        =   1
      Top             =   0
      Visible         =   0   'False
      Width           =   6600
   End
   Begin VB.TextBox txtEdit 
      BeginProperty Font 
         Name            =   "Lucida Console"
         Size            =   9.75
         Charset         =   0
         Weight          =   400
         Underline       =   0   'False
         Italic          =   0   'False
         Strikethrough   =   0   'False
      EndProperty
      Height          =   4500
      HideSelection   =   0   'False
      Index           =   0
      Left            =   0
      MultiLine       =   -1  'True
      ScrollBars      =   3  'Both
      TabIndex        =   0
      Top             =   0
      Width           =   6600
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
      Begin VB.Menu mnuFileSave 
         Caption         =   "&Save"
         Shortcut        =   ^S
      End
      Begin VB.Menu mnuFileSaveAs 
         Caption         =   "Save &As..."
      End
      Begin VB.Menu mnuFileSep1 
         Caption         =   "-"
      End
      Begin VB.Menu mnuFileExit 
         Caption         =   "E&xit"
      End
   End
   Begin VB.Menu mnuEdit 
      Caption         =   "&Edit"
      Begin VB.Menu mnuEditCut 
         Caption         =   "Cu&t"
         Shortcut        =   ^X
      End
      Begin VB.Menu mnuEditCopy 
         Caption         =   "&Copy"
         Shortcut        =   ^C
      End
      Begin VB.Menu mnuEditPaste 
         Caption         =   "&Paste"
         Shortcut        =   ^V
      End
      Begin VB.Menu mnuEditDelete 
         Caption         =   "De&lete"
         Shortcut        =   {DEL}
      End
      Begin VB.Menu mnuEditSep1 
         Caption         =   "-"
      End
      Begin VB.Menu mnuEditFind 
         Caption         =   "&Find..."
         Shortcut        =   ^F
      End
      Begin VB.Menu mnuEditFindNext 
         Caption         =   "Find &Next"
         Shortcut        =   {F3}
      End
      Begin VB.Menu mnuEditSep2 
         Caption         =   "-"
      End
      Begin VB.Menu mnuEditSelectAll 
         Caption         =   "Select &All"
         Shortcut        =   ^A
      End
      Begin VB.Menu mnuEditTimeDate 
         Caption         =   "Time/&Date"
         Shortcut        =   {F5}
      End
   End
   Begin VB.Menu mnuFormat 
      Caption         =   "F&ormat"
      Begin VB.Menu mnuFormatWordWrap 
         Caption         =   "&Word Wrap"
      End
      Begin VB.Menu mnuFormatFont 
         Caption         =   "&Font..."
      End
   End
   Begin VB.Menu mnuHelp 
      Caption         =   "&Help"
      Begin VB.Menu mnuHelpAbout 
         Caption         =   "&About Notepad"
      End
   End
End
Attribute VB_Name = "frmNotepad"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' The text is in one of two TextBoxes. MultiLine and ScrollBars can't be changed at run time, so Word Wrap swaps
' txtEdit(0), which has a horizontal scroll bar and so never wraps, for txtEdit(1), which has none and so always does.
Private mEdit As Integer

' The file being edited, and whether the text has changed since it was opened or saved.
Private mPath As String
Private mDirty As Boolean

' The last search, which Find Next repeats and the Find dialog starts from.
Private mFindWhat As String
Private mMatchCase As Boolean
Private mFindDown As Boolean

Private Const FILE_FILTER As String = "Text Documents (*.txt)|*.txt|All Files (*.*)|*.*"

Private Sub Form_Load()
    Dim path As String
    mFindDown = True
    NewDocument
    ' Like Notepad, open the file named on the command line.
    path = Trim$(Command$)
    If Left$(path, 1) = """" Then path = Mid$(path, 2, Len(path) - 2)
    If path <> "" Then LoadFile path
End Sub

Private Sub Form_Resize()
    Dim i As Integer
    If WindowState = vbMinimized Then Exit Sub
    For i = 0 To 1
        txtEdit(i).Move 0, 0, ScaleWidth, ScaleHeight
    Next i
End Sub

' Closing with unsaved changes asks first. Cancel keeps the window open.
Private Sub Form_QueryUnload(Cancel As Integer, UnloadMode As Integer)
    If Not ConfirmDiscard() Then Cancel = True
End Sub

Private Sub Form_Unload(Cancel As Integer)
    Unload frmFind
End Sub

Private Sub txtEdit_Change(Index As Integer)
    mDirty = True
End Sub

' --- File ---

Private Sub mnuFileNew_Click()
    If ConfirmDiscard() Then NewDocument
End Sub

Private Sub mnuFileOpen_Click()
    If Not ConfirmDiscard() Then Exit Sub
    On Error GoTo Cancelled
    With dlgCommon
        .CancelError = True
        .Filter = FILE_FILTER
        .FilterIndex = 1
        .FileName = ""
        .Flags = cdlOFNFileMustExist Or cdlOFNHideReadOnly
        .ShowOpen
        LoadFile .FileName
    End With
    Exit Sub
Cancelled:
    If Err.Number <> cdlCancel Then MsgBox Err.Description, vbExclamation, "Notepad"
End Sub

Private Sub mnuFileSave_Click()
    Save
End Sub

Private Sub mnuFileSaveAs_Click()
    SaveAs
End Sub

Private Sub mnuFileExit_Click()
    Unload Me
End Sub

' --- Edit ---

' Opening the Edit menu raises its Click first, so the items match the selection and the clipboard.
Private Sub mnuEdit_Click()
    Dim selected As Boolean
    selected = txtEdit(mEdit).SelLength > 0
    mnuEditCut.Enabled = selected
    mnuEditCopy.Enabled = selected
    mnuEditDelete.Enabled = selected
    mnuEditPaste.Enabled = Clipboard.GetFormat(vbCFText)
    mnuEditFindNext.Enabled = mFindWhat <> ""
End Sub

Private Sub mnuEditCut_Click()
    With txtEdit(mEdit)
        If .SelLength = 0 Then Exit Sub
        Clipboard.Clear
        Clipboard.SetText .SelText
        .SelText = ""
    End With
End Sub

Private Sub mnuEditCopy_Click()
    With txtEdit(mEdit)
        If .SelLength = 0 Then Exit Sub
        Clipboard.Clear
        Clipboard.SetText .SelText
    End With
End Sub

Private Sub mnuEditPaste_Click()
    txtEdit(mEdit).SelText = Clipboard.GetText(vbCFText)
End Sub

' Delete takes the selection, or the character after the caret, as the Del key does.
Private Sub mnuEditDelete_Click()
    With txtEdit(mEdit)
        If .SelLength = 0 Then .SelLength = 1
        .SelText = ""
    End With
End Sub

Private Sub mnuEditFind_Click()
    frmFind.Show vbModeless, Me
End Sub

Private Sub mnuEditFindNext_Click()
    If mFindWhat = "" Then
        mnuEditFind_Click
    Else
        FindText mFindWhat, mMatchCase, mFindDown
    End If
End Sub

Private Sub mnuEditSelectAll_Click()
    With txtEdit(mEdit)
        .SelStart = 0
        .SelLength = Len(.Text)
    End With
End Sub

' Notepad's Time/Date: the time, then the date, at the caret.
Private Sub mnuEditTimeDate_Click()
    txtEdit(mEdit).SelText = Format$(Now, "h:nn AM/PM m/d/yyyy")
End Sub

' --- Format ---

Private Sub mnuFormatWordWrap_Click()
    Dim other As Integer, dirty As Boolean
    other = 1 - mEdit
    ' Copying the text raises Change, which would mark an unchanged document as changed.
    dirty = mDirty
    With txtEdit(other)
        .Text = txtEdit(mEdit).Text
        .SelStart = txtEdit(mEdit).SelStart
        .SelLength = txtEdit(mEdit).SelLength
        .Visible = True
    End With
    txtEdit(mEdit).Visible = False
    mEdit = other
    mDirty = dirty
    mnuFormatWordWrap.Checked = (mEdit = 1)
    txtEdit(mEdit).SetFocus
End Sub

Private Sub mnuFormatFont_Click()
    Dim i As Integer
    On Error GoTo Cancelled
    With dlgCommon
        .CancelError = True
        .Flags = cdlCFScreenFonts
        .FontName = txtEdit(mEdit).FontName
        .FontSize = txtEdit(mEdit).FontSize
        .FontBold = txtEdit(mEdit).FontBold
        .FontItalic = txtEdit(mEdit).FontItalic
        .ShowFont
        For i = 0 To 1
            txtEdit(i).FontName = .FontName
            txtEdit(i).FontSize = .FontSize
            txtEdit(i).FontBold = .FontBold
            txtEdit(i).FontItalic = .FontItalic
        Next i
    End With
    Exit Sub
Cancelled:
    If Err.Number <> cdlCancel Then MsgBox Err.Description, vbExclamation, "Notepad"
End Sub

' --- Help ---

Private Sub mnuHelpAbout_Click()
    MsgBox "Notepad" & vbCrLf & vbCrLf & "Windows' Notepad, written as a VB6 program for HexIDE.", _
        vbInformation, "About Notepad"
End Sub

' --- Find, called by the Find dialog ---

Public Property Get LastFind() As String
    LastFind = mFindWhat
End Property

Public Property Get LastMatchCase() As Boolean
    LastMatchCase = mMatchCase
End Property

Public Property Get LastFindDown() As Boolean
    LastFindDown = mFindDown
End Property

' Finds the next match after the selection, or the previous one before it, and selects it.
Public Sub FindText(ByVal what As String, ByVal matchCase As Boolean, ByVal down As Boolean)
    Dim compare As Long, found As Long
    mFindWhat = what
    mMatchCase = matchCase
    mFindDown = down
    If what = "" Then Exit Sub
    If matchCase Then compare = vbBinaryCompare Else compare = vbTextCompare
    With txtEdit(mEdit)
        If down Then
            found = InStr(.SelStart + .SelLength + 1, .Text, what, compare)
        ElseIf .SelStart > 0 Then
            found = InStrRev(.Text, what, .SelStart, compare)
        End If
        If found > 0 Then
            .SelStart = found - 1
            .SelLength = Len(what)
        Else
            MsgBox "Cannot find """ & what & """", vbInformation, "Notepad"
        End If
    End With
End Sub

' --- Documents ---

Private Sub NewDocument()
    txtEdit(mEdit).Text = ""
    mPath = ""
    mDirty = False
    ShowTitle
End Sub

Private Sub ShowTitle()
    Caption = DocumentName() & " - Notepad"
End Sub

Private Function DocumentName() As String
    Dim slash As Long
    If mPath = "" Then
        DocumentName = "Untitled"
    Else
        slash = InStrRev(mPath, "\")
        DocumentName = Mid$(mPath, slash + 1)
    End If
End Function

' True when the text can be thrown away: it is unchanged, or the user saved it or chose not to.
Private Function ConfirmDiscard() As Boolean
    If Not mDirty Then
        ConfirmDiscard = True
        Exit Function
    End If
    Select Case MsgBox("The text in the " & DocumentName() & " file has changed." & vbCrLf & vbCrLf & _
            "Do you want to save the changes?", vbYesNoCancel + vbExclamation, "Notepad")
        Case vbYes: ConfirmDiscard = Save()
        Case vbNo: ConfirmDiscard = True
        Case Else: ConfirmDiscard = False
    End Select
End Function

' Reads the file a line at a time. Line Input drops each line's ending, so the lines are joined with CR LF.
Private Sub LoadFile(ByVal path As String)
    Dim f As Integer, lineText As String, text As String, first As Boolean
    On Error GoTo Failed
    f = FreeFile
    Open path For Input As #f
    first = True
    Do While Not EOF(f)
        Line Input #f, lineText
        If first Then
            text = lineText
            first = False
        Else
            text = text & vbCrLf & lineText
        End If
    Loop
    Close #f
    txtEdit(mEdit).Text = text
    txtEdit(mEdit).SelStart = 0
    mPath = path
    mDirty = False
    ShowTitle
    Exit Sub
Failed:
    MsgBox "Cannot open " & path & vbCrLf & vbCrLf & Err.Description, vbExclamation, "Notepad"
    Close #f
End Sub

' Saves to the file it came from, or asks for one. False when the user cancelled or the save failed.
Private Function Save() As Boolean
    If mPath = "" Then
        Save = SaveAs()
    Else
        Save = WriteFile(mPath)
    End If
End Function

Private Function SaveAs() As Boolean
    On Error GoTo Cancelled
    With dlgCommon
        .CancelError = True
        .Filter = FILE_FILTER
        .FilterIndex = 1
        .DefaultExt = "txt"
        .FileName = mPath
        .Flags = cdlOFNOverwritePrompt Or cdlOFNHideReadOnly Or cdlOFNPathMustExist
        .ShowSave
        SaveAs = WriteFile(.FileName)
    End With
    Exit Function
Cancelled:
    If Err.Number <> cdlCancel Then MsgBox Err.Description, vbExclamation, "Notepad"
End Function

' The trailing semicolon keeps Print # from adding a line ending the text doesn't have.
Private Function WriteFile(ByVal path As String) As Boolean
    Dim f As Integer
    On Error GoTo Failed
    f = FreeFile
    Open path For Output As #f
    Print #f, txtEdit(mEdit).Text;
    Close #f
    mPath = path
    mDirty = False
    ShowTitle
    WriteFile = True
    Exit Function
Failed:
    MsgBox "Cannot save " & path & vbCrLf & vbCrLf & Err.Description, vbExclamation, "Notepad"
    Close #f
End Function
