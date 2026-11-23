using System;

namespace HexIDE.IDE;

public interface IDialog
{
    string Title { get; }
    bool CanResize { get; }
    bool RefusesPersonClose => false;
    event Action<bool> CloseRequested;
}
