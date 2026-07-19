using Avalonia.Controls;
using HexIDE.Forms.ViewModels;

namespace HexIDE.Forms.Views;

public partial class LanguageRevertGateView : UserControl
{
    public LanguageRevertGateView()
    {
        InitializeComponent();
        // Start the countdown once the dialog is actually shown.
        Loaded += (_, _) => (DataContext as LanguageRevertGateViewModel)?.Start();
    }
}
