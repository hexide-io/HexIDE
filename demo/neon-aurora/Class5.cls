VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "Class5"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

'==============================================================================
' NeonAurora - clsBobs.cls   (VECTOR BALLS / SINE BOBS)
' A cloud of dots on a rotating 3D sphere, projected to 2D and drawn as filled
' Circles, depth-cued cyan->white core over an indigo/magenta halo, painters'
' sorted far->near. Pure form-drawing. Resize-adaptive; deterministic from f.
'
'     Public Sub InitFx(frm As Form)
'     Public Sub Render(frm As Form, ByVal f As Long)
'==============================================================================

Private Const NUMBOBS As Long = 140      ' dots on the sphere
Private Const TWO_PI As Double = 6.28318530717959

Private mBX(0 To NUMBOBS - 1) As Single
Private mBY(0 To NUMBOBS - 1) As Single
Private mBZ(0 To NUMBOBS - 1) As Single

Private mPhase(0 To NUMBOBS - 1) As Single

Private mScrX(0 To NUMBOBS - 1) As Long      ' projected screen x
Private mScrY(0 To NUMBOBS - 1) As Long      ' projected screen y
Private mDepth(0 To NUMBOBS - 1) As Single   ' rotated z (for sort + cue)
Private mRad(0 To NUMBOBS - 1) As Long       ' projected radius
Private mOrder(0 To NUMBOBS - 1) As Long     ' draw order, far -> near

Private mSeeded As Boolean

'==============================================================================
' InitFx - seed the bob cloud once via a deterministic Fibonacci lattice.
'==============================================================================
Public Sub InitFx(frm As Form)
    Dim i As Long
    Dim ga As Double      ' golden-angle increment
    Dim yy As Double
    Dim rr As Double
    Dim ang As Double

    If mSeeded Then Exit Sub

    ga = PI * (3# - Sqr(5#))

    For i = 0 To NUMBOBS - 1
        yy = 1# - (i / (NUMBOBS - 1#)) * 2#
        rr = Sqr(1# - yy * yy)
        ang = ga * i

        mBX(i) = CSng(Cos(ang) * rr)
        mBY(i) = CSng(yy)
        mBZ(i) = CSng(Sin(ang) * rr)

        mPhase(i) = CSng((i / NUMBOBS) * TWO_PI)
        mOrder(i) = i
    Next i

    mSeeded = True
End Sub

'==============================================================================
' Render - tumble, project, sort and draw the bob cloud for frame f.
'==============================================================================
Public Sub Render(frm As Form, ByVal f As Long)
    Dim sw As Long, sh As Long
    Dim cx As Long, cy As Long
    Dim baseR As Single
    Dim i As Long, j As Long, k As Long

    Dim ax As Long, ay As Long, az As Long
    Dim sinX As Single, cosX As Single
    Dim sinY As Single, cosY As Single
    Dim sinZ As Single, cosZ As Single

    Dim px As Single, py As Single, pz As Single
    Dim x1 As Single, y1 As Single, z1 As Single
    Dim x2 As Single, y2 As Single, z2 As Single
    Dim x3 As Single, y3 As Single, z3 As Single

    Dim breathe As Single
    Dim ridx As Long
    Dim grow As Single

    Dim persp As Single
    Dim depthN As Single
    Dim oldDW As Integer
    Dim oldFS As Integer

    sw = frm.ScaleWidth
    sh = frm.ScaleHeight
    If sw <= 0 Or sh <= 0 Then Exit Sub
    If Not mSeeded Then InitFx frm

    cx = sw \ 2
    cy = sh \ 2

    If sw < sh Then
        baseR = CSng(sw) * 0.33
    Else
        baseR = CSng(sh) * 0.33
    End If

    ax = (f * 3&) And 1023&               ' pitch
    ay = (f * 2&) And 1023&               ' yaw
    az = (f * 1&) And 1023&               ' roll
    sinX = gSin(ax): cosX = gSin((ax + 256&) And 1023&)
    sinY = gSin(ay): cosY = gSin((ay + 256&) And 1023&)
    sinZ = gSin(az): cosZ = gSin((az + 256&) And 1023&)

    breathe = gSin((f * 4&) And 1023&)    ' -1..+1

    For i = 0 To NUMBOBS - 1
        ridx = (CLng(mPhase(i) / TWO_PI * 1024#) + (f * 6&)) And 1023&
        grow = 1# + 0.16 * gSin(ridx) + 0.06 * breathe

        px = mBX(i) * grow
        py = mBY(i) * grow
        pz = mBZ(i) * grow

        ' Rotate X (pitch):  y,z
        y1 = py * cosX - pz * sinX
        z1 = py * sinX + pz * cosX
        x1 = px
        ' Rotate Y (yaw):    x,z
        x2 = x1 * cosY + z1 * sinY
        z2 = -x1 * sinY + z1 * cosY
        y2 = y1
        ' Rotate Z (roll):   x,y
        x3 = x2 * cosZ - y2 * sinZ
        y3 = x2 * sinZ + y2 * cosZ
        z3 = z2

        mDepth(i) = z3

        persp = 2.6! / (2.6! - z3)
        mScrX(i) = cx + CLng(x3 * baseR * persp)
        mScrY(i) = cy + CLng(y3 * baseR * persp)

        mRad(i) = CLng(baseR * 0.085 * persp) + 1
        If mRad(i) < 1 Then mRad(i) = 1
    Next i

    ' --- painters' sort: far (small z) first, near (large z) last ----------
    For i = 1 To NUMBOBS - 1
        k = mOrder(i)
        j = i - 1
        Do While j >= 0
            If mDepth(mOrder(j)) <= mDepth(k) Then Exit Do
            mOrder(j + 1) = mOrder(j)
            j = j - 1
        Loop
        mOrder(j + 1) = k
    Next i

    oldDW = frm.DrawWidth
    oldFS = frm.FillStyle
    frm.DrawWidth = 1
    frm.FillStyle = 0          ' vbFSSolid - filled circles

    For k = 0 To NUMBOBS - 1
        i = mOrder(k)

        If mScrX(i) >= -mRad(i) And mScrX(i) <= sw + mRad(i) Then
            If mScrY(i) >= -mRad(i) And mScrY(i) <= sh + mRad(i) Then

                depthN = (mDepth(i) + 1#) * 0.5!
                If depthN < 0! Then depthN = 0!
                If depthN > 1! Then depthN = 1!

                DrawBob frm, mScrX(i), mScrY(i), mRad(i), depthN
            End If
        End If
    Next k

    frm.DrawWidth = oldDW
    frm.FillStyle = oldFS
End Sub

'==============================================================================
' DrawBob - one bob: indigo/magenta halo with a hot cyan->white core.
'==============================================================================
Private Sub DrawBob(frm As Form, ByVal sx As Long, ByVal sy As Long, _
                    ByVal rad As Long, ByVal depthN As Single)
    Dim r As Long, g As Long, b As Long
    Dim haloR As Long
    Dim coreR As Long
    Dim cR As Long, cG As Long, cB As Long

    r = CLng(40 + depthN * 130)              ' 40..170
    g = CLng(20 + depthN * 120)              ' 20..140
    b = CLng(120 + depthN * 135)             ' 120..255
    If r > 255 Then r = 255
    If g > 255 Then g = 255
    If b > 255 Then b = 255

    haloR = rad + 2
    frm.FillColor = RGB(r, g, b)
    frm.Circle (sx, sy), haloR, RGB(r, g, b)

    cR = CLng(80 + depthN * 175)             ' 80..255
    cG = CLng(200 + depthN * 55)             ' 200..255
    cB = 255                                  ' always full blue/cyan
    If cR > 255 Then cR = 255
    If cG > 255 Then cG = 255

    coreR = rad \ 2
    If coreR < 1 Then coreR = 1
    frm.FillColor = RGB(cR, cG, cB)
    frm.Circle (sx, sy), coreR, RGB(cR, cG, cB)
End Sub
