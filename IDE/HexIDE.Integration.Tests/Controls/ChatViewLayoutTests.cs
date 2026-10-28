using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using HexIDE.Addins;
using HexIDE.AIChat.Views;

namespace HexIDE.Integration.Tests.Controls;

public class ChatViewLayoutTests
{
    [AvaloniaTheory]
    [InlineData(212)]
    [InlineData(254)]
    [InlineData(280)]
    public void Input_row_fits_the_pane(double width)
    {
        var settingsDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var view = new ChatView(Substitute.For<IHexIdeHost>(), settingsDir);
        var window = new Window { Width = width, Height = 600, Content = view };
        try
        {
            window.Show();
            window.UpdateLayout();

            view.Bounds.Width.Should().BeLessThanOrEqualTo(width);
            var input = view.GetVisualDescendants().OfType<TextBox>().Single();
            input.Bounds.Width.Should().BeGreaterThan(0);
            foreach (var control in view.GetVisualDescendants().OfType<Button>().Cast<Control>().Append(input))
            {
                var origin = control.TranslatePoint(default, view)!.Value;
                origin.X.Should().BeGreaterThanOrEqualTo(0);
                (origin.X + control.Bounds.Width).Should().BeLessThanOrEqualTo(width);
            }
            view.GetVisualDescendants().OfType<Button>().Select(b => b.Content)
                .Should().Contain(new object[] { "Send", "New" });
        }
        finally
        {
            window.Close();
            if (Directory.Exists(settingsDir)) Directory.Delete(settingsDir, true);
        }
    }
}
