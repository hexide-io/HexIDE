using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HexIDE.Integration.Tests.Automation;

public partial class BoundTextPathView : UserControl
{
    public BoundTextPathView() => AvaloniaXamlLoader.Load(this);
}

public sealed class TextPathVm : INotifyPropertyChanged
{
    public string Caption { get; private set; } = "for 1 m 31 s";
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Tick()
    {
        Caption = "for 1 m 32 s";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Caption)));
    }
}
