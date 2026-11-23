using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

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

    // #922 deliberately implements runtime AutoSize only; design time has not been measured.
    [AvaloniaFact]
    public void Properties_caption_edit_keeps_saved_size()
    {
        var label = new ComponentInstance(LabelComponentClass.Instance, "Label1");
        label.SetProperty(VBProperties.AutoSizeProperty, true);
        label.SetProperty(VBProperties.CaptionProperty, "Hello");
        label.SetProperty(VBProperties.WidthProperty, 200.0);
        label.SetProperty(VBProperties.HeightProperty, 60.0);
        var control = (VBLabel)LabelComponentClass.Instance.Instantiate(label);
        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
        form.UpdateComponents([..form.Components, label]);
        using var designer = new FormEditViewModel(null!, new EventBus(), null!, null!, null!, null!,
            Substitute.For<ILocalizationService>()).Initialize(form);
        designer.PushSetPropertyCommand(label, VBProperties.CaptionProperty, "Hello", "A longer caption");
        label.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(200);
        label.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(60);
        // The preview's own Text handler must not resize it either.
        control.Width = 200;
        control.Height = 60;
        control.Text = "A longer caption";
        control.Width.Should().Be(200);
        control.Height.Should().Be(60);
    }
}
