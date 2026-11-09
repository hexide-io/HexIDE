using System;
using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using HexIDE.IDE;
using HexIDE.Themes;

namespace HexIDE.Integration.Tests.Controls;

public sealed class EditorColorOverrideTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HexIDE-editor-colors-" + Guid.NewGuid());
    public EditorColorOverrideTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    // Reflection keeps these regressions runnable against main, where the configuration seam is absent.
    private static void Configure(ISettingsService settings, IThemeService themes)
    {
        var method = typeof(SyntaxHighlightingTheme).GetMethod("Configure", BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull();
        method!.Invoke(null, [settings, themes]);
    }

    [AvaloniaFact]
    public void OverridesSurviveSameVariantThemeSwitchAndRestart()
    {
        File.WriteAllText(SettingsPath, """{"editorColorOverrides":{"Dark":{"Keyword":{"foreground":"#123456","background":"#654321"}},"Abyss":{"Keyword":{"foreground":"#ABCDEF"}}}}""");
        var settings = new SettingsService(SettingsPath);
        var themes = new ThemeService(_directory);
        var original = Application.Current!.RequestedThemeVariant;
        try
        {
            Configure(settings, themes);
            themes.Apply("Dark");
            Keyword.Foreground!.GetColor(null!).Should().Be(Color.Parse("#123456"));
            Keyword.Background!.GetColor(null!).Should().Be(Color.Parse("#654321"));
            themes.Apply("Abyss");
            Keyword.Foreground!.GetColor(null!).Should().Be(Color.Parse("#ABCDEF"));
            Keyword.Background.Should().BeNull();
            themes.Apply("Dark");
            Keyword.Foreground!.GetColor(null!).Should().Be(Color.Parse("#123456"));
            Configure(new SettingsService(SettingsPath), themes);
            Keyword.Background!.GetColor(null!).Should().Be(Color.Parse("#654321"));
        }
        finally { Configure(new SettingsService(Path.Combine(_directory, "empty.json")), themes); themes.Apply("Classic"); Application.Current.RequestedThemeVariant = original; }
    }

    [AvaloniaFact]
    public void ResetRestoresFallbackAndPersistsWithoutLosingOtherThemes()
    {
        var themes = new ThemeService(_directory);
        var settings = new SettingsService(SettingsPath);
        var original = Application.Current!.RequestedThemeVariant;
        try
        {
            Configure(settings, themes);
            themes.Apply("Dark");
            var fallback = Keyword.Foreground!.GetColor(null!);
            File.WriteAllText(SettingsPath, """{"editorColorOverrides":{"Dark":{"Keyword":{"foreground":"#123456"}},"Abyss":{"Keyword":{"foreground":"#ABCDEF"}}}}""");
            settings.Load();
            Keyword.Foreground!.GetColor(null!).Should().Be(Color.Parse("#123456"));
            var reset = settings.GetType().GetMethod("ResetEditorColors");
            reset.Should().NotBeNull();
            reset!.Invoke(settings, ["Dark", null]);
            settings.Save();
            Keyword.Foreground!.GetColor(null!).Should().Be(fallback);
            Configure(new SettingsService(SettingsPath), themes);
            Keyword.Foreground!.GetColor(null!).Should().Be(fallback);
            themes.Apply("Abyss");
            Keyword.Foreground!.GetColor(null!).Should().Be(Color.Parse("#ABCDEF"));
        }
        finally { Configure(new SettingsService(Path.Combine(_directory, "empty.json")), themes); themes.Apply("Classic"); Application.Current.RequestedThemeVariant = original; }
    }

    [AvaloniaFact]
    public void ResetOneTokenPreservesOtherTokensAndSnapshotEditsAreStaged()
    {
        File.WriteAllText(SettingsPath, """{"editorColorOverrides":{"Classic":{"Keyword":{"foreground":"#123456"},"String":{"foreground":"#ABCDEF"}}}}""");
        var settings = new SettingsService(SettingsPath);
        var themes = new ThemeService(_directory);
        var original = Application.Current!.RequestedThemeVariant;
        try
        {
            Configure(settings, themes);
            themes.Apply("Classic");
            var property = settings.GetType().GetProperty("EditorColorOverrides");
            property.Should().NotBeNull();
            var snapshot = (System.Collections.IDictionary)property!.GetValue(settings)!;
            snapshot.Clear();
            Keyword.Foreground!.GetColor(null!).Should().Be(Color.Parse("#123456"));
            var reset = settings.GetType().GetMethod("ResetEditorColors");
            reset.Should().NotBeNull();
            reset!.Invoke(settings, ["Classic", "Keyword"]);
            Keyword.Foreground!.GetColor(null!).Should().NotBe(Color.Parse("#123456"));
            SyntaxHighlightingTheme.Definition.GetNamedColor("String").Foreground!.GetColor(null!).Should().Be(Color.Parse("#ABCDEF"));
            property.SetValue(settings, snapshot);
            SyntaxHighlightingTheme.Definition.GetNamedColor("String").Foreground!.GetColor(null!).Should().NotBe(Color.Parse("#ABCDEF"));
        }
        finally { Configure(new SettingsService(Path.Combine(_directory, "empty.json")), themes); themes.Apply("Classic"); Application.Current.RequestedThemeVariant = original; }
    }

    private static AvaloniaEdit.Highlighting.HighlightingColor Keyword => SyntaxHighlightingTheme.Definition.GetNamedColor("Keyword");
}
