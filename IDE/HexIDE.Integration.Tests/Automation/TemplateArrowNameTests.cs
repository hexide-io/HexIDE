using System.Text.Json;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Automation;

public class TemplateArrowNameTests
{
    private static Window Show(string language)
    {
        var window = ToolbarAutomationNameTests.ShowMainView();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "IDE", "HexIDE", "Localization", "Packs")))
            root = root.Parent;
        using var pack = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName,
            "IDE", "HexIDE", "Localization", "Packs", language + ".json")));
        foreach (var entry in pack.RootElement.GetProperty("strings").EnumerateObject())
            window.Resources[entry.Name] = entry.Value.GetString();
        return window;
    }

    [AvaloniaFact]
    public void Dialog_note_excludes_repeat_and_template_buttons()
    {
        var part = new Button { Content = "Template action" };
        var host = new ContentControl
        {
            Template = new Avalonia.Controls.Templates.FuncControlTemplate<ContentControl>((_, _) => part),
        };
        var window = Show("en");
        window.Title = "Dialog";
        window.Content = new StackPanel { Children =
        {
            host, new RepeatButton { Content = "Repeat action" }, new Button { Content = "OK" },
        } };
        try
        {
            Dispatcher.UIThread.RunJobs();
            part.TemplatedParent.Should().BeSameAs(host);
            OpenDialogs.DescriptionOf(window).Should().Be("'Dialog' (OK)");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en", "Line up", "Line down")]
    [InlineData("de", "Zeile nach oben", "Zeile nach unten")]
    public void Scrolling_context_menu_names_its_arrows(string language, string up, string down)
    {
        var window = Show(language);
        var anchor = new Button { Content = "Menu" };
        var menu = new ContextMenu { MaxHeight = 120 };
        for (var i = 0; i < 40; i++)
            menu.Items.Add(new MenuItem { Header = "Item " + i });
        anchor.ContextMenu = menu;
        window.Content = anchor;
        try
        {
            menu.Open(anchor);
            Dispatcher.UIThread.RunJobs();
            var buttons = menu.GetVisualDescendants().OfType<RepeatButton>()
                .Where(b => b.TemplatedParent is ScrollViewer).ToArray();
            buttons.Should().HaveCount(2);
            buttons.Should().Contain(b => b.IsEffectivelyVisible);
            buttons.OrderBy(b => DockPanel.GetDock(b) == Avalonia.Controls.Dock.Top ? 0 : 1)
                .Select(b => ControlAutomationPeer.CreatePeerForElement(b).GetName()).Should().Equal(up, down);
        }
        finally { menu.Close(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en", "Increase", "Decrease")]
    [InlineData("de", "Erhöhen", "Verringern")]
    public void Project_properties_names_its_spinner_arrows(string language, string increase, string decrease)
    {
        var window = Show(language);
        window.Content = new ProjectPropertiesView
        {
            DataContext = new ProjectPropertiesViewModel(new ProjectDefinition(VBProjectType.EXE, "Ledger"), _ => null),
        };
        try
        {
            Dispatcher.UIThread.RunJobs();
            var buttons = window.GetVisualDescendants().OfType<RepeatButton>()
                .Where(b => b.TemplatedParent is ButtonSpinner).ToArray();
            buttons.Should().HaveCount(2);
            buttons.OrderBy(b => b.Name == "PART_IncreaseButton" ? 0 : 1)
                .Select(b => ControlAutomationPeer.CreatePeerForElement(b).GetName()).Should().Equal(increase, decrease);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en", "OK/Cancel/Help")]
    [InlineData("de", "OK/Abbrechen/Hilfe")]
    public void Project_properties_note_lists_only_answers(string language, string answers)
    {
        var window = Show(language);
        window.Title = "Project Properties";
        window.Content = new ProjectPropertiesView
        {
            DataContext = new ProjectPropertiesViewModel(new ProjectDefinition(VBProjectType.EXE, "Ledger"), _ => null),
        };
        try
        {
            Dispatcher.UIThread.RunJobs();
            OpenDialogs.DescriptionOf(window).Should().Be($"'Project Properties' ({answers})");
        }
        finally { window.Close(); }
    }
}
