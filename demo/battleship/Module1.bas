Attribute VB_Name = "Module1"
Public gF As Integer

Sub Log(s As String)
    Print #gF, s
End Sub

Sub Main()
    gF = FreeFile
    Open App.Path & "\output.txt" For Output As #gF

    Dim g As Game
    Set g = New Game
    Dim a As Announcer
    Set a = New Announcer
    a.Watch g

    ' A 3-ship fleet: sizes 2, 3, 4 laid horizontally.
    g.AddShip 2, 0, 0
    g.AddShip 3, 2, 0
    g.AddShip 4, 4, 0

    Log "Firing..."
    g.Fire 0, 0     ' ship 0
    g.Fire 0, 1     ' ship 0 -> sunk
    g.Fire 5, 5     ' miss
    g.Fire 2, 0     ' ship 1
    g.Fire 2, 1     ' ship 1
    g.Fire 2, 2     ' ship 1 -> sunk
    Log "AllSunk=" & g.AllSunk

    Close #gF
End Sub
