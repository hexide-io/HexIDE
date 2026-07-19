using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Tools.TranslationEditor;

namespace HexIDE.Integration.Tests.Views;

// The translation editor is a Document pane opened only via Options → Language → "Customize…", which MCP
// take_snapshot cannot drive (no tool clicks a button inside the modal). So we render it headlessly with
// real Skia and capture a frame — the documented substitute for the mandatory visual check. Because the
// test app uses the same SimpleTheme (and no extra DataGrid theme) as the real app, a grid that renders
// here renders there too.
public class TranslationEditorViewIntegrationTests
{
    private static (LocalizationService loc, UserTranslationsService uts, string dir) BuildServices(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "hexide_te_" + name + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var uts = new UserTranslationsService(dir);
        // A user override on neutral German for a always-present menu key → shows (bold) when To = de.
        uts.SetOverride("de", "Str.Menu.File", "DATEI ★ OVERRIDE");
        var loc = new LocalizationService(uts);
        loc.Apply("en");                       // load en chrome so the editor's own Str.* labels resolve
        return (loc, uts, dir);
    }

    private static TranslationEditorViewModel BuildVm(LocalizationService loc, UserTranslationsService uts)
    {
        var vm = new TranslationEditorViewModel(loc, uts, Substitute.For<IWindowManager>());
        // To = German neutral (surfaces the override, bold); From = en-GB (a thin region → most rows red).
        vm.ToLanguage = vm.ToLanguages.First(l => l.Id == "de");
        vm.FromRegion = vm.FromRegions.First(r => r.Id == "en-GB");
        return vm;
    }

    [AvaloniaFact]
    public void TranslationEditor_PopulatesGrid_OverrideBold_And_EnGbRowsRed()
    {
        var (loc, uts, dir) = BuildServices(nameof(TranslationEditor_PopulatesGrid_OverrideBold_And_EnGbRowsRed));
        try
        {
            var vm = BuildVm(loc, uts);

            // The grid is populated from the canonical en key list.
            vm.Rows.Count.Should().BeGreaterThan(400);

            // The German override row is marked overridden (drives bold) and shows the override value.
            var fileRow = vm.Rows.First(r => r.Key == "Str.Menu.File");
            fileRow.IsOverridden.Should().BeTrue();
            fileRow.ToValue.Should().Be("DATEI ★ OVERRIDE");

            // From = en-GB: keys en-GB does NOT itself override resolve to English → flagged red.
            // (en-GB ships only ~17 British-spelling keys, so the vast majority are red.)
            vm.Rows.Count(r => r.IsRedFallback).Should().BeGreaterThan(100);
            // A key en-GB DOES define (a British spelling) must NOT be red.
            var anyBritish = vm.Rows.FirstOrDefault(r => !r.IsRedFallback && r.Key != "Str.Menu.File");
            anyBritish.Should().NotBeNull();

            var path = Capture(vm, "hexide_translation_editor.png");
            new FileInfo(path).Length.Should().BeGreaterThan(0);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [AvaloniaFact]
    public void TranslationEditor_ChromeLocalizes_French()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hexide_te_fr_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var uts = new UserTranslationsService(dir);
            var loc = new LocalizationService(uts);
            loc.Apply("fr");                       // French IDE chrome
            var vm = new TranslationEditorViewModel(loc, uts, Substitute.For<IWindowManager>());
            vm.FromLanguage = vm.FromLanguages.First(l => l.Id == "en");   // English reference; To stays French

            vm.Rows.Count.Should().BeGreaterThan(400);
            // The grid group header for a Dialog-area key reads in French (not the English "Dialogs"),
            // proving the editor's own chrome localizes off the active pack after the backfill.
            var dialogRow = vm.Rows.First(r => r.Area == "Dialog");
            dialogRow.AreaLabel.Should().Be(loc.GetStringFrom("fr", "Str.TranslationEditor.Area.Dialog"));
            dialogRow.AreaLabel.Should().NotBeNullOrWhiteSpace();

            var path = Capture(vm, "hexide_translation_editor_fr.png");
            new FileInfo(path).Length.Should().BeGreaterThan(0);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    // Render the editor in a headless top-level (real Skia) and save a PNG. Provide the ValidationErrorBrush
    // the real app supplies from Classic.axaml so the red en-fallback text renders faithfully here too.
    private static string Capture(TranslationEditorViewModel vm, string name)
    {
        var window = new Window
        {
            Width = 1100,
            Height = 680,
            Background = Brushes.White,
            Content = new TranslationEditorView { DataContext = vm },
        };
        window.Resources["ValidationErrorBrush"] = new SolidColorBrush(Color.Parse("#CC0000"));
        // The From cell's non-red foreground binds SystemColors.WindowTextBrushKey, which the real app gets
        // from Classic.axaml but the bare-SimpleTheme test app lacks — supply it so From text renders.
        window.Resources[Classic.CommonControls.SystemColors.WindowTextBrushKey] = new SolidColorBrush(Colors.Black);

        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull();
        var path = Path.Combine(Path.GetTempPath(), name);
        frame!.Save(path);
        window.Close();
        return path;
    }
}
