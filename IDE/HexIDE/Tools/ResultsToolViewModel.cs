using System;
using Dock.Model.Mvvm.Controls;

namespace HexIDE.Tools;

public abstract class ResultsToolViewModel : Tool
{
    public event Action? ShowRequested;
    internal event Action? LayoutResized;
    protected void RequestShow() => ShowRequested?.Invoke();
    internal void NotifyLayoutResized() => LayoutResized?.Invoke();
}
