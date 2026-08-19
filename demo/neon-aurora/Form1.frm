VERSION 5.00
Begin VB.Form Form1
   Caption =   "Form1"
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

'==============================================================================
' NeonAurora - Form1 (the DIRECTOR)
' Drives the whole intro: one Timer fires every ~30ms, bumps gFrame, dispatches
' the active part's layers back-to-front, ticks the music, and presents the
' frame with Me.Refresh. Loops gFrame back to 0 at the end of the timeline so
' the demo runs forever. Esc quits.
'==============================================================================

' --- effect instances (mapped to the IDE's class module names) ----------------
Private copper As New Class1
Private stars As New Class2
Private plasma As New Class3
Private vector As New Class4
Private bobs As New Class5
Private scroll As New Class6

' --- timeline part boundaries (inclusive end frames) --------------------------
Private Const END_AWAKENING As Long = 300     ' copper + stars + scroll
Private Const END_PLASMATIDE As Long = 660    ' plasma + scroll
Private Const END_GLENZ As Long = 1020        ' copper + vector + scroll
Private Const END_BOBCLOUD As Long = 1380     ' plasma + bobs + scroll
Private Const END_FULLAURORA As Long = 1740   ' copper + stars + bobs + scroll
Private Const END_DRIFTOUT As Long = 1980     ' copper + stars + scroll (loop here)

Private Sub Form_Load()
    Me.ScaleMode = vbPixels
    Me.AutoRedraw = True
    Me.BackColor = RGB(0, 0, 0)
    Me.Caption = "NeonAurora :: a HexIDE intro in pure VB6"
    Me.KeyPreview = True

    ' Give the demo some room to breathe.
    Me.Width = 800 * Screen.TwipsPerPixelX
    Me.Height = 600 * Screen.TwipsPerPixelY

    ' Shared backbone first, then the music, then every effect's one-time setup.
    InitShared
    InitMusic

    copper.InitFx Me
    stars.InitFx Me
    plasma.InitFx Me
    vector.InitFx Me
    bobs.InitFx Me
    scroll.InitFx Me

    gFrame = 0
    Timer0.Interval = 30
    Timer0.Enabled = True
End Sub

Private Sub Timer0_Timer()
    Dim f As Long

    gFrame = gFrame + 1
    f = gFrame

    ' Clear the AutoRedraw buffer; the background layer of every part repaints
    ' every pixel anyway, so this only guarantees a clean slate on part seams.
    Me.Cls

    ' --- dispatch the active part's layers, back-to-front --------------------
    If f <= END_AWAKENING Then
        ' Awakening: copper backdrop + starfield + scroller
        copper.Render Me, f
        stars.Render Me, f
        scroll.Render Me, f
    ElseIf f <= END_PLASMATIDE Then
        ' Plasma Tide: full-screen plasma + scroller
        plasma.Render Me, f
        scroll.Render Me, f
    ElseIf f <= END_GLENZ Then
        ' Glenz Rising: copper backdrop + 3D vector + scroller
        copper.Render Me, f
        vector.Render Me, f
        scroll.Render Me, f
    ElseIf f <= END_BOBCLOUD Then
        ' Bob Cloud: plasma backdrop + sine bobs + scroller
        plasma.Render Me, f
        bobs.Render Me, f
        scroll.Render Me, f
    ElseIf f <= END_FULLAURORA Then
        ' Full Aurora: copper + stars + bobs + scroller (the composite finale)
        copper.Render Me, f
        stars.Render Me, f
        bobs.Render Me, f
        scroll.Render Me, f
    Else
        ' Drift Out: settle back to the calm opening combo
        copper.Render Me, f
        stars.Render Me, f
        scroll.Render Me, f
    End If

    ' --- music advances on the same clock -----------------------------------
    MusicTick f

    ' --- present the frame ---------------------------------------------------
    Me.Refresh

    ' --- loop the timeline for a seamless forever-repeat ---------------------
    If gFrame >= END_DRIFTOUT Then gFrame = 0
End Sub

Private Sub Form_KeyDown(KeyCode As Integer, Shift As Integer)
    If KeyCode = 27 Then Unload Me      ' Esc quits
End Sub

Private Sub Form_Unload(Cancel As Integer)
    StopMusic
End Sub
