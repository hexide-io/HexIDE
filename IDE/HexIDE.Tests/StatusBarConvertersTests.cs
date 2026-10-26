using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;
using HexIDE.Localization;
using HexIDE.Utils;

namespace HexIDE.Tests;

public class StatusBarConvertersTests
{
    [AvaloniaFact]
    public void Position_FollowsLanguageSwitchWithoutCaretMovement()
    {
        var loc = new LocalizationService();
        var text = new TextBlock();
        text.Bind(TextBlock.TextProperty, new MultiBinding
        {
            Converter = StatusBarConverters.LinColConverter,
            Bindings =
            {
                new Binding { Source = 12 }, new Binding { Source = 34 },
                new DynamicResourceExtension("Str.StatusBar.LineColumn")
            }
        });
        var window = new Window { Content = text };
        window.Show();
        try
        {
            loc.Apply("en");
            text.Text.Should().Be("Ln 12, Col 34");
            loc.Apply("pseudo");
            text.Text.Should().StartWith("⟦").And.Contain("12").And.Contain("34");
            loc.Apply("en");
            text.Text.Should().Be("Ln 12, Col 34");
        }
        finally
        {
            window.Close();
            loc.Apply("en");
        }
    }

    [AvaloniaTheory]
    [InlineData(true, "INS")]
    [InlineData(false, "OVR")]
    public void Mode_FollowsLanguageSwitch(bool insert, string english)
    {
        var loc = new LocalizationService();
        var text = new TextBlock();
        text.Bind(TextBlock.TextProperty, new MultiBinding
        {
            Converter = StatusBarConverters.InsertModeConverter,
            Bindings =
            {
                new Binding { Source = insert },
                new DynamicResourceExtension("Str.StatusBar.Insert"),
                new DynamicResourceExtension("Str.StatusBar.Overtype")
            }
        });
        var window = new Window { Content = text };
        window.Show();
        try
        {
            loc.Apply("en");
            text.Text.Should().Be(english);
            loc.Apply("pseudo");
            text.Text.Should().StartWith("⟦");
            loc.Apply("en");
            text.Text.Should().Be(english);
        }
        finally
        {
            window.Close();
            loc.Apply("en");
        }
    }
}
