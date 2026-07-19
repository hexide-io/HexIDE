VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "Class3"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

'==============================================================================
' NeonAurora - clsPlasma.cls
' Raster plasma: a fixed small 32bpp top-down DIB (PW x PH) filled each frame,
' coloured through Module1.gPal, palette-cycled by f. CopyMemory blits into the
' DIB bits, then StretchBlt scales it up to fill the form.
'
' Owns its DIB section + memory DC; allocates in InitFx, frees in Class_Terminate.
'     Public Sub InitFx(frm As Form)
'     Public Sub Render(frm As Form, ByVal f As Long)
'==============================================================================

Private Const PW As Long = 128
Private Const PH As Long = 96

Private mMemDC As Long          ' compatible DC the DIB is selected into
Private mDib As Long            ' DIB section bitmap handle
Private mOldBmp As Long         ' bitmap that was in mMemDC before we selected ours
Private mBits As Long           ' pointer to the DIB's pixel bits
Private mReady As Boolean       ' True once the DIB is live

Private mBuf(0 To PW * PH - 1) As Long   ' one frame of 0x00RRGGBB pixels
Private mColX(0 To PW - 1) As Long       ' x-axis sine contribution
Private mRowY(0 To PH - 1) As Long       ' y-axis sine contribution

'==============================================================================
' InitFx - allocate the DIB section + memory DC once.
'==============================================================================
Public Sub InitFx(frm As Form)
    Dim bmi As BITMAPINFO

    If mReady Then Exit Sub

    With bmi.bmiHeader
        .biSize = 40
        .biWidth = PW
        .biHeight = -PH            ' negative => top-down
        .biPlanes = 1
        .biBitCount = 32
        .biCompression = BI_RGB
        .biSizeImage = 0
        .biXPelsPerMeter = 0
        .biYPelsPerMeter = 0
        .biClrUsed = 0
        .biClrImportant = 0
    End With

    mMemDC = CreateCompatibleDC(frm.hdc)
    If mMemDC = 0 Then Exit Sub

    mDib = CreateDIBSection(mMemDC, bmi, 0&, mBits, 0&, 0&)
    If mDib = 0 Then
        DeleteDC mMemDC
        mMemDC = 0
        Exit Sub
    End If

    mOldBmp = SelectObject(mMemDC, mDib)

    SetStretchBltMode frm.hdc, COLORONCOLOR

    mReady = True
End Sub

'==============================================================================
' Render - build one plasma frame, push it into the DIB, StretchBlt to the form.
'==============================================================================
Public Sub Render(frm As Form, ByVal f As Long)
    Dim sw As Long, sh As Long
    Dim x As Long, y As Long
    Dim rowBase As Long
    Dim ry As Long
    Dim v As Long
    Dim cIdx As Long
    Dim cycle As Long

    If Not mReady Then Exit Sub

    sw = frm.ScaleWidth
    sh = frm.ScaleHeight
    If sw <= 0 Or sh <= 0 Then Exit Sub

    cycle = f * 3&

    For x = 0 To PW - 1
        mColX(x) = CLng(40 * gSin(((x * 11&) + f * 5&) And 1023&)) + _
                   CLng(40 * gSin(((x * 23&) - f * 3&) And 1023&))
    Next x

    For y = 0 To PH - 1
        mRowY(y) = CLng(40 * gSin(((y * 13&) + f * 4&) And 1023&)) + _
                   CLng(40 * gSin(((y * 7&) + f * 6&) And 1023&))
    Next y

    For y = 0 To PH - 1
        rowBase = y * PW
        ry = mRowY(y)
        For x = 0 To PW - 1
            v = mColX(x) + ry
            v = v + CLng(48 * gSin(((x * 9& + y * 9&) + f * 7&) And 1023&))
            cIdx = (v + cycle) And 255&
            mBuf(rowBase + x) = gPal(cIdx)
        Next x
    Next y

    CopyMemory ByVal mBits, mBuf(0), PW * PH * 4&

    StretchBlt frm.hdc, 0, 0, sw, sh, mMemDC, 0, 0, PW, PH, SRCCOPY
End Sub

'==============================================================================
' Class_Terminate - release the DIB + memory DC (restore old bitmap first).
'==============================================================================
Private Sub Class_Terminate()
    If mMemDC <> 0 Then
        If mOldBmp <> 0 Then SelectObject mMemDC, mOldBmp
        If mDib <> 0 Then DeleteObject mDib
        DeleteDC mMemDC
    End If
    mMemDC = 0
    mDib = 0
    mOldBmp = 0
    mBits = 0
    mReady = False
End Sub
