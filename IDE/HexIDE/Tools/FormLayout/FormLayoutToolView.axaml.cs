using Avalonia;
using Avalonia.Controls;

namespace HexIDE.Tools;

public partial class FormLayoutToolView : UserControl
{
    public FormLayoutToolView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is not FormLayoutToolViewModel vm)
            return;

        var primary = TopLevel.GetTopLevel(this)?.Screens?.Primary;
        if (primary is not null)
            vm.SetScreenBounds(primary.Bounds.Width / primary.Scaling,
                               primary.Bounds.Height / primary.Scaling);

        ScreenCanvas.SizeChanged += (_, args) =>
            vm.SetCanvasSize(args.NewSize.Width, args.NewSize.Height);
    }
}
