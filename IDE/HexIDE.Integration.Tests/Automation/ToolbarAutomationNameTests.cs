using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Classic.Avalonia.Theme;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// Guards hexide-io/HexIDE#526 for the toolbars: every toolbar button has an automation name a caller can use.
/// </summary>
/// <remarks>
/// <para>
/// A toolbar button shows only an icon, so without a name of its own its automation peer falls back to the
/// icon's type: every one was <c>Avalonia.Controls.Shapes.Path</c>. The fix names each button after its
/// tooltip, which makes a button with no tooltip the way this regresses, and nineteen of them had none.
/// Only a live dump would notice the next one; this renders the real <see cref="MainView"/> instead.
/// </para>
/// <para>
/// The list families #526 also named (designer controls, project templates, property rows) need their view
/// models to render, which this setup does not provide. They are left to the broader guard proposed in
/// hexide-io/HexIDE#542.
/// </para>
/// </remarks>
public class ToolbarAutomationNameTests
{
    /// <summary>A dotted identifier: what a peer reports when it has fallen back to the content's type.</summary>
    private static readonly Regex TypeName = new(@"^[A-Za-z_][\w]*(\.[A-Za-z_][\w`]*)+$");

    private static readonly string[] Toolbars = ["StandardToolbar", "EditToolbar", "DebugToolbar", "FormEditorToolbar"];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                                && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;
        return dir!.FullName;
    }

    /// <summary>
    /// The main view with the resources its markup names: the icon geometries and IDE colours it takes from
    /// <c>App.axaml</c>, and the English strings its tooltips resolve, which the IDE loads from the same pack.
    /// </summary>
    private static Window ShowMainView()
    {
        var window = new Window { Width = 1600, Height = 900 };
        foreach (var source in new[] { "Themes/IconGeometry.axaml", "Themes/Classic.axaml" })
            window.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://HexIDE/"))
            {
                Source = new Uri("avares://HexIDE/" + source),
            });

        var pack = Path.Combine(RepoRoot(), "IDE", "HexIDE", "Localization", "Packs", "en.json");
        using var json = JsonDocument.Parse(File.ReadAllText(pack));
        foreach (var entry in json.RootElement.GetProperty("strings").EnumerateObject())
            window.Resources[entry.Name] = entry.Value.GetString();

        window.Content = new MainView();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Every_toolbar_button_is_named_for_what_it_does()
    {
        var window = ShowMainView();
        try
        {
            var decorators = window.GetVisualDescendants().OfType<ClassicBorderDecorator>()
                .Where(d => Toolbars.Contains(Avalonia.Automation.AutomationProperties.GetAutomationId(d)))
                .ToList();
            decorators.Select(d => Avalonia.Automation.AutomationProperties.GetAutomationId(d))
                .Should().BeEquivalentTo(Toolbars, "the guard has to reach every toolbar to guard it");

            var bad = new List<string>();
            var count = 0;
            foreach (var decorator in decorators)
            foreach (var button in decorator.GetVisualDescendants().OfType<Button>())
            {
                count++;
                var name = ControlAutomationPeer.CreatePeerForElement(button).GetName();
                var id = Avalonia.Automation.AutomationProperties.GetAutomationId(button);
                if (string.IsNullOrWhiteSpace(name) || TypeName.IsMatch(name))
                    bad.Add($"{id}: '{name}'");
            }

            count.Should().BeGreaterThan(50, "four toolbars hold over fifty buttons, so fewer means the walk missed some");
            string.Join(Environment.NewLine, bad).Should().BeEmpty(
                "a toolbar button without a tooltip is named after its icon's type, which no caller can use");
        }
        finally
        {
            window.Close();
        }
    }
}
