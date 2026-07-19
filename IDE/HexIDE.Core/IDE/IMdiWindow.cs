using System;

namespace HexIDE.IDE;

public interface IMdiWindow
{
    string Title { get; }
    object? Icon { get; }
    event Action<IMdiWindow>? CloseRequest;
}
