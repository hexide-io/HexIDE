using Avalonia.Controls;
using Avalonia.Interactivity;
namespace HexIDE.Forms.Views;
public partial class WorkspaceArtifactConsentView : UserControl
{
    public WorkspaceArtifactConsentView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        DenyButton.Focus();
    }
}
