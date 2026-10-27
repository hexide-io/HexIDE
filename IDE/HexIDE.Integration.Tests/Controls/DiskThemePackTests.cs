using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using HexIDE.Themes;

namespace HexIDE.Integration.Tests.Controls;

public sealed class DiskThemePackTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HexIDE-theme-" + Guid.NewGuid());

    public DiskThemePackTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, true);

    private void Write(string id, string json) => File.WriteAllText(Path.Combine(_directory, id + ".json"), json);

    [AvaloniaFact]
    public void DiscoversPacksByFilenameAlongsideShippedThemes()
    {
        Write("Zeta", """{"name":"Display name","colors":{}}""");
        Write("Alpha", "{}");
        File.WriteAllText(Path.Combine(_directory, "ignored.txt"), "{}");
        new ThemeService(_directory).AvailableThemes.Should().Equal("Classic", "Dark", "Abyss", "Alpha", "Zeta");
    }

    [AvaloniaFact]
    public void AppliesDiskColoursAndVariantAndReloadsEdits()
    {
        Write("Custom", """{"name":"Custom","themeVariant":"Dark","colors":{"ReadOnlyText":"#123456","Unknown":"invalid"}}""");
        var service = new ThemeService(_directory);
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        try
        {
            service.Apply("Custom");
            service.ActiveTheme.Should().Be("Custom");
            app.RequestedThemeVariant.Should().Be(ThemeVariant.Dark);
            ((ISolidColorBrush)app.FindResource("ReadOnlyTextBrush")!).Color.Should().Be(Color.Parse("#123456"));
            Write("Custom", """{"name":"Custom","themeVariant":"Light","colors":{"ReadOnlyText":"#654321"}}""");
            service.Apply("Custom");
            app.RequestedThemeVariant.Should().Be(ThemeVariant.Light);
            ((ISolidColorBrush)app.FindResource("ReadOnlyTextBrush")!).Color.Should().Be(Color.Parse("#654321"));
        }
        finally
        {
            service.Apply("Classic");
            app.RequestedThemeVariant = original;
        }
    }

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("dArK")]
    public void DiskPackCannotReplaceShippedTheme(string id)
    {
        Write(id, """{"name":"Dark","themeVariant":"Light","colors":{}}""");
        Write("Custom", "{}");
        var service = new ThemeService(_directory);
        service.AvailableThemes.Should().Equal("Classic", "Dark", "Abyss", "Custom");
        var original = Application.Current!.RequestedThemeVariant;
        try
        {
            service.Apply("Dark");
            Application.Current.RequestedThemeVariant.Should().Be(ThemeVariant.Dark);
        }
        finally
        {
            service.Apply("Classic");
            Application.Current.RequestedThemeVariant = original;
        }
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"colors\":{\"ReadOnlyText\":\"invalid\"}}")]
    public void FailedDiskPackRemovesPreviousOverride(string? invalid)
    {
        Write("Custom", """{"colors":{"ReadOnlyText":"#123456"}}""");
        var service = new ThemeService(_directory);
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var count = app.Resources.MergedDictionaries.Count;
        try
        {
            service.Apply("Custom");
            service.ActiveTheme.Should().Be("Custom");
            app.Resources.MergedDictionaries.Count.Should().Be(count + 1);
            if (invalid == null)
                File.Delete(Path.Combine(_directory, "Custom.json"));
            else
                Write("Custom", invalid);
            service.Apply("Custom");
            service.ActiveTheme.Should().Be("Classic");
            app.RequestedThemeVariant.Should().Be(ThemeVariant.Light);
            app.Resources.MergedDictionaries.Count.Should().Be(count);
        }
        finally
        {
            service.Apply("Classic");
            app.RequestedThemeVariant = original;
        }
    }
}
