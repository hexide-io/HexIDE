VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "Class4"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

'==============================================================================
' NeonAurora - clsVector.cls   (3D GLENZ VECTOR)
' A rotating wireframe glenz star drawn with frm.Line over the background.
' Depth-cued: magenta at the front, deep indigo at the back. All pixel/coord
' math Long; reads ScaleWidth/Height every Render. Never clears the form.
'
'     Public Sub InitFx(frm As Form)
'     Public Sub Render(frm As Form, ByVal f As Long)
'==============================================================================

Private mVx() As Double        ' vertex X (model space, ~ -1..+1)
Private mVy() As Double        ' vertex Y
Private mVz() As Double        ' vertex Z
Private mNV As Long            ' number of vertices

Private mE0() As Long          ' edge: index of first vertex
Private mE1() As Long          ' edge: index of second vertex
Private mNE As Long            ' number of edges

Private mPx() As Double        ' rotated/projected screen X
Private mPy() As Double        ' rotated/projected screen Y
Private mPz() As Double        ' rotated camera-space Z (for depth cue)

Private mReady As Boolean

'==============================================================================
' InitFx - build the vertex + edge tables once. No form-size caching.
'==============================================================================
Public Sub InitFx(frm As Form)
    Dim i As Long
    Dim k As Double

    ' ---- VERTICES --------------------------------------------------------
    mNV = 14
    ReDim mVx(0 To mNV - 1)
    ReDim mVy(0 To mNV - 1)
    ReDim mVz(0 To mNV - 1)

    ' cube corners (unit cube, half-extent 1)
    SetV 0, -1, -1, -1
    SetV 1, 1, -1, -1
    SetV 2, 1, 1, -1
    SetV 3, -1, 1, -1
    SetV 4, -1, -1, 1
    SetV 5, 1, -1, 1
    SetV 6, 1, 1, 1
    SetV 7, -1, 1, 1

    ' spike tips poke out past the faces for the "star" silhouette
    k = 1.75
    SetV 8, k, 0, 0          ' +X
    SetV 9, -k, 0, 0         ' -X
    SetV 10, 0, k, 0         ' +Y
    SetV 11, 0, -k, 0        ' -Y
    SetV 12, 0, 0, k         ' +Z
    SetV 13, 0, 0, -k        ' -Z

    ' ---- EDGES -----------------------------------------------------------
    mNE = 36
    ReDim mE0(0 To mNE - 1)
    ReDim mE1(0 To mNE - 1)
    i = 0

    ' cube frame
    AddEdge i, 0, 1: AddEdge i, 1, 2: AddEdge i, 2, 3: AddEdge i, 3, 0   ' back face
    AddEdge i, 4, 5: AddEdge i, 5, 6: AddEdge i, 6, 7: AddEdge i, 7, 4   ' front face
    AddEdge i, 0, 4: AddEdge i, 1, 5: AddEdge i, 2, 6: AddEdge i, 3, 7   ' connectors

    ' +X spike (tip 8) -> the four corners with x = +1 : 1,2,5,6
    AddEdge i, 8, 1: AddEdge i, 8, 2: AddEdge i, 8, 5: AddEdge i, 8, 6
    ' -X spike (tip 9) -> corners with x = -1 : 0,3,4,7
    AddEdge i, 9, 0: AddEdge i, 9, 3: AddEdge i, 9, 4: AddEdge i, 9, 7
    ' +Y spike (tip 10) -> corners with y = +1 : 2,3,6,7
    AddEdge i, 10, 2: AddEdge i, 10, 3: AddEdge i, 10, 6: AddEdge i, 10, 7
    ' -Y spike (tip 11) -> corners with y = -1 : 0,1,4,5
    AddEdge i, 11, 0: AddEdge i, 11, 1: AddEdge i, 11, 4: AddEdge i, 11, 5
    ' +Z spike (tip 12) -> corners with z = +1 : 4,5,6,7
    AddEdge i, 12, 4: AddEdge i, 12, 5: AddEdge i, 12, 6: AddEdge i, 12, 7
    ' -Z spike (tip 13) -> corners with z = -1 : 0,1,2,3
    AddEdge i, 13, 0: AddEdge i, 13, 1: AddEdge i, 13, 2: AddEdge i, 13, 3

    ' scratch buffers
    ReDim mPx(0 To mNV - 1)
    ReDim mPy(0 To mNV - 1)
    ReDim mPz(0 To mNV - 1)

    mReady = True
End Sub

' helper: store a model vertex
Private Sub SetV(ByVal idx As Long, ByVal vx As Double, ByVal vy As Double, ByVal vz As Double)
    mVx(idx) = vx
    mVy(idx) = vy
    mVz(idx) = vz
End Sub

' helper: store an edge and advance the running index
Private Sub AddEdge(ByRef idx As Long, ByVal a As Long, ByVal b As Long)
    mE0(idx) = a
    mE1(idx) = b
    idx = idx + 1
End Sub

'==============================================================================
' Render - rotate, project, depth-cue and draw every edge for frame f.
'==============================================================================
Public Sub Render(frm As Form, ByVal f As Long)
    Dim sw As Long, sh As Long
    Dim cx As Double, cy As Double
    Dim sc As Double
    Dim i As Long

    Dim ax As Double, ay As Double, az As Double
    Dim sax As Double, cax As Double
    Dim say As Double, cay As Double
    Dim saz As Double, caz As Double

    Dim x0 As Double, y0 As Double, z0 As Double
    Dim x1 As Double, y1 As Double, z1 As Double
    Dim x2 As Double, y2 As Double, z2 As Double

    Dim camZ As Double, persp As Double, invZ As Double

    Dim zMin As Double, zMax As Double, zRng As Double

    Dim e As Long, va As Long, vb As Long
    Dim depth As Double, tNorm As Double
    Dim rr As Long, gg As Long, bb As Long
    Dim breathe As Double
    Dim glint As Double, shim As Double

    If Not mReady Then Exit Sub

    sw = frm.ScaleWidth
    sh = frm.ScaleHeight
    If sw <= 0 Or sh <= 0 Then Exit Sub

    cx = sw / 2#
    cy = sh / 2#

    If sw < sh Then
        sc = sw
    Else
        sc = sh
    End If
    breathe = 1# + 0.08 * gSin((f * 4&) And 1023&)
    sc = sc * 0.2 * breathe

    ax = f * 0.017
    ay = f * 0.013
    az = f * 0.009
    sax = Sin(ax): cax = Cos(ax)
    say = Sin(ay): cay = Cos(ay)
    saz = Sin(az): caz = Cos(az)

    camZ = 4#        ' distance from camera to object centre (model units)
    persp = 2.6#     ' perspective strength / focal length

    zMin = 1E+30
    zMax = -1E+30
    For i = 0 To mNV - 1
        x0 = mVx(i): y0 = mVy(i): z0 = mVz(i)

        ' rotate about X
        y1 = y0 * cax - z0 * sax
        z1 = y0 * sax + z0 * cax
        x1 = x0
        ' rotate about Y
        x2 = x1 * cay + z1 * say
        z2 = -x1 * say + z1 * cay
        y2 = y1
        ' rotate about Z
        x0 = x2 * caz - y2 * saz
        y0 = x2 * saz + y2 * caz
        z0 = z2

        mPz(i) = z0

        invZ = persp / (camZ + z0)
        mPx(i) = cx + x0 * invZ * sc
        mPy(i) = cy - y0 * invZ * sc   ' screen Y grows downward -> negate

        If z0 < zMin Then zMin = z0
        If z0 > zMax Then zMax = z0
    Next i

    zRng = zMax - zMin
    If zRng < 0.0001 Then zRng = 0.0001

    For e = 0 To mNE - 1
        va = mE0(e)
        vb = mE1(e)

        depth = (mPz(va) + mPz(vb)) * 0.5
        tNorm = (depth - zMin) / zRng        ' 0 = far/back, 1 = near/front

        rr = Int(40 + tNorm * 215)
        gg = Int(10 + tNorm * tNorm * 60)
        bb = Int(120 + (1# - tNorm) * 110)
        If bb > 255 Then bb = 255

        If tNorm > 0.72 Then
            glint = (tNorm - 0.72) / 0.28
            shim = 0.5 + 0.5 * gSin((f * 11& + e * 37&) And 1023&)
            gg = gg + Int(glint * shim * 200)
            bb = bb + Int(glint * shim * 60)
            rr = rr - Int(glint * shim * 40)
        End If

        If rr < 0 Then rr = 0
        If rr > 255 Then rr = 255
        If gg < 0 Then gg = 0
        If gg > 255 Then gg = 255
        If bb < 0 Then bb = 0
        If bb > 255 Then bb = 255

        frm.DrawWidth = 1 + Int(tNorm * 3)           ' 1..4

        frm.Line (mPx(va), mPy(va))-(mPx(vb), mPy(vb)), RGB(rr, gg, bb)
    Next e

    ' bright core node at each spike tip + cube corner
    frm.DrawWidth = 2
    For i = 0 To mNV - 1
        tNorm = (mPz(i) - zMin) / zRng
        If tNorm > 0.55 Then
            rr = Int(120 + tNorm * 135)
            gg = Int(tNorm * 230)
            bb = Int(200 + tNorm * 55)
            If rr > 255 Then rr = 255
            If gg > 255 Then gg = 255
            If bb > 255 Then bb = 255
            frm.PSet (mPx(i), mPy(i)), RGB(rr, gg, bb)
        End If
    Next i

    ' restore the shared drawing state for layers that follow
    frm.DrawWidth = 1
End Sub
