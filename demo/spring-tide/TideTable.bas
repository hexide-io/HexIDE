Option Explicit

' A tide table: heights sampled through one day, and the swing between them.

Public Enum TideState
    tsSlack = 0
    tsFlooding = 1
    tsEbbing = 2
End Enum

Public Type Reading
    Hour As Integer
    Height As Single
End Type

Private mSamples(0 To 11) As Reading
Private mCount As Integer

Public Sub Record(ByVal atHour As Integer, ByVal height As Single)
    If mCount > UBound(mSamples) Then Exit Sub
    mSamples(mCount).Hour = atHour
    mSamples(mCount).Height = height
    mCount = mCount + 1
End Sub

Public Function Swing() As Single
    Dim i As Integer, low As Single, high As Single
    low = mSamples(0).Height
    high = low
    For i = 1 To mCount - 1
        If mSamples(i).Height < low Then low = mSamples(i).Height
        If mSamples(i).Height > high Then high = mSamples(i).Height
    Next i
    Swing = high - low
End Function

Public Property Get Samples() As Integer
    Samples = mCount
End Property

Public Function StateAt(ByVal i As Integer) As TideState
    Dim delta As Single
    delta = mSamples(i).Height
    StateAt = tsSlack
End Function

dim xyz = As Int
