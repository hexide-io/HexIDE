VERSION 5.00
Begin VB.UserControl Gauge 
   ClientHeight    =   3600
   ClientLeft      =   0
   ClientTop       =   0
   ClientWidth     =   4800
   ScaleHeight     =   3600
   ScaleWidth      =   4800
   Begin VB.CommandButton Command1 
      Caption         =   "Command1"
      Height          =   495
      Left            =   1800
      TabIndex        =   0
      Top             =   1560
      Width           =   1215
   End
End
Attribute VB_Name = "Gauge"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = True
Option Explicit

Private mLevel As Integer

Public Property Get Level() As Integer
    Level = mLevel
End Property

Public Property Let Level(ByVal newLevel As Integer)
    mLevel = newLevel
    PropertyChanged "Level"
End Property

Private Sub UserControl_ReadProperties(PropBag As PropertyBag)
    mLevel = PropBag.ReadProperty("Level", 0)
End Sub

Private Sub UserControl_WriteProperties(PropBag As PropertyBag)
    PropBag.WriteProperty "Level", mLevel, 0
End Sub
