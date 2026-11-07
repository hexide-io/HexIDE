using Dock.Model.Mvvm.Controls;

namespace HexIDE.Addins;

public sealed class AddinToolWindowViewModel : Tool
{
    internal bool KeepSavedWidth { get; set; }
    internal System.Collections.Generic.HashSet<Dock.Model.Core.IDockable> SizedDocks { get; } = [];

    public object? Content { get; }

    public AddinToolWindowViewModel(string id, string title, Func<object> factory)
    {
        Id = id;
        Title = title;
        CanPin = false;
        CanClose = true;
        Content = factory();
    }
}
