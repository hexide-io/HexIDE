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

'==============================================================================
' clsCopper.cls - NeonAurora copper bars (BACKGROUND layer, drawn FIRST).
' Paints every pixel as a stack of horizontal gradient bands sliding through
' the gSin LUT. Uses RGB() for form drawing (NOT PixColor).
'
' Interface (verbatim from the architect):
'     Public Sub InitFx(frm As Form)
'     Public Sub Render(frm As Form, ByVal f As Long)
'==============================================================================

Private Const NUMBARS As Long = 7        ' how many copper ridges
Private Const STRIPH  As Long = 3        ' scanline strip height in pixels

Private mBarHue(0 To NUMBARS - 1) As Long    ' 0..255 position in the sweep
Private mBarPhase(0 To NUMBARS - 1) As Long  ' phase offset into gSin (0..1023)
Private mBarWob(0 To NUMBARS - 1) As Long    ' secondary wobble phase
Private mBarHalf(0 To NUMBARS - 1) As Long   ' half-height (glow radius) in px
Private mInited As Boolean

'==============================================================================
' InitFx - seed the per-bar tables. Called once after InitShared / AutoRedraw.
'==============================================================================
Public Sub InitFx(frm As Form)
    Dim i As Long

    For i = 0 To NUMBARS - 1
        ' Spread the bars across the indigo->magenta half of the wheel.
        mBarHue(i) = 8 + (i * 142&) \ (NUMBARS - 1)

        ' Fan the drift phases out evenly around the full LUT.
        mBarPhase(i) = (i * 1024&) \ NUMBARS

        ' Secondary wobble, deliberately off the main phase for organic drift.
        mBarWob(i) = (i * 311&) Mod 1024&

        ' Alternate fat / slim ridges for visual rhythm.
        If (i And 1) = 0 Then
            mBarHalf(i) = 26
        Else
            mBarHalf(i) = 18
        End If
    Next i

    mInited = True
End Sub

'==============================================================================
' Render - draw the copper field for frame f.
'==============================================================================
Public Sub Render(frm As Form, ByVal f As Long)
    Dim sw As Long, sh As Long
    Dim y As Long
    Dim i As Long
    Dim x1 As Long

    sw = frm.ScaleWidth
    sh = frm.ScaleHeight
    If sw <= 0 Or sh <= 0 Then Exit Sub
    If Not mInited Then InitFx frm

    x1 = sw - 1

    ' --- precompute each bar's centre Y for this frame ---------------------
    Dim cy(0 To NUMBARS - 1) As Long
    Dim half(0 To NUMBARS - 1) As Long
    Dim margin As Long, span As Long
    Dim s1 As Single, s2 As Single
    Dim p1 As Long, p2 As Long

    margin = sh \ 6
    If margin < 8 Then margin = 8
    span = sh - 2 * margin
    If span < 1 Then span = 1

    For i = 0 To NUMBARS - 1
        p1 = ((f * 5&) + mBarPhase(i)) And 1023&
        p2 = ((f * 13&) + mBarWob(i)) And 1023&
        s1 = gSin(p1)
        s2 = gSin(p2)

        cy(i) = margin + CLng((span / 2#) * (1# + s1)) + CLng(s2 * (span * 0.12))
        If cy(i) < 0 Then cy(i) = 0
        If cy(i) > sh - 1 Then cy(i) = sh - 1

        half(i) = mBarHalf(i) + CLng(s2 * 5#)
        If half(i) < 6 Then half(i) = 6
    Next i

    ' --- paint the form as horizontal strips ------------------------------
    Dim r As Long, g As Long, b As Long
    Dim dy As Long, h As Long
    Dim glow As Single, w As Single
    Dim hue As Long, pc As Long
    Dim rBar As Long, gBar As Long, bBar As Long
    Dim accR As Single, accG As Single, accB As Single
    Dim shimmer As Single

    shimmer = 0.85 + 0.15 * gSin((f * 7&) And 1023&)

    For y = 0 To sh - 1 Step STRIPH

        ' Deep indigo void as the base for this scanline.
        accR = 5#
        accG = 0#
        accB = 18#

        For i = 0 To NUMBARS - 1
            dy = y - cy(i)
            If dy < 0 Then dy = -dy
            h = half(i)
            If dy <= h Then
                w = 1# - (dy / CSng(h))
                w = w * w

                hue = mBarHue(i) + ((dy * 40&) \ (h + 1&))
                If hue < 0 Then hue = 0
                If hue > 255 Then hue = 255

                pc = gPal(hue)
                rBar = (pc \ 65536&) And &HFF&
                gBar = (pc \ 256&) And &HFF&
                bBar = pc And &HFF&

                glow = w * shimmer
                accR = accR + rBar * glow
                accG = accG + gBar * glow
                accB = accB + bBar * glow
            End If
        Next i

        r = CLng(accR)
        g = CLng(accG)
        b = CLng(accB)
        If r < 0 Then r = 0
        If r > 255 Then r = 255
        If g < 0 Then g = 0
        If g > 255 Then g = 255
        If b < 0 Then b = 0
        If b > 255 Then b = 255

        ' One filled box for the whole strip width. Form drawing => RGB().
        frm.Line (0, y)-(x1, y + STRIPH - 1), RGB(r, g, b), BF
    Next y
End Sub
