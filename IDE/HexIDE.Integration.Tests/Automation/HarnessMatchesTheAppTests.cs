using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// The harness must theme what the application themes, or a test walks a tree the running IDE never has.
///
/// <para>
/// <c>TestApp</c> loaded SimpleTheme, the DataGrid theme and AccessKeyNames, but neither AvaloniaEdit's
/// theme nor Dock's, both of which <c>App.axaml</c> loads. An untemplated <c>TextEditor</c> has no
/// <c>TextArea</c> in its visual tree at all, so #611's test asserted a leaf class that exists only in the
/// harness — it passed <em>because</em> the two differed. Nothing else failed when the themes were added,
/// which is a weaker result than it sounds: those tests walked the thinner tree and proved less than they
/// appeared to, rather than proving something false.
/// </para>
///
/// <para>
/// These tests exist so that gap cannot reopen silently. The first pins the shape that matters to the
/// automation driver; the second pins the styles themselves, including Dock's, whose absence no current
/// test would notice.
/// </para>
/// </summary>
public class HarnessMatchesTheAppTests
{
    [AvaloniaFact]
    public void A_templated_editor_puts_its_text_area_in_the_tree_the_driver_walks()
    {
        var editor = new TextEditor { Name = "Editor" };
        var window = new Window { Content = editor, Width = 320, Height = 240 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            editor.GetVisualDescendants().OfType<AvaloniaEdit.Editing.TextArea>().Should().NotBeEmpty(
                "without AvaloniaEdit's theme the editor has no template, and every path through it names "
                + "a shape the running IDE does not produce");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_harness_loads_every_style_the_application_loads()
    {
        // App.axaml's <Styles>, as of b6a64e4: SimpleTheme, DockSimpleTheme, AvaloniaEdit, DataGrid,
        // AccessKeyNames. A style added there and not here is the gap this class is about.
        var styles = Application.Current!.Styles;

        styles.Should().ContainSingle(s => s is Avalonia.Themes.Simple.SimpleTheme);
        styles.Should().ContainSingle(s => s is Dock.Avalonia.Themes.Simple.DockSimpleTheme);

        var sources = styles.OfType<StyleInclude>().Select(s => s.Source?.ToString()).ToList();
        sources.Should().Contain("avares://AvaloniaEdit/Themes/Simple/AvaloniaEdit.xaml");
        sources.Should().Contain("avares://Avalonia.Controls.DataGrid/Themes/Simple.xaml");
        sources.Should().Contain("avares://HexIDE/Automation/AccessKeyNames.axaml");
    }

    [AvaloniaFact]
    public void The_harness_holds_the_resources_a_running_form_reads()
    {
        // A system colour is looked up on the application, so a harness without Classic.axaml drew every one DeepPink and
        // its tests compared pink with pink (#908). App.axaml also merges the IDE chrome's own dictionaries (icons, the
        // MDI windows, the resize adorner, the property box), which the harness does not; a test of those views needs them.
        var sources = Application.Current!.Resources.MergedDictionaries.OfType<ResourceInclude>()
            .Select(r => r.Source?.ToString()).ToList();

        sources.Should().Contain("avares://HexIDE/Themes/Classic.axaml");
        sources.Should().Contain("avares://HexIDE.Runtime/BuiltinControls/VBTimer.axaml");
        sources.Should().Contain("avares://HexIDE.Runtime/BuiltinControls/VBLabel.axaml");
        sources.Should().Contain("avares://HexIDE.Runtime/BuiltinControls/Resources.axaml");
    }
}
