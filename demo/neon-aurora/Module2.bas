Attribute VB_Name = "Module2"
Option Explicit

'==============================================================================
' NeonAurora - modMusic.bas   (CHIPTUNE SEQUENCER)
' A real VB6 6.0 winmm midiOut sequencer for the NeonAurora intro.
'
' Two-channel synthwave loop in A minor:
'   Ch 0  BASS : GM program 38 'Synth Bass 1' - relentless root-fifth-octave
'                pulse, Am-F-G-Em-ish 16-step loop, tight note-off each step.
'   Ch 1  ARP  : GM program 81 'Lead 1 square' - bright square-wave broken
'                triads (Am/F/C/G) cycling up an octave, short gated notes,
'                plus a sparse octave-up shimmer echo every 4th step.
'
' Non-blocking: midiOutShort only. Robust if no MIDI device exists - the whole
' thing becomes a silent no-op when the handle fails to open.
'
' Public API:  InitMusic()   MusicTick(ByVal f As Long)   StopMusic()
'==============================================================================

'--- winmm MIDI Declares (PRIVATE to this module) -----------------------------
Private Declare Function midiOutOpen Lib "winmm.dll" (lphMidiOut As Long, ByVal uDeviceID As Long, ByVal dwCallback As Long, ByVal dwInstance As Long, ByVal dwFlags As Long) As Long
Private Declare Function midiOutShort Lib "winmm.dll" Alias "midiOutShortMsg" (ByVal hMidiOut As Long, ByVal dwMsg As Long) As Long
Private Declare Function midiOutClose Lib "winmm.dll" (ByVal hMidiOut As Long) As Long

'--- MIDI status / device constants -------------------------------------------
Private Const MIDI_MAPPER As Long = -1&          ' default output device

Private Const STAT_NOTEON As Long = &H90&        ' note-on  (vel 0 = note-off)
Private Const STAT_PROG   As Long = &HC0&        ' program change
Private Const STAT_CTRL   As Long = &HB0&        ' control change (for all-notes-off)

Private Const CH_BASS As Long = 0
Private Const CH_ARP  As Long = 1

Private Const PROG_BASS As Long = 38             ' GM Synth Bass 1
Private Const PROG_ARP  As Long = 81             ' GM Lead 1 (square)

Private Const VEL_BASS  As Long = 100
Private Const VEL_ARP   As Long = 80
Private Const VEL_ECHO  As Long = 52             ' quieter octave-up shimmer

Private Const STEP_FRAMES As Long = 6            ' advance one step every 6 frames
Private Const PAT_LEN     As Long = 16           ' 16-step loop

Private Const REST As Long = -1                  ' rest marker in a pattern

'--- module state -------------------------------------------------------------
Private mHandle As Long          ' midiOut handle; 0 = no device / silent
Private mReady  As Boolean       ' True only when the handle is live
Private mStep   As Long          ' current sequencer step index 0..PAT_LEN-1
Private mStarted As Boolean      ' has the sequencer fired at least one step

' Notes currently sounding so we can note-off precisely before the next step.
Private mLiveBass As Long        ' MIDI note on Ch 0, or REST
Private mLiveArp  As Long        ' MIDI note on Ch 1, or REST
Private mLiveEcho As Long        ' octave-up echo on Ch 1, or REST

'--- pattern arrays (module-level Long, MIDI note numbers, REST = silence) -----
' A minor reference notes:  A1=33  E2=40  A2=45  F2=41  G2=43  E2=40  C3=48
Private mBass(0 To 15) As Long   ' driving root-fifth-octave pulse
Private mArp(0 To 15)  As Long   ' bright broken-triad arpeggio (octave up)

'==============================================================================
' InitMusic - build the patterns, open MIDI_MAPPER, send program changes.
'             Safe to call once at Form_Load. If no device, stays silent.
'==============================================================================
Public Sub InitMusic()
    Dim rc As Long

    BuildPatterns

    mHandle = 0
    mReady = False
    mStep = 0
    mStarted = False
    mLiveBass = REST
    mLiveArp = REST
    mLiveEcho = REST

    On Error Resume Next
    rc = midiOutOpen(mHandle, MIDI_MAPPER, 0&, 0&, 0&)
    On Error GoTo 0

    ' Guard the handle: a non-zero return code or a null handle means no device.
    If rc <> 0 Then mHandle = 0
    If mHandle = 0 Then
        mReady = False
        Exit Sub
    End If

    mReady = True

    ' Assign the chip voices.
    SendMsg ProgMsg(CH_BASS, PROG_BASS)
    SendMsg ProgMsg(CH_ARP, PROG_ARP)
End Sub

'==============================================================================
' MusicTick - call EVERY frame with the global frame counter. Advances the
'             sequencer once per STEP_FRAMES frames. No-op without a device.
'==============================================================================
Public Sub MusicTick(ByVal f As Long)
    Dim s As Long
    Dim bn As Long
    Dim an As Long
    Dim en As Long

    If Not mReady Then Exit Sub
    If mHandle = 0 Then Exit Sub

    ' Only act on step boundaries.
    If (f Mod STEP_FRAMES) <> 0 Then Exit Sub

    ' Determine which step this frame lands on (deterministic, resyncs cleanly).
    s = (f \ STEP_FRAMES) Mod PAT_LEN
    If s < 0 Then s = s + PAT_LEN

    ' --- 1) Silence everything that was sounding from the previous step -------
    KillLive

    mStep = s
    mStarted = True

    ' --- 2) BASS (Ch 0): fire the new root pulse ----------------------------
    bn = mBass(s)
    If bn <> REST Then
        SendMsg NoteOn(CH_BASS, bn, VEL_BASS)
        mLiveBass = bn
    End If

    ' --- 3) ARP (Ch 1): bright gated square-wave triad note ------------------
    an = mArp(s)
    If an <> REST Then
        SendMsg NoteOn(CH_ARP, an, VEL_ARP)
        mLiveArp = an

        ' Sparse octave-up shimmer echo every 4th step, when in range.
        If (s Mod 4) = 0 Then
            en = an + 12
            If en <= 127 Then
                SendMsg NoteOn(CH_ARP, en, VEL_ECHO)
                mLiveEcho = en
            End If
        End If
    End If
End Sub

'==============================================================================
' StopMusic - silence every sounding note, send all-notes-off on both
'             channels, then close the device. Safe to call repeatedly.
'==============================================================================
Public Sub StopMusic()
    If mHandle <> 0 Then
        ' Note-off anything we know is live.
        KillLive

        ' Belt-and-braces: CC 123 = All Notes Off on both channels.
        SendMsg STAT_CTRL + CH_BASS + (123 * 256&)
        SendMsg STAT_CTRL + CH_ARP + (123 * 256&)

        On Error Resume Next
        midiOutClose mHandle
        On Error GoTo 0
    End If

    mHandle = 0
    mReady = False
    mStarted = False
    mLiveBass = REST
    mLiveArp = REST
    mLiveEcho = REST
End Sub

'==============================================================================
' BuildPatterns - the 16-step loop. Am - F - G - Em feel.
'==============================================================================
Private Sub BuildPatterns()
    ' --- BASS: driving root-fifth-octave pulse with a couple of walks --------
    mBass(0) = 33:  mBass(1) = 40:  mBass(2) = 45:  mBass(3) = 40
    mBass(4) = 29:  mBass(5) = 36:  mBass(6) = 41:  mBass(7) = 36
    mBass(8) = 31:  mBass(9) = 38:  mBass(10) = 43: mBass(11) = 38
    mBass(12) = 28: mBass(13) = 35: mBass(14) = 40: mBass(15) = 35

    ' --- ARP: bright broken triads, an octave up over the bass --------------
    mArp(0) = 69:  mArp(1) = 72:  mArp(2) = 76:  mArp(3) = 81
    mArp(4) = 65:  mArp(5) = 69:  mArp(6) = 72:  mArp(7) = 77
    mArp(8) = 72:  mArp(9) = 76:  mArp(10) = 79: mArp(11) = 84
    mArp(12) = 67: mArp(13) = 71: mArp(14) = 74: mArp(15) = 79
End Sub

'==============================================================================
' KillLive - note-off every voice we believe is currently sounding.
'==============================================================================
Private Sub KillLive()
    If mLiveBass <> REST Then
        SendMsg NoteOn(CH_BASS, mLiveBass, 0)   ' vel 0 = note-off
        mLiveBass = REST
    End If
    If mLiveArp <> REST Then
        SendMsg NoteOn(CH_ARP, mLiveArp, 0)
        mLiveArp = REST
    End If
    If mLiveEcho <> REST Then
        SendMsg NoteOn(CH_ARP, mLiveEcho, 0)
        mLiveEcho = REST
    End If
End Sub

'==============================================================================
' NoteOn - pack a note-on (or note-off when vel=0) MIDI short message.
'   msg = &H90 + channel  +  note*256  +  velocity*65536
'==============================================================================
Private Function NoteOn(ByVal ch As Long, ByVal nt As Long, ByVal vel As Long) As Long
    NoteOn = STAT_NOTEON + ch + (nt * 256&) + (vel * 65536&)
End Function

'==============================================================================
' ProgMsg - pack a program-change MIDI short message.
'   msg = &HC0 + channel  +  program*256
'==============================================================================
Private Function ProgMsg(ByVal ch As Long, ByVal prog As Long) As Long
    ProgMsg = STAT_PROG + ch + (prog * 256&)
End Function

'==============================================================================
' SendMsg - fire one MIDI short message; tolerant of any transient error.
'==============================================================================
Private Sub SendMsg(ByVal dwMsg As Long)
    If mHandle = 0 Then Exit Sub
    On Error Resume Next
    midiOutShort mHandle, dwMsg
    On Error GoTo 0
End Sub
