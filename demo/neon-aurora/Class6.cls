VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "Class6"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

'==============================================================================
' NeonAurora - clsScroll.cls   (BIG SINE SCROLLER)
' A long message scrolls right-to-left across the lower-middle of the screen.
' Each glyph is printed individually with frm.Print, Y bobbed by gSin, colour
' cycled magenta<->cyan. Wraps seamlessly forever. Resize-adaptive.
'
'     Public Sub InitFx(frm As Form)
'     Public Sub Render(frm As Form, ByVal f As Long)
'==============================================================================

Private mText As String
Private mTextLen As Long          ' character count of mText

Private mScrollX As Long          ' pixel-space scroll offset (Long)

Private mPitch() As Long          ' per-glyph pixel widths
Private mPitchValid As Boolean
Private mLastFontH As Long        ' font height the pitch table was built for
Private mTotalW As Long           ' total pixel width of one full pass of mText

Private Const SCROLL_SPEED As Long = 4       ' pixels per frame, R->L
Private Const WAVE_AMP_FRAC As Double = 0.06 ' vertical bob amplitude / ScaleHeight
Private Const FONT_FRAC As Double = 0.11     ' font height / ScaleHeight

'==============================================================================
' InitFx - one-time per-instance setup. Font is set inside Render.
'==============================================================================
Public Sub InitFx(frm As Form)
    mText = "                                        NEON AURORA . . . a real VB6 6.0 intro coded inside HEXIDE !   *** greetz flying out to all the sceners keeping the AMIGA spirit alive in 2026 ***   this whole production was wired up by CLAUDE driving HexIDE through the MCP automation bridge - no mouse harmed !   raster plasma + copper bars + a 3D starfield + a glenz vector + sine bobs + this very scroller, all compiled by the REAL vb6.exe and humming on the classic runtime .   sixteen-bit integers tried to overflow us but we typed everything LONG and lived to tell the tale &   hardware sprites ? we don't need no stinking hardware sprites - just Sin , Cos and a DIB section !   shouts to the copper , the blitter , and every demcoder who ever fought a 30ms Timer .   keep it chunky , keep it cycled , keep it BEAUTIFUL . . .   wrapping back around now , hold tight . . .                                                                                "
    mTextLen = Len(mText)
    mScrollX = 0
    mPitchValid = False
    mLastFontH = -1
End Sub

'==============================================================================
' Render - one frame of scroller.
'==============================================================================
Public Sub Render(frm As Form, ByVal f As Long)
    Dim sw As Long, sh As Long
    Dim fontH As Long
    Dim baseY As Long
    Dim amp As Double
    Dim startX As Long

    sw = frm.ScaleWidth
    sh = frm.ScaleHeight
    If sw <= 0 Or sh <= 0 Then Exit Sub
    If mTextLen <= 0 Then Exit Sub

    fontH = CLng(sh * FONT_FRAC)
    If fontH < 12 Then fontH = 12
    frm.FontName = "Arial"
    frm.FontBold = True
    frm.FontSize = PixelsToPoints(fontH)
    frm.FontTransparent = True

    If (Not mPitchValid) Or (fontH <> mLastFontH) Then
        BuildPitch frm
        mLastFontH = fontH
    End If
    If mTotalW <= 0 Then Exit Sub

    amp = sh * WAVE_AMP_FRAC
    baseY = sh \ 2 + sh \ 8       ' a touch below centre

    mScrollX = (f * SCROLL_SPEED) Mod mTotalW
    If mScrollX < 0 Then mScrollX = mScrollX + mTotalW

    startX = sw - mScrollX

    PrintRun frm, startX, baseY, amp, fontH, f, sw
    PrintRun frm, startX + mTotalW, baseY, amp, fontH, f, sw
End Sub

'==============================================================================
' PrintRun - print the whole message once starting at pen X = ox, clipped to
' the visible band [ 0 .. sw ].
'==============================================================================
Private Sub PrintRun(frm As Form, ByVal ox As Long, ByVal baseY As Long, _
                     ByVal amp As Double, ByVal fontH As Long, ByVal f As Long, _
                     ByVal sw As Long)
    Dim i As Long
    Dim penX As Long
    Dim gw As Long
    Dim ch As String
    Dim phase As Long
    Dim wob As Double
    Dim gy As Long
    Dim cphase As Long
    Dim r As Long, g As Long, b As Long
    Dim mix As Double

    penX = ox
    For i = 1 To mTextLen
        gw = mPitch(i - 1)

        If penX + gw >= 0 And penX <= sw Then
            ch = Mid$(mText, i, 1)
            If ch <> " " Then
                phase = ((i * 48&) + (f * 7&)) And 1023&
                wob = gSin(phase)
                gy = baseY - CLng(amp * wob) - (fontH \ 2)

                cphase = ((i * 24&) - (f * 9&)) And 1023&
                mix = (gSin(cphase) + 1#) * 0.5

                r = CLng(255 - mix * 135)        ' 255 -> 120
                g = CLng(48 + mix * 192)          '  48 -> 240
                b = CLng(220 + mix * 35)          ' 220 -> 255
                If r < 0 Then r = 0
                If r > 255 Then r = 255
                If g < 0 Then g = 0
                If g > 255 Then g = 255
                If b < 0 Then b = 0
                If b > 255 Then b = 255

                ' subtle dark drop-shadow for neon-on-near-black pop
                frm.CurrentX = penX + 2
                frm.CurrentY = gy + 2
                frm.ForeColor = RGB(8, 0, 24)
                frm.Print ch;

                ' the glowing glyph itself
                frm.CurrentX = penX
                frm.CurrentY = gy
                frm.ForeColor = RGB(r, g, b)
                frm.Print ch;
            End If
        End If

        penX = penX + gw
        If penX > sw Then Exit For
    Next i
End Sub

'==============================================================================
' BuildPitch - measure every glyph's pixel width for the current font; cache it.
'==============================================================================
Private Sub BuildPitch(frm As Form)
    Dim i As Long
    Dim w As Long

    ReDim mPitch(0 To mTextLen - 1)
    mTotalW = 0
    For i = 1 To mTextLen
        w = CLng(frm.TextWidth(Mid$(mText, i, 1)))
        If w < 1 Then w = 1
        mPitch(i - 1) = w
        mTotalW = mTotalW + w
    Next i
    If mTotalW < 1 Then mTotalW = 1
    mPitchValid = True
End Sub

'==============================================================================
' PixelsToPoints - desired glyph cell height in pixels -> VB6 FontSize (points).
'==============================================================================
Private Function PixelsToPoints(ByVal px As Long) As Single
    Dim dpiY As Double
    Dim tpp As Double

    tpp = Screen.TwipsPerPixelY
    If tpp <= 0 Then tpp = 15#           ' 96 DPI fallback (1440/96)
    dpiY = 1440# / tpp                   ' pixels per inch
    If dpiY <= 0 Then dpiY = 96#

    PixelsToPoints = CSng(px * 72# / dpiY)
    If PixelsToPoints < 6! Then PixelsToPoints = 6!
End Function
