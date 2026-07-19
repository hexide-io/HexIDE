VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "Class1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private Const PI As Double = 3.14159265358979

Private msg As String
Private t As Double

Private Sub Class_Initialize()
    msg = "HEXIDE"
    t = 0
End Sub

Public Sub Render(frm As Form)
    Dim i As Integer
    Dim n As Integer
    Dim ch As String
    Dim cw As Single
    Dim totalW As Single
    Dim x As Single
    Dim baseY As Single
    Dim amp As Single
    Dim yOff As Single
    Dim r As Integer, g As Integer, b As Integer

    frm.Cls
    t = t + 0.12
    n = Len(msg)

    frm.FontName = "Arial Black"
    frm.FontBold = True
    frm.FontSize = frm.ScaleHeight / 6
    If frm.FontSize < 8 Then frm.FontSize = 8

    totalW = 0
    For i = 1 To n
        totalW = totalW + frm.TextWidth(Mid(msg, i, 1))
    Next i

    x = (frm.ScaleWidth - totalW) / 2
    baseY = frm.ScaleHeight / 2 - frm.TextHeight(msg) / 2
    amp = frm.ScaleHeight / 7

    For i = 1 To n
        ch = Mid(msg, i, 1)
        cw = frm.TextWidth(ch)
        yOff = Sin(t + i * 0.7) * amp
        r = 128 + 127 * Sin(t * 1.3 + i * 0.5)
        g = 128 + 127 * Sin(t * 1.3 + i * 0.5 + 2#)
        b = 128 + 127 * Sin(t * 1.3 + i * 0.5 + 4#)
        frm.ForeColor = RGB(r, g, b)
        frm.CurrentX = x
        frm.CurrentY = baseY + yOff
        frm.Print ch;
        x = x + cw
    Next i
End Sub
