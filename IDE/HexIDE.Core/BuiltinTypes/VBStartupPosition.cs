namespace HexIDE.Runtime.BuiltinTypes;

public enum VBStartupPosition
{
    [Vb6Name("Manual")]          StartUpManual = 0,
    [Vb6Name("CenterOwner")]     StartUpOwner = 1,
    [Vb6Name("CenterScreen")]    StartUpScreen = 2,
    [Vb6Name("Windows Default")] StartUpWindowsDefault = 3,
}
