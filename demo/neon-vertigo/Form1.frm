VERSION 5.00
Begin VB.Form Form1
   Caption =   "Form1"
   Width =   6000
   Height =   4500
   ClientWidth =   6000
   ScaleWidth =   6000
   ClientHeight =   4500
   ScaleHeight =   4500
   Begin VB.Timer Timer0
      Left =   360
      Top =   360
   End
End
Option Explicit

Const NB = 130

Private px(1 To NB) As Double
Private py(1 To NB) As Double
Private pz(1 To NB) As Double
Private t As Double

Private Sub Form_Load()
    Dim i As Long, k As Double, lon As Double, r As Double
    Me.AutoRedraw = True
    Me.ScaleMode = 3
    Me.Caption = "Neon Vertigo"
    Me.BackColor = RGB(4, 4, 16)
    For i = 1 To NB
        k = -1 + 2 * (i - 0.5) / NB
        r = Sqr(1 - k * k)
        lon = i * 2.399963229728653
        px(i) = r * Cos(lon)
        py(i) = k
        pz(i) = r * Sin(lon)
    Next i
    Timer0.Interval = 30
    Timer0.Enabled = True
    Randomize
End Sub

Private Sub Timer0_Timer()
    Dim i As Long, j As Long
    Dim w As Long, h As Long
    Dim cx As Double, cy As Double, rad As Double
    Dim sa As Double, ca As Double, sb As Double, cb As Double
    Dim x As Double, y As Double, z As Double
    Dim x1 As Double, z1 As Double, y2 As Double, z2 As Double
    Dim persp As Double, br As Double
    Dim sx(1 To NB) As Double, sy(1 To NB) As Double
    Dim sr(1 To NB) As Double, dz(1 To NB) As Double
    Dim col(1 To NB) As Long, ord(1 To NB) As Long, tmp As Long

    t = t + 0.04
    w = Me.ScaleWidth
    h = Me.ScaleHeight
    cx = w / 2
    cy = h / 2
    rad = h
    If w < h Then rad = w
    rad = rad * 0.34

    sa = Sin(t): ca = Cos(t)
    sb = Sin(t * 0.6): cb = Cos(t * 0.6)

    Me.Cls
    Me.FillStyle = 0

    For i = 1 To NB
        x = px(i): y = py(i): z = pz(i)
        x1 = x * cb - z * sb
        z1 = x * sb + z * cb
        y2 = y * ca - z1 * sa
        z2 = y * sa + z1 * ca
        persp = 2.6 / (2.6 + z2)
        sx(i) = cx + x1 * rad * persp
        sy(i) = cy + y2 * rad * persp
        sr(i) = rad * 0.085 * persp
        dz(i) = z2
        br = 1 - (z2 + 1) / 2
        col(i) = RGB(40 + 215 * br, 20 + 120 * br, 120 + 135 * br)
        ord(i) = i
    Next i

    For i = 1 To NB - 1
        For j = 1 To NB - i
            If dz(ord(j)) < dz(ord(j + 1)) Then
                tmp = ord(j): ord(j) = ord(j + 1): ord(j + 1) = tmp
            End If
        Next j
    Next i

    For i = 1 To NB
        j = ord(i)
        If sr(j) > 0.5 Then
            Me.FillColor = col(j)
            Me.Circle (sx(j), sy(j)), sr(j), col(j)
        End If
    Next i
End Sub
