Attribute VB_Name = "modMain"
Option Explicit

' What the setup dialog asked for, read by the board.
Public gPlayer As String
Public gSize As Integer
Public gCancelled As Boolean

Sub Main()
    Randomize
    ' The dialog hides itself on OK and unloads itself on Cancel or its close box.
    gCancelled = True
    frmSetup.Show vbModal
    If gCancelled Then Exit Sub
    gPlayer = Trim$(frmSetup.txtName.Text)
    gSize = frmSetup.BoardSize
    Unload frmSetup
    ' Main returns here, and the run goes on until the board unloads.
    frmBoard.Show
End Sub
