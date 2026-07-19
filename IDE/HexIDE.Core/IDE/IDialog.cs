using System;

namespace HexIDE.IDE;

public interface IDialog
{
    string Title { get; }
    bool CanResize { get; }
    event Action<bool> CloseRequested;
}
