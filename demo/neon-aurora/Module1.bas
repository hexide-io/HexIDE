Attribute VB_Name = "Module1"
Option Explicit

'==============================================================================
' NeonAurora - Module1.bas  (SHARED BACKBONE)
' A real VB6 6.0 demoscene intro for HexIDE.
' Holds: every graphics/CopyMemory API Declare (Public, for the classes),
'        the DIB Type defs, shared globals (gFrame, gSin LUT, gPal palette),
'        InitShared(), and PixColor().  NO music here (see modMusic.bas).
'==============================================================================

Public Const PI As Double = 3.14159265358979

'--- Win32 raster / GDI Declares (Public so clsPlasma etc. can call them) -----
Public Declare Function CreateCompatibleDC Lib "gdi32" (ByVal hdc As Long) As Long
Public Declare Function CreateDIBSection Lib "gdi32" (ByVal hdc As Long, pBMI As BITMAPINFO, ByVal un As Long, ByRef lplpVoid As Long, ByVal hSection As Long, ByVal dwOffset As Long) As Long
Public Declare Function SelectObject Lib "gdi32" (ByVal hdc As Long, ByVal hObject As Long) As Long
Public Declare Function DeleteObject Lib "gdi32" (ByVal hObject As Long) As Long
Public Declare Function DeleteDC Lib "gdi32" (ByVal hdc As Long) As Long
Public Declare Function BitBlt Lib "gdi32" (ByVal hDestDC As Long, ByVal x As Long, ByVal y As Long, ByVal nWidth As Long, ByVal nHeight As Long, ByVal hSrcDC As Long, ByVal xSrc As Long, ByVal ySrc As Long, ByVal dwRop As Long) As Long
Public Declare Function StretchBlt Lib "gdi32" (ByVal hDestDC As Long, ByVal x As Long, ByVal y As Long, ByVal nWidth As Long, ByVal nHeight As Long, ByVal hSrcDC As Long, ByVal xSrc As Long, ByVal ySrc As Long, ByVal nSrcWidth As Long, ByVal nSrcHeight As Long, ByVal dwRop As Long) As Long
Public Declare Function SetStretchBltMode Lib "gdi32" (ByVal hdc As Long, ByVal nStretchMode As Long) As Long

'--- kernel32 memory copy -----------------------------------------------------
Public Declare Sub CopyMemory Lib "kernel32" Alias "RtlMoveMemory" (Destination As Any, Source As Any, ByVal Length As Long)

'--- GDI raster-op / stretch constants ---------------------------------------
Public Const SRCCOPY As Long = &HCC0020
Public Const BI_RGB As Long = 0
Public Const COLORONCOLOR As Long = 1

'--- DIB Type defs ------------------------------------------------------------
Public Type BITMAPINFOHEADER
    biSize As Long
    biWidth As Long
    biHeight As Long
    biPlanes As Integer
    biBitCount As Integer
    biCompression As Long
    biSizeImage As Long
    biXPelsPerMeter As Long
    biYPelsPerMeter As Long
    biClrUsed As Long
    biClrImportant As Long
End Type

Public Type BITMAPINFO
    bmiHeader As BITMAPINFOHEADER
    bmiColors As Long
End Type

'--- Shared globals -----------------------------------------------------------
Public gFrame As Long              ' master frame counter, bumped by the director
Public gSin(0 To 1023) As Single   ' sine look-up table, full turn over 0..1023
Public gPal(0 To 255) As Long      ' aurora palette, DIB 0x00RRGGBB entries

Private mInited As Boolean

'==============================================================================
' InitShared - fill the sine LUT and build the aurora palette. Call ONCE at
' Form_Load before any InitFx. Idempotent.
'==============================================================================
Public Sub InitShared()
    Dim i As Long
    Dim t As Double
    Dim r As Long, g As Long, b As Long

    If mInited Then Exit Sub

    ' Sine LUT: one full revolution across indices 0..1023, range -1..+1.
    For i = 0 To 1023
        gSin(i) = Sin(i / 1024# * 2# * PI)
    Next i

    ' Aurora palette, 256 entries, looping smoothly so plasma can cycle it.
    ' Quadrants: indigo -> magenta -> cyan -> back to indigo.
    For i = 0 To 255
        t = i / 256# * 2# * PI

        ' R rides the magenta hump (peaks near i=128).
        r = Int(128 + 110 * Sin(t - PI / 2#))            ' 18..238
        ' G stays low except in the cyan highlight band.
        g = Int(96 + 96 * Sin(t * 2# - PI / 2#))         ' 0..192 oscillating twice
        ' B stays high (cold base), dipping only in the hot-magenta zone.
        b = Int(170 + 84 * Sin(t + PI / 2#))             ' 86..254

        If r < 0 Then r = 0
        If r > 255 Then r = 255
        If g < 0 Then g = 0
        If g > 255 Then g = 255
        If b < 0 Then b = 0
        If b > 255 Then b = 255

        gPal(i) = PixColor(r, g, b)
    Next i

    mInited = True
End Sub

'==============================================================================
' PixColor - pack R,G,B (each 0..255) into a DIB-format Long 0x00RRGGBB.
' NOTE: this is for the DIB Long buffer. For Form Line/Circle use RGB() instead.
' & suffix on 65536& / 256& keeps the intermediate math in Long (the rules).
'==============================================================================
Public Function PixColor(ByVal r As Long, ByVal g As Long, ByVal b As Long) As Long
    PixColor = r * 65536& + g * 256& + b
End Function
