VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "Class2"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

'==============================================================================
' NeonAurora - clsStars.cls
' 3D perspective starfield streaming toward the viewer. Foreground layer:
' never clears the form. Resize-safe (re-reads ScaleWidth/Height every Render).
'
'     Public Sub InitFx(frm As Form)
'     Public Sub Render(frm As Form, ByVal f As Long)
'==============================================================================

Private Const STAR_COUNT As Long = 320     ' how many stars stream at once
Private Const ZNEAR As Long = 24           ' near clip
Private Const ZFAR As Long = 1024          ' far spawn depth
Private Const WORLD As Long = 2048         ' half-extent of the (sx,sy) world cube

Private mSX() As Long          ' world X slot for each star (-WORLD..WORLD)
Private mSY() As Long          ' world Y slot for each star
Private mZ() As Long           ' current depth of each star (ZNEAR..ZFAR)
Private mSpeed() As Long       ' per-star approach speed (gives depth variety)
Private mSeed As Long          ' tiny LCG seed for deterministic respawns
Private mReady As Boolean

'------------------------------------------------------------------------------
' Deterministic LCG done in Double to avoid VB6 Long overflow (error 6).
'------------------------------------------------------------------------------
Private Const LCG_MOD As Double = 2147483648#   ' 2^31
Private Const LCG_MUL As Double = 1103515245#
Private Const LCG_ADD As Double = 12345#

Private Function NextRnd(ByVal rng As Long) As Long
    Dim s As Double
    s = (CDbl(mSeed) * LCG_MUL) + LCG_ADD
    s = s - (Int(s / LCG_MOD) * LCG_MOD)
    mSeed = CLng(s)             ' s is in 0 .. 2^31-1, always fits a positive Long
    If rng <= 0 Then
        NextRnd = 0
    Else
        NextRnd = mSeed Mod rng
    End If
End Function

'------------------------------------------------------------------------------
' SpawnStar - drop one star at the far plane (or a random depth on first seed).
'------------------------------------------------------------------------------
Private Sub SpawnStar(ByVal idx As Long, ByVal spawnFar As Boolean)
    Dim wx As Long, wy As Long
    wx = NextRnd(WORLD * 2&) - WORLD
    wy = NextRnd(WORLD * 2&) - WORLD
    If wx > -64 And wx < 64 Then wx = wx + 96
    If wy > -64 And wy < 64 Then wy = wy + 96
    mSX(idx) = wx
    mSY(idx) = wy

    If spawnFar Then
        mZ(idx) = ZFAR
    Else
        mZ(idx) = ZNEAR + NextRnd(ZFAR - ZNEAR)
    End If

    mSpeed(idx) = 6& + NextRnd(10&)
End Sub

'==============================================================================
' InitFx - seed the star arrays. Called once by the director after InitShared.
'==============================================================================
Public Sub InitFx(frm As Form)
    Dim i As Long

    ReDim mSX(0 To STAR_COUNT - 1)
    ReDim mSY(0 To STAR_COUNT - 1)
    ReDim mZ(0 To STAR_COUNT - 1)
    ReDim mSpeed(0 To STAR_COUNT - 1)

    mSeed = 305419896          ' fixed seed -> deterministic
    For i = 0 To STAR_COUNT - 1
        SpawnStar i, False     ' scatter through full depth so the field is full at f=0
    Next i

    mReady = True
End Sub

'==============================================================================
' Render - advance every star toward the camera and plot it.
'==============================================================================
Public Sub Render(frm As Form, ByVal f As Long)
    If Not mReady Then Exit Sub

    Dim sw As Long, sh As Long
    sw = frm.ScaleWidth
    sh = frm.ScaleHeight
    If sw <= 0 Or sh <= 0 Then Exit Sub

    Dim cx As Long, cy As Long
    cx = sw \ 2& + CLng(gSin((f * 2&) And 1023&) * (sw \ 18&))
    cy = sh \ 2& + CLng(gSin((f * 3& + 256&) And 1023&) * (sh \ 22&))

    Dim focal As Long
    focal = sw
    If sh < focal Then focal = sh
    focal = focal * 3&          ' wider = flatter; *3 gives a punchy tunnel

    Dim i As Long
    Dim zz As Long
    Dim px As Long, py As Long
    Dim r As Long, g As Long, b As Long
    Dim col As Long
    Dim inv As Long             ' 256..0 inverse-depth factor for size/brightness
    Dim tw As Long
    Dim rad As Long

    Dim oldDW As Integer
    oldDW = frm.DrawWidth

    For i = 0 To STAR_COUNT - 1
        zz = mZ(i) - mSpeed(i)
        If zz <= ZNEAR Then
            SpawnStar i, True
            zz = mZ(i)
        Else
            mZ(i) = zz
        End If

        px = cx + (mSX(i) * focal) \ zz
        py = cy + (mSY(i) * focal) \ zz

        If px >= 0 And px < sw And py >= 0 And py < sh Then

            inv = ((ZFAR - zz) * 256&) \ (ZFAR - ZNEAR)
            If inv < 0 Then inv = 0
            If inv > 255 Then inv = 255

            r = 10& + (inv * 200&) \ 255&
            g = 20& + (inv * 235&) \ 255&
            b = 80& + (inv * 175&) \ 255&
            tw = CLng(gSin((f * 11& + i * 37&) And 1023&) * 18#)
            r = r + tw: g = g + tw: b = b + tw
            If r < 0 Then r = 0
            If r > 255 Then r = 255
            If g < 0 Then g = 0
            If g > 255 Then g = 255
            If b < 0 Then b = 0
            If b > 255 Then b = 255

            col = RGB(r, g, b)

            If inv < 96& Then
                frm.DrawWidth = 1
                frm.PSet (px, py), col
            ElseIf inv < 176& Then
                frm.DrawWidth = 2
                frm.PSet (px, py), col
            Else
                rad = 1& + ((inv - 176&) \ 24&)        ' 1..4 px
                frm.DrawWidth = 1
                frm.Circle (px, py), rad, col
            End If
        End If
    Next i

    frm.DrawWidth = oldDW
End Sub
