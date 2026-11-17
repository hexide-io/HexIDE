using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Controls;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Tools.TranslationEditor;

namespace HexIDE.Integration.Tests.Views;

public class CaptionAccessKeyTests
{
    private static Dictionary<string, string> Strings(string id)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://HexIDE/Localization/Packs/{id}.json"));
        return JsonSerializer.Deserialize(stream, LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;
    }

    private static Window Open(Control view, string id = "en")
    {
        var app = new App();
        app.LoadMarkup();
        var window = new Window { Content = view, Width = 800, Height = 600 };
        var resources = app.Resources;
        app.Resources = new ResourceDictionary();
        window.Resources.MergedDictionaries.Add(resources);
        foreach (var (key, value) in Strings("en"))
            window.Resources[key] = value;
        foreach (var (key, value) in Strings(id))
            window.Resources[key] = value;
        while (app.Styles.Count > 0)
        {
            var style = app.Styles[0];
            app.Styles.RemoveAt(0);
            window.Styles.Add(style);
        }
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    [AvaloniaFact]
    public void NewProject_captions_render_without_markers_and_focus_their_boxes()
    {
        var view = new NewProjectView
        {
            DataContext = new NewProjectViewModel(Substitute.For<IWindowManager>(),
                Substitute.For<IRecentProjectsService>(), Substitute.For<ISettingsService>(),
                new ProfileService(), Substitute.For<ILocalizationService>()),
        };
        var window = Open(view);
        try
        {
            var tabs = view.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var labels = view.GetVisualDescendants().OfType<Label>().Where(l => l.Target != null).ToArray();
            labels.Should().HaveCount(3);
            foreach (var label in labels)
            {
                var caption = label.GetVisualDescendants().OfType<AccessText>().Single();
                caption.AccessKey.Should().NotBeNullOrEmpty();
                tabs.Focus();
                UiAutomationDriver.PressKey(tabs, caption.AccessKey.ToString(), "Alt").Success.Should().BeTrue();
                Dispatcher.UIThread.RunJobs();
                label.Target!.IsKeyboardFocusWithin.Should().BeTrue(caption.Text);
            }
            VisibleText(window).Should().OnlyContain(text => !text.Contains('_'));
        }
        finally { window.Close(); }
    }

    private static IEnumerable<string> VisibleText(Control root) => root.GetVisualDescendants()
        .OfType<TextBlock>().Where(t => t.IsEffectivelyVisible)
        .Select(t => string.Concat(t.TextLayout.TextLines.SelectMany(line => line.TextRuns)
            .Select(run => run.Text.ToString())));

    [AvaloniaFact]
    public void NewProject_keys_are_unique_in_every_shipped_pack()
    {
        using var scope = new AwesomeAssertions.Execution.AssertionScope();
        var canonical = Strings("en");
        // Buttons repeat on mutually exclusive tabs; count each resource once, including tab headers.
        var keys = new[] { "Label.LookIn", "Label.FileName", "Label.FilesOfType", "Button.Open",
            "Button.Cancel", "Checkbox.DontShowOnStartup", "Tab.New", "Tab.Existing", "Tab.Recent" };
        foreach (var uri in AssetLoader.GetAssets(new Uri("avares://HexIDE/Localization/Packs/"), null))
        {
            var id = System.IO.Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            var strings = new Dictionary<string, string>(canonical);
            var parent = id.Split('-')[0];
            if (id != parent && LanguageManifest.Packs.Any(pack => pack.Id == parent))
                foreach (var pair in Strings(parent)) strings[pair.Key] = pair.Value;
            foreach (var pair in Strings(id)) strings[pair.Key] = pair.Value;
            var captions = keys.Select(k => strings["Str.Dialog.NewProject." + k]).ToArray();
            var letters = captions.Select(c => new AccessText { Text = c }.AccessKey)
                .Where(c => !string.IsNullOrEmpty(c)).Select(c => c!.ToUpperInvariant()).ToArray();
            letters.Should().OnlyHaveUniqueItems(id);
            foreach (var key in new[] { "FromLabel", "RegionLabel", "ToLabel" })
                strings["Str.TranslationEditor." + key].Should().NotContain("_", id);
        }
    }

    // VB6 SP6 keys (2026-11-16) and HexIDE’s combo/radio adaptations: https://github.com/hexide-io/HexIDE/issues/2738.
    [AvaloniaFact]
    public void FindReplace_English_keys_match_the_measurement()
    {
        var strings = Strings("en");
        var expected = new Dictionary<string, string>
        {
            ["Label.FindWhat"] = "F", ["Label.ReplaceWith"] = "W",
            ["Button.FindNext"] = "N", ["Button.Replace"] = "R", ["Button.ReplaceAll"] = "A",
            ["Option.MatchCase"] = "S", ["Option.WholeWordOnly"] = "O",
            ["Option.UsePatternMatching"] = "U", ["Label.Direction"] = "D",
            ["Label.Search"] = "E", ["Direction.Up"] = "", ["Direction.Down"] = "",
            ["Button.Cancel"] = "",
        };
        foreach (var (key, letter) in expected)
            (new AccessText { Text = strings["Str.Dialog.FindReplace." + key] }.AccessKey ?? "")
                .ToUpperInvariant().Should().Be(letter, key);
    }

    [AvaloniaFact]
    public void FindReplace_keys_are_unique_in_every_shipped_pack()
    {
        using var scope = new AwesomeAssertions.Execution.AssertionScope();
        var canonical = Strings("en");
        foreach (var uri in AssetLoader.GetAssets(new Uri("avares://HexIDE/Localization/Packs/"), null))
        {
            var id = System.IO.Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            var strings = new Dictionary<string, string>(canonical);
            var parent = id.Split('-')[0];
            if (id != parent && LanguageManifest.Packs.Any(pack => pack.Id == parent))
                foreach (var pair in Strings(parent)) strings[pair.Key] = pair.Value;
            foreach (var pair in Strings(id)) strings[pair.Key] = pair.Value;
            foreach (var replace in new[] { false, true })
            {
                var letters = strings.Where(p => p.Key.StartsWith("Str.Dialog.FindReplace.", StringComparison.Ordinal)
                        && (replace || p.Key is not ("Str.Dialog.FindReplace.Label.ReplaceWith"
                            or "Str.Dialog.FindReplace.Button.Replace" or "Str.Dialog.FindReplace.Button.ReplaceAll")))
                    .Select(p => new AccessText { Text = p.Value }.AccessKey)
                    .Where(k => !string.IsNullOrEmpty(k)).Select(k => k!.ToUpperInvariant());
                letters.Should().OnlyHaveUniqueItems($"{id}, replace={replace}");
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FindReplace_captions_render_without_markers_and_focus_their_targets(bool replace)
    {
        var view = new FindReplaceView
        {
            DataContext = new FindReplaceViewModel(Substitute.For<IWindowManager>(),
                Substitute.For<IDocumentDockService>(), Substitute.For<ILocalizationService>())
                { ShowReplace = replace },
        };
        var window = Open(view);
        try
        {
            var labels = view.GetVisualDescendants().OfType<Label>()
                .Where(l => l.IsEffectivelyVisible && l.Target != null).ToArray();
            labels.Should().HaveCount(replace ? 4 : 3);
            foreach (var label in labels)
            {
                var caption = label.GetVisualDescendants().OfType<AccessText>().Single();
                caption.AccessKey.Should().NotBeNullOrEmpty();
                var other = view.GetVisualDescendants().OfType<CheckBox>().First();
                other.Focus();
                UiAutomationDriver.PressKey(other, caption.AccessKey!, "Alt").Success.Should().BeTrue();
                Dispatcher.UIThread.RunJobs();
                label.Target!.IsKeyboardFocusWithin.Should().BeTrue($"{caption.Text}; focused: {window.FocusManager?.GetFocusedElement()}");
            }
            VisibleText(window).Should().OnlyContain(text => !text.Contains('_'));
        }
        finally { window.Close(); }
    }

    // Direction's key enters its option group where Tab would: at the selected option, not on the group's panel (#4142).
    [AvaloniaTheory]
    [InlineData(FindDirection.Up)]
    [InlineData(FindDirection.Down)]
    public void FindReplace_direction_key_focuses_the_selected_option(FindDirection direction)
    {
        var view = new FindReplaceView
        {
            DataContext = new FindReplaceViewModel(Substitute.For<IWindowManager>(),
                Substitute.For<IDocumentDockService>(), Substitute.For<ILocalizationService>())
                { Direction = direction },
        };
        var window = Open(view);
        try
        {
            var group = view.FindControl<StackPanel>("DirectionGroup")!;
            var label = view.GetVisualDescendants().OfType<Label>().Single(l => ReferenceEquals(l.Target, group));
            var other = view.GetVisualDescendants().OfType<CheckBox>().First();
            other.Focus();
            UiAutomationDriver.PressKey(other, label.GetVisualDescendants().OfType<AccessText>().Single().AccessKey!, "Alt")
                .Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            window.FocusManager!.GetFocusedElement().Should().BeOfType<IdeRadioButton>()
                .Which.IsChecked.Should().BeTrue();
            view.FindControl<StackPanel>("DirectionGroup")!.Children.OfType<RadioButton>()
                .Single(option => option.IsChecked == true).IsFocused.Should().BeTrue($"{direction} is the selected option");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ja")]
    public void TranslationEditor_captions_have_no_access_keys(string id)
    {
        var view = new TranslationEditorView();
        var window = Open(view, id);
        try
        {
            var bar = view.GetVisualDescendants().OfType<Grid>()
                .Single(g => g.ColumnDefinitions.Count == 9);
            var captions = bar.Children.OfType<TextBlock>().ToArray();
            captions.Should().HaveCount(4);
            captions.Should().OnlyContain(t => t.GetType() == typeof(TextBlock));
            captions.Select(t => t.Text).Should().NotContainNulls()
                .And.OnlyContain(text => !text!.Contains('_'));
            bar.GetVisualDescendants().OfType<AccessText>().Should().BeEmpty();
            bar.Children.OfType<ComboBox>().Should().HaveCount(4)
                .And.OnlyContain(box => box.Focusable);
        }
        finally { window.Close(); }
    }
}
