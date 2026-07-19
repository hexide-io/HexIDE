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
      Left =   300
      Top =   300
   End
End
Option Explicit

Private fx As New Class1

Private Sub Form_Load()
    Me.AutoRedraw = True
    Me.ScaleMode = vbPixels
    Me.BackColor = RGB(0, 0, 0)
    Me.Caption = "Wild Ecstasy"
    Timer0.Interval = 30
    Timer0.Enabled = True
    Randomize
End Sub

Private Sub Timer0_Timer()
    fx.Render Me
End Sub
