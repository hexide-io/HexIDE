using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Classic.CommonControls;
using HexIDE.Controls;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Views;

public class FormLayoutThemeTests
{
    [AvaloniaTheory]
    [InlineData("face")]
    [InlineData("text")]
    [InlineData("title")]
    public void Thumbnail_UsesDynamicThemeBrush(string part)
    {
        var view = new FormLayoutToolView();
        view.GetLogicalDescendants().OfType<ProportionPanel>().Single().Proportion = 4.0 / 3;
        var window = new Window { Content = view, Width = 320, Height = 240 };
        var key = part switch
        {
            "face" => SystemColors.WindowBrushKey,
            "text" => SystemColors.WindowTextBrushKey,
            _ => SystemColors.ControlDarkBrushKey
        };
        var initial = new SolidColorBrush(Color.Parse("#252526"));
        window.Resources[key] = initial;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var label = view.GetVisualDescendants().OfType<TextBlock>().Single();
            var face = (Border)label.Parent!;
            var title = ((DockPanel)face.Parent!).Children.OfType<Border>().First();
            IBrush? ReadBrush() => part switch
            {
                "face" => face.Background,
                "text" => label.Foreground,
                _ => title.Background
            };

            ReadBrush().Should().BeSameAs(initial);
            var replacement = new SolidColorBrush(Color.Parse("#D4D4D4"));
            window.Resources[key] = replacement;
            Dispatcher.UIThread.RunJobs();
            ReadBrush().Should().BeSameAs(replacement);
        }
        finally
        {
            window.Close();
        }
    }
}
