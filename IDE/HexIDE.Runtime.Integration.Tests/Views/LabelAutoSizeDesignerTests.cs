using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Integration.Tests.Views;

public class LabelAutoSizeDesignerTests
{
    // #921, oracle PR #5225: fixed-size Labels wrap in the designer too.
    [AvaloniaFact]
    public void Fixed_size_designer_preview_wraps_with_WordWrap_off()
    {
        var label = new ComponentInstance(LabelComponentClass.Instance, "Label2");
        label.SetProperty(VBProperties.CaptionProperty, "This caption is wider than its label");
        label.SetProperty(VBProperties.WidthProperty, 100.0);
        label.SetProperty(VBProperties.HeightProperty, 41.0);
        label.SetProperty(VBProperties.BorderStyleProperty, HexIDE.Runtime.BuiltinTypes.VBBorder.FixedSingle);
        label.SetProperty(VBProperties.AutoSizeProperty, false);
        label.SetProperty(VBProperties.WordWrapProperty, false);
        var control = (VBLabel)LabelComponentClass.Instance.Instantiate(label);
        var window = new Window();
        window.Content = control;
        window.Show();
        window.UpdateLayout();
        control.GetVisualDescendants().OfType<TextBlock>().Single().TextWrapping.Should().Be(TextWrapping.Wrap);
        window.Close();
    }
}
