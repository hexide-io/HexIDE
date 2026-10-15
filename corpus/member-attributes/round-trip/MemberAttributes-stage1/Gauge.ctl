VERSION 5.00
Begin VB.UserControl Gauge 
   ClientHeight    =   1200
   ClientLeft      =   0
   ClientTop       =   0
   ClientWidth     =   2400
   ScaleHeight     =   1200
   ScaleWidth      =   2400
End
Attribute VB_Name = "Gauge"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private mLevel As Long

Public Property Get Level() As Long
Attribute Level.VB_Description = "Current level."
Attribute Level.VB_ProcData.VB_Invoke_Property = ";Behavior"
Attribute Level.VB_UserMemId = 0
Attribute Level.VB_MemberFlags = "200"
    Level = mLevel
End Property

Public Property Let Level(ByVal Value As Long)
    mLevel = Value
    PropertyChanged "Level"
End Property
