using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
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
