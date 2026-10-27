using Dock.Model.Mvvm.Controls;

namespace HexIDE.Addins;

public sealed class AddinToolWindowViewModel : Tool
{
    public string AddinId { get; }
    public string WindowId { get; }

    public object? Content { get; }

    public AddinToolWindowViewModel(string id, string title, Func<object> factory, string addinId = "")
    {
        AddinId = addinId;
        WindowId = id;
        Id = addinId.Length == 0 ? id : $"addin:{addinId.Length}:{addinId}:{id}";
        Title = title;
        CanPin = false;
        CanClose = true;
        Content = factory();
    }
}
