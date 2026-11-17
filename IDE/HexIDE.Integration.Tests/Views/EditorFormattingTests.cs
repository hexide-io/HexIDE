using System;
using System.Collections;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using HexIDE.Addins;
using HexIDE.Bookmarks;
using HexIDE.Projects;
using HexIDE.Lsp;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Forms.Views;
using Avalonia.Controls;
using Avalonia.Threading;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;
using HexIDE.Keymaps;
using HexIDE.Localization;
using HexIDE.Themes;

namespace HexIDE.Integration.Tests.Views;

public sealed class EditorFormattingTests
{
    // Reflection lets the same tests run against the pre-page branch, rather than fail to compile there.
    private static object Keyword(object page)
    {
        var property = page.GetType().GetProperty("Tokens");
        property.Should().NotBeNull("Formatting must expose the highlighter's tokens");
        return ((IEnumerable)property!.GetValue(page)!).Cast<object>()
            .Single(t => (string)t.GetType().GetProperty("Name")!.GetValue(t)! == "Keyword");
    }

    private static void Set(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    private static object? Get(object target, string name) => target.GetType().GetProperty(name)!.GetValue(target);

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfirmAppliesAndCancelDiscardsColorsAndFont(bool confirm)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-format-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var themes = new ThemeService(directory);
        themes.Apply("Classic");
        var path = Path.Combine(directory, "settings.json");
        var settings = new SettingsService(path);
        settings.EditorColorOverrides = new() { ["Classic"] = new() { ["String"] = new("#135790") } };
        settings.Save();
        var originalChoices = settings.EditorColorOverrides;
        var originalFile = File.ReadAllText(path);
        SyntaxHighlightingTheme.Configure(settings, themes);
        var before = SyntaxHighlightingTheme.Definition.GetNamedColor("Keyword").Foreground!.GetColor(null!);
        var shell = Shell(settings, themes);
        using var editorVm = Editor(settings);
        var editorView = new CodeEditorView { DataContext = editorVm };
        var editorWindow = new Window { Content = editorView, Width = 800, Height = 600 };
        editorWindow.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = editorView.FindControl<AvaloniaEdit.TextEditor>("TextEditor")!;
        try
        {
            var page = shell.RootNodes.SelectMany(n => n.Children).Select(n => n.Page).OfType<EditorFormattingPageViewModel>().Single();
            var keyword = Keyword(page);
            Set(keyword, "Foreground", "#123456");
            Set(keyword, "Background", "#654321");
            Set(page, "FontFamily", "Monospace");
            Set(page, "FontSize", 19d);
            settings.EditorColorOverrides.Should().BeEquivalentTo(originalChoices);
            editor.FontSize.Should().Be(13);
            SyntaxHighlightingTheme.Definition.GetNamedColor("Keyword").Foreground!.GetColor(null!).Should().Be(before);
            var preview = (AvaloniaEdit.Highlighting.IHighlightingDefinition)Get(page, "PreviewDefinition")!;
            preview.GetNamedColor("Keyword").Foreground!.GetColor(null!).Should().Be(Color.Parse("#123456"));
            if (confirm) shell.OkCommand.Execute(null);
            else shell.CancelCommand.Execute(null);
            editor.FontSize.Should().Be(confirm ? 19 : 13);
            editor.FontFamily.Should().Be(new FontFamily(confirm ? "Monospace" : "Courier New,Monospace"));
            if (confirm)
            {
                settings.EditorColorOverrides["Classic"]["Keyword"].Should().Be(new EditorTokenColors("#123456", "#654321"));
                SyntaxHighlightingTheme.Definition.GetNamedColor("Keyword").Foreground!.GetColor(null!).Should().Be(Color.Parse("#123456"));
                var reloaded = new SettingsService(path);
                Get(reloaded, "EditorFontFamily").Should().Be("Monospace");
                Get(reloaded, "EditorFontSize").Should().Be(19d);
                reloaded.EditorColorOverrides.Should().BeEquivalentTo(settings.EditorColorOverrides);
            }
            else
            {
                settings.EditorColorOverrides.Should().BeEquivalentTo(originalChoices);
                Get(settings, "EditorFontSize").Should().Be(13d);
                File.ReadAllText(path).Should().Be(originalFile);
                SyntaxHighlightingTheme.Definition.GetNamedColor("Keyword").Foreground!.GetColor(null!).Should().Be(before);
            }
        }
        finally
        {
            editorWindow.Close();
            shell.CancelCommand.Execute(null);
            SyntaxHighlightingTheme.Configure(new SettingsService(Path.Combine(directory, "empty.json")), themes);
            themes.Apply("Classic");
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public void PageReadsPerThemeOverridesAndResetRestoresFallbackOnlyForCurrentTheme()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-format-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var themes = new ThemeService(directory);
        themes.Apply("Classic");
        var settings = new SettingsService(Path.Combine(directory, "settings.json"));
        settings.EditorColorOverrides = new()
        {
            ["Classic"] = new() { ["Keyword"] = new("#123456") },
            ["Dark"] = new() { ["Keyword"] = new("#ABCDEF") }
        };
        var shell = Shell(settings, themes);
        try
        {
            var page = shell.RootNodes.SelectMany(n => n.Children).Select(n => n.Page).OfType<EditorFormattingPageViewModel>().Single();
            Get(Keyword(page), "Foreground").Should().Be("#123456");
            Set(Keyword(page), "Background", "#654321");
            themes.Apply("Dark");
            Get(Keyword(page), "Foreground").Should().Be("#ABCDEF");
            Get(Keyword(page), "Background").Should().BeNull();
            ((System.Windows.Input.ICommand)Get(Keyword(page), "ResetCommand")!).Execute(null);
            Get(Keyword(page), "Foreground").Should().BeNull();
            var preview = (AvaloniaEdit.Highlighting.IHighlightingDefinition)Get(page, "PreviewDefinition")!;
            preview.GetNamedColor("Keyword").Foreground!.GetColor(null!).Should().Be(Color.Parse((string)Get(Keyword(page), "ThemeForeground")!));
            themes.Apply("Classic");
            Get(Keyword(page), "Background").Should().Be("#654321");
            ((System.Windows.Input.ICommand)Get(page, "ResetColorsCommand")!).Execute(null);
            Get(Keyword(page), "Foreground").Should().BeNull();
            settings.EditorColorOverrides["Classic"]["Keyword"].Foreground.Should().Be("#123456", "reset is staged");
            shell.OkCommand.Execute(null);
            settings.EditorColorOverrides.Should().BeEmpty();
        }
        finally { shell.CancelCommand.Execute(null); themes.Apply("Classic"); Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public void FormattingViewBindsPickersAndFontToAnIndependentPreview()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.ActiveTheme.Returns("Classic");
        settings.TabWidth.Returns(4);
        var shell = Shell(settings, Substitute.For<IThemeService>());
        var page = shell.RootNodes.SelectMany(n => n.Children).Select(n => n.Page).OfType<EditorFormattingPageViewModel>().Single();
        Set(page, "SelectedToken", Keyword(page));
        var view = new HexIDE.Forms.Views.Options.EditorFormattingPageView { DataContext = page };
        view.Resources["Str.Automation.ChooseColor"] = "Choisir une couleur";
        var window = new Avalonia.Controls.Window
        {
            Content = view,
            Width = 500, Height = 700
        };
        try
        {
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var pickers = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.ColorPicker>().ToArray();
            pickers.Should().HaveCount(2);
            var unnamedButtons = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Button>()
                .Where(button => Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(button).GetName() == "Avalonia.Controls.Panel")
                .Select(button => $"Name={button.Name}, parent={button.TemplatedParent?.GetType().Name}, attached={Avalonia.Automation.AutomationProperties.GetName(button)}");
            unnamedButtons.Should().BeEmpty("picker buttons need localized accessible names");
            pickers[0].Color = Color.Parse("#123456");
            Color.Parse((string)Get(Keyword(page), "Foreground")!).Should().Be(Color.Parse("#123456"));
            Set(page, "FontSize", 20d);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var preview = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<AvaloniaEdit.TextEditor>().Single();
            preview.FontSize.Should().Be(20);
            var highlighter = new AvaloniaEdit.Highlighting.DocumentHighlighter(preview.Document, preview.SyntaxHighlighting);
            using (highlighter)
            {
                var shown = Enumerable.Range(1, preview.Document.LineCount)
                    .SelectMany(line => highlighter.HighlightLine(line).Sections).Select(section => section.Color.Name);
                shown.Should().Contain(((IEnumerable)Get(page, "Tokens")!).Cast<object>()
                    .Select(token => (string)Get(token, "Name")!).Except(["Identifier", "Operator"]));
            }
            var samples = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
                .OfType<ItemsControl>().Single(control => control.Name == "TokenPreview");
            var labels = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(samples)
                .OfType<TextBlock>().Where(label => label.DataContext?.GetType().Name == "EditorTokenViewModel").ToArray();
            labels.Should().HaveCount(((IEnumerable)Get(page, "Tokens")!).Cast<object>().Count());
            foreach (var label in labels)
            {
                ((SolidColorBrush)label.Foreground!).Color.Should().Be((Color)Get(label.DataContext!, "ForegroundColor")!);
                label.FontSize.Should().Be(20);
            }
            preview.SyntaxHighlighting.GetNamedColor("Keyword").Foreground!.GetColor(null!).Should().Be(Color.Parse("#123456"));
            preview.SyntaxHighlighting.Should().NotBeSameAs(SyntaxHighlightingTheme.Definition);
        }
        finally { window.Close(); shell.CancelCommand.Execute(null); }
    }

    private static CodeEditorViewModel Editor(ISettingsService settings)
    {
        var vm = new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), new EventBus(), Substitute.For<ILspClient>(), settings,
            Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(),
            Substitute.For<HexIDE.Debugging.IBreakpointService>(), Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(), Substitute.For<ILocalizationService>());
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Sub Main()\r\nEnd Sub\r\n");
        return vm.Initialize(module);
    }

    private static OptionsViewModel Shell(ISettingsService settings, IThemeService themes)
    {
        var registry = Substitute.For<IAddinRegistry>();
        registry.GetAll().Returns([]);
        var language = Substitute.For<ILanguageSwitchService>();
        language.ActiveLanguage.Returns("en");
        language.AvailableLanguages.Returns([new LanguageInfo("en", "English", "ltr")]);
        language.RegionsFor(Arg.Any<string>()).Returns([]);
        var keymaps = Substitute.For<IKeymapService>();
        keymaps.AvailableKeymaps.Returns([]);
        return new OptionsViewModel(settings, themes, keymaps, language, registry, new AddinOptionsService(),
            Substitute.For<IDeveloperModeService>(), Substitute.For<ILocalizationService>(), Substitute.For<IEventBus>());
    }
}
