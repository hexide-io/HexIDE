using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Markup.Xaml;

namespace HexIDE.Tools.LanguageServers;

public partial class LanguageServerEditorView : UserControl
{
    public LanguageServerEditorView()
    {
        AvaloniaXamlLoader.Load(this);
        Loaded += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!IsLoaded) return;
            var first = this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            first?.Focus();
            first?.BringIntoView();
        }, DispatcherPriority.Loaded);
    }
}
