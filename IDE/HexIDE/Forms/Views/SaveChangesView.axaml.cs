using Avalonia.Controls;
using HexIDE.Dialogs;

namespace HexIDE.Forms.Views;

public partial class SaveChangesView : UserControl
{
    public SaveChangesView()
    {
        InitializeComponent();
        new KeyUpCommit(this);
    }
}