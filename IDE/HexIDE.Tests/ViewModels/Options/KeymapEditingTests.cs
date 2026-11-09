using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Windows.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using HexIDE.Forms.Views.Options;
using Avalonia.Input;
using HexIDE.Addins;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;
using HexIDE.Keymaps;
using HexIDE.Localization;
using HexIDE.Profiles;
using HexIDE.Themes;

namespace HexIDE.Tests.ViewModels.Options;

public sealed class KeymapEditingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HexIDE-keymap-edit-" + Guid.NewGuid());
    private readonly Dictionary<string, KeyGesture[]> _original = CommandKeyMapping.Table.ToDictionary(p => p.Key, p => p.Value.Gestures.ToArray());
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly KeymapService _service;
    private string FilePath => Path.Combine(_directory, "overrides", "bindings.json");

    public KeymapEditingTests()
    {
        Directory.CreateDirectory(_directory);
        _settings.ActiveKeymap.Returns("Default");
        _settings.ActiveTheme.Returns("Classic");
        _settings.ActiveLanguage.Returns("en");
        _service = new KeymapService(_directory);
    }

    // Resolve the editing surface by name so these regression tests also compile on the pre-editor branch.
    private static object Property(object target, string name)
    {
        var property = target.GetType().GetProperty(name);
        property.Should().NotBeNull("the Keymap page must expose {0}", name);
        return property!.GetValue(target)!;
    }
    private static void Click(object target, string name) => ((ICommand)Property(target, name + "Command")).Execute(null);
    private static void Select(KeymapPageViewModel page, string name)
    {
        var row = ((IEnumerable)Property(page, "Commands")).Cast<object>().Single(c => (string)Property(c, "Id") == name);
        page.GetType().GetProperty("SelectedCommand")!.SetValue(page, row);
    }
    private static object? Invoke(object page, string method, params object[] args)
    {
        var member = page.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        member.Should().NotBeNull("the editor must support {0}", method);
        return member!.Invoke(page, args);
    }
    private static void Set(KeymapPageViewModel page, KeyGesture gesture)
    {
        Click(page, "Set");
        Invoke(page, "Capture", gesture);
    }
    private static string Binding(KeymapPageViewModel page, string name)
    {
        using var json = JsonDocument.Parse((string)Invoke(page, "ExportJson")!);
        return json.RootElement.GetProperty("bindings").GetProperty(name).GetString()!;
    }
    private KeymapPageViewModel Page() => new(_settings, _service);

    [AvaloniaFact]
    public void SetStagesAndOkPersistsAndApplies()
    {
        var page = Page();
        Select(page, nameof(ApplicationCommands.ExpandSelectionCommand));
        var gesture = new KeyGesture(Key.F24, KeyModifiers.Shift);
        Set(page, gesture);
        File.Exists(FilePath).Should().BeFalse();
        ApplicationCommands.ExpandSelectionCommand.Gestures.Should().Equal(_original[nameof(ApplicationCommands.ExpandSelectionCommand)]);
        page.SaveToSettings();
        ApplicationCommands.ExpandSelectionCommand.Gestures.Should().Equal(gesture);
        JsonDocument.Parse(File.ReadAllText(FilePath)).RootElement.GetProperty("bindings")
            .GetProperty(nameof(ApplicationCommands.ExpandSelectionCommand)).GetString().Should().Be(gesture.ToString());
    }

    [AvaloniaFact]
    public void ClearCreatesAnExplicitEmptyOverride()
    {
        var page = Page();
        Select(page, nameof(ApplicationCommands.CopyCommand));
        Click(page, "Clear");
        Binding(page, nameof(ApplicationCommands.CopyCommand)).Should().BeEmpty();
        page.SaveToSettings();
        ApplicationCommands.CopyCommand.Gestures.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void ResetRestoresPackAndResetAllRemovesEveryOverride()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """{"bindings":{"CopyCommand":"","PasteCommand":""}}""");
        _service.Apply("VB6");
        _settings.ActiveKeymap.Returns("VB6");
        var page = Page();
        Select(page, nameof(ApplicationCommands.CopyCommand));
        Click(page, "Reset");
        page.SaveToSettings();
        ApplicationCommands.CopyCommand.Gestures.Should().Equal(_original[nameof(ApplicationCommands.CopyCommand)]);
        ApplicationCommands.PasteCommand.Gestures.Should().BeEmpty();
        Click(page, "ResetAll");
        page.SaveToSettings();
        ApplicationCommands.PasteCommand.Gestures.Should().Equal(_original[nameof(ApplicationCommands.PasteCommand)]);
    }

    [AvaloniaFact]
    public void ConflictRequiresKeepAndDoesNotUnbindTheOtherCommand()
    {
        var page = Page();
        Select(page, nameof(ApplicationCommands.ExpandSelectionCommand));
        var gesture = ApplicationCommands.CopyCommand.Gestures.First();
        Set(page, gesture);
        ((bool)Property(page, "HasConflict")).Should().BeTrue();
        ((string)Property(page, "Conflict")).Should().Contain(new LocalizationService().GetString("Str.Menu.Edit.Copy").Replace("_", ""));
        Click(page, "CancelCapture");
        page.SaveToSettings();
        ApplicationCommands.ExpandSelectionCommand.Gestures.Should().Equal(_original[nameof(ApplicationCommands.ExpandSelectionCommand)]);
        Set(page, gesture);
        Click(page, "Keep");
        page.SaveToSettings();
        ApplicationCommands.ExpandSelectionCommand.Gestures.Should().Equal(gesture);
        ApplicationCommands.CopyCommand.Gestures.Should().Equal(_original[nameof(ApplicationCommands.CopyCommand)]);
    }

    [AvaloniaFact]
    public void InvalidImportLeavesEveryStagedBindingIntact()
    {
        var page = Page();
        Select(page, nameof(ApplicationCommands.CopyCommand));
        Click(page, "Clear");
        var before = (string)Invoke(page, "ExportJson")!;
        Invoke(page, "ImportJson", """{"bindings":{"PasteCommand":"Shift+F24","CopyCommand":"NotAKey"}}""").Should().Be(false);
        ((string)Property(page, "Error")).Should().NotBeEmpty();
        Invoke(page, "ExportJson").Should().Be(before);
        Invoke(page, "ImportJson", "{").Should().Be(false);
        Invoke(page, "ExportJson").Should().Be(before);
        Invoke(page, "ImportJson", """{"bindings":{"PasteCommand":"Shift+F24"}}""").Should().Be(true);
        Binding(page, nameof(ApplicationCommands.PasteCommand)).Should().Be("Shift+F24");
        File.Exists(FilePath).Should().BeFalse();
    }

    [AvaloniaFact]
    public void OptionsCancelDiscardsShortcutEdits()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        const string saved = """{"bindings":{"PasteCommand":""}}""";
        File.WriteAllText(FilePath, saved);
        _service.Apply("Default");
        var language = Substitute.For<ILanguageSwitchService>();
        language.ActiveLanguage.Returns("en");
        language.AvailableLanguages.Returns([new LanguageInfo("en", "English", "ltr")]);
        language.RegionsFor(Arg.Any<string>()).Returns([]);
        var theme = Substitute.For<IThemeService>();
        theme.ActiveTheme.Returns("Classic");
        theme.AvailableThemes.Returns(["Classic"]);
        var registry = Substitute.For<IAddinRegistry>();
        registry.GetAll().Returns([]);
        var shell = new OptionsViewModel(_settings, theme, _service, language, registry, new AddinOptionsService(),
            Substitute.For<IDeveloperModeService>(), new LocalizationService(), Substitute.For<IEventBus>(), "Str.Options.Node.Keymap");
        shell.SelectedNode = shell.RootNodes.SelectMany(n => n.Children).Single(n => n.Key == "Str.Options.Node.Keymap");
        var page = (KeymapPageViewModel)shell.CurrentPage!;
        Select(page, nameof(ApplicationCommands.CopyCommand));
        Click(page, "Clear");
        shell.CancelCommand.Execute(null);
        File.ReadAllText(FilePath).Should().Be(saved);
        ApplicationCommands.CopyCommand.Gestures.Should().Equal(_original[nameof(ApplicationCommands.CopyCommand)]);
        var reopened = Page();
        Select(reopened, nameof(ApplicationCommands.CopyCommand));
        ((string)Property(Property(reopened, "SelectedCommand"), "Gesture")).Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void SearchCoversEveryCommandAndShowsPackOrUserSourceWithoutApplyingPack()
    {
        var page = Page();
        var rows = ((IEnumerable)Property(page, "Commands")).Cast<object>().ToArray();
        rows.Select(row => (string)Property(row, "Id")).Should().BeEquivalentTo(CommandKeyMapping.Table.Keys);
        var copy = rows.Single(row => (string)Property(row, "Id") == nameof(ApplicationCommands.CopyCommand));
        ((string)Property(copy, "Name")).Should().Be(new LocalizationService().GetString("Str.Menu.Edit.Copy").Replace("_", ""));
        ((string)Property(copy, "Source")).Should().Be("Pack");
        page.GetType().GetProperty("SearchText")!.SetValue(page, "ExpandSelectionCommand");
        ((IEnumerable)Property(page, "FilteredCommands")).Cast<object>().Should().ContainSingle();
        Select(page, nameof(ApplicationCommands.ExpandSelectionCommand));
        Set(page, new KeyGesture(Key.F24, KeyModifiers.Shift));
        page.SelectedKeymap = "VB6";
        _service.ActiveKeymap.Should().Be("Default");
        var selected = Property(page, "SelectedCommand");
        ((string)Property(selected, "Source")).Should().Be("User");
        ((string)Property(selected, "Gesture")).Should().Be("Shift+F24");
    }

    [AvaloniaFact]
    public void ProfileResetRestoresPayloadWithoutPersistingItsNameAsADiskPack()
    {
        File.WriteAllText(Path.Combine(_directory, "profile.json"),
            """{"schemaVersion":1,"id":"custom","keymap":"keymap.json","settingsDefaults":"defaults.json"}""");
        File.WriteAllText(Path.Combine(_directory, "defaults.json"), """{"activeKeymap":"Default"}""");
        File.WriteAllText(Path.Combine(_directory, "keymap.json"), """{"name":"VB6","bindings":{"CopyCommand":"Shift+F12"}}""");
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """{"bindings":{"CopyCommand":"Shift+F11"}}""");
        var profile = new ProfileResolver(_directory, _directory).Resolve(_directory)!;
        var settings = new SettingsService(Path.Combine(_directory, "settings.json"), profile);
        _service.Apply(profile, settings);
        var page = new KeymapPageViewModel(settings, _service);
        Select(page, nameof(ApplicationCommands.CopyCommand));
        Click(page, "Reset");
        page.SaveToSettings();
        ApplicationCommands.CopyCommand.Gestures.Should().Equal(KeyGesture.Parse("Shift+F12"));
        _service.ActiveKeymap.Should().Be("VB6");
        settings.HasExplicitKeymapChoice.Should().BeFalse();
    }

    [AvaloniaFact]
    public void FailedAtomicSaveLeavesFileSettingsAndLiveGesturesUnchanged()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        const string saved = """{"bindings":{"PasteCommand":""}}""";
        File.WriteAllText(FilePath, saved);
        var page = Page();
        Select(page, nameof(ApplicationCommands.CopyCommand));
        Click(page, "Clear");
        AtomicFileWriter.OpenStream.Value = _ => throw new IOException("write refused");
        try
        {
            Action save = page.SaveToSettings;
            save.Should().Throw<IOException>();
            File.ReadAllText(FilePath).Should().Be(saved);
            ApplicationCommands.CopyCommand.Gestures.Should().Equal(_original[nameof(ApplicationCommands.CopyCommand)]);
            _settings.DidNotReceive().ActiveKeymap = Arg.Any<string>();
        }
        finally { AtomicFileWriter.OpenStream.Value = null; }
    }

    [AvaloniaFact]
    public void ViewCapturesPressedGestureAndEscapeCancelsCapture()
    {
        var page = Page();
        Select(page, nameof(ApplicationCommands.ExpandSelectionCommand));
        var view = new KeymapPageView { DataContext = page };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            var button = view.FindControl<Button>("SetButton");
            button.Should().NotBeNull();
            button!.Command!.Execute(null);
            var modifier = new KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Key.LeftShift };
            view.RaiseEvent(modifier);
            modifier.Handled.Should().BeTrue();
            ((bool)Property(page, "IsCapturing")).Should().BeTrue();
            var key = new KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Key.F24, KeyModifiers = KeyModifiers.Shift };
            view.RaiseEvent(key);
            key.Handled.Should().BeTrue();
            Binding(page, nameof(ApplicationCommands.ExpandSelectionCommand)).Should().Be("Shift+F24");
            button.Command.Execute(null);
            view.RaiseEvent(new KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Key.Escape });
            ((bool)Property(page, "IsCapturing")).Should().BeFalse();
            Binding(page, nameof(ApplicationCommands.ExpandSelectionCommand)).Should().Be("Shift+F24");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task BulkCommandsUseFilePickersAndExportStagedOverrides()
    {
        var windows = Substitute.For<IWindowManager>();
        var input = Path.Combine(_directory, "import.json");
        var output = Path.Combine(_directory, "export.json");
        File.WriteAllText(input, """{"bindings":{"CopyCommand":""}}""");
        windows.OpenFilePickerAsync(Arg.Any<FilePickerOpenOptions>()).Returns(Task.FromResult<IReadOnlyList<string>?>([input]));
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>()).Returns(Task.FromResult<string?>(output));
        var constructor = typeof(KeymapPageViewModel).GetConstructors().Single();
        var page = (KeymapPageViewModel)constructor.Invoke(constructor.GetParameters().Length == 2
            ? [_settings, _service] : [_settings, _service, new LocalizationService(), windows]);
        await ((IAsyncRelayCommand)Property(page, "ImportCommand")).ExecuteAsync(null);
        Binding(page, nameof(ApplicationCommands.CopyCommand)).Should().BeEmpty();
        await ((IAsyncRelayCommand)Property(page, "ExportCommand")).ExecuteAsync(null);
        File.ReadAllText(output).Should().Be((string)Invoke(page, "ExportJson")!);
        File.Exists(FilePath).Should().BeFalse();
        ApplicationCommands.CopyCommand.Gestures.Should().Equal(_original[nameof(ApplicationCommands.CopyCommand)]);
        await windows.Received(1).OpenFilePickerAsync(Arg.Any<FilePickerOpenOptions>());
        await windows.Received(1).SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>());
    }

    [AvaloniaFact]
    public void RecoveryEditingDoesNotReadOrOverwriteSavedOverrides()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        const string saved = """{"bindings":{"CopyCommand":""}}""";
        File.WriteAllText(FilePath, saved);
        var previous = Static.IsRecoveryMode;
        try
        {
            Static.IsRecoveryMode = true;
            var page = Page();
            Select(page, nameof(ApplicationCommands.CopyCommand));
            ((string)Property(Property(page, "SelectedCommand"), "Source")).Should().Be("Pack");
            Click(page, "Clear");
            page.SaveToSettings();
            File.ReadAllText(FilePath).Should().Be(saved);
            ApplicationCommands.CopyCommand.Gestures.Should().Equal(_original[nameof(ApplicationCommands.CopyCommand)]);
        }
        finally { Static.IsRecoveryMode = previous; }
    }

    [AvaloniaFact]
    public void EveryCommandHasALocalizedName()
    {
        // NYI is the shared placeholder handler, without a distinct menu identity.
        CommandKeyMapping.Table.Keys.Except(["NYICommand"]).Should().OnlyContain(id => CommandDisplayNames.Keys.ContainsKey(id));
    }

    [AvaloniaFact]
    public void RowsAndSearchHaveAccessibleNames()
    {
        var page = Page();
        var view = new KeymapPageView { DataContext = page };
        view.Styles.Add(new Avalonia.Themes.Simple.SimpleTheme());
        view.Resources["Str.Tool.ObjectBrowser.Toolbar.Search"] = new LocalizationService().GetString("Str.Tool.ObjectBrowser.Toolbar.Search");
        var window = new Window { Content = view };
        window.Show();
        try
        {
            var search = view.GetLogicalDescendants().OfType<TextBox>().Single();
            Avalonia.Automation.AutomationProperties.GetName(search).Should().NotBeNullOrWhiteSpace();
            var row = view.GetVisualDescendants().OfType<ListBoxItem>().First();
            Avalonia.Automation.AutomationProperties.GetName(row).Should().Contain(page.Commands[0].Name);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MissingBindingsExplainsTheProblemWithoutChangingEdits()
    {
        var page = Page();
        var before = page.ExportJson();
        page.ImportJson("{}").Should().BeFalse();
        page.Error.Should().Contain("bindings");
        page.ExportJson().Should().Be(before);
    }

    [AvaloniaFact]
    public void ExportKeepsPlusReadable()
    {
        var page = Page();
        page.ImportJson("""{"bindings":{"CopyCommand":"Shift+F24"}}""").Should().BeTrue();
        page.ExportJson().Should().Contain("Shift+F24");
    }

    [AvaloniaFact]
    public void CommandPathsDistinguishAmbiguousLabels()
    {
        var page = Page();
        page.Commands.Single(c => c.Id == "AlignLeftsCommand").Name.Should().Be("Format ▸ Align ▸ Lefts");
        page.Commands.Where(c => c.Id != "NYICommand").Select(c => c.Name).Should().OnlyHaveUniqueItems();
    }

    [AvaloniaTheory]
    [InlineData(Key.OemPlus)]
    [InlineData(Key.Return)]
    public void CaptureLabelsAreReadable(Key key)
    {
        var page = Page();
        Select(page, "ExpandSelectionCommand");
        Set(page, new KeyGesture(key));
        page.SelectedCommand!.Gesture.Should().NotContain(key.ToString());
    }

    [AvaloniaFact]
    public void ConflictDoesNotAppendPeriodToEllipsis()
    {
        var page = Page();
        var gesture = page.Commands.Single(c => c.Id == "ReplaceCommand").Keys.First();
        Select(page, "ExpandSelectionCommand");
        Set(page, gesture);
        page.Conflict.Should().NotContain("....");
    }

    public void Dispose()
    {
        foreach (var (name, keys) in _original)
        {
            var command = CommandKeyMapping.Table[name];
            command.Gestures.Clear();
            foreach (var key in keys) command.Gestures.Add(key);
        }
        Directory.Delete(_directory, true);
    }
}
