using Dock.Model.Mvvm.Controls;

namespace HexIDE.Addins;

public sealed class AddinToolWindowViewModel : Tool
{
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
