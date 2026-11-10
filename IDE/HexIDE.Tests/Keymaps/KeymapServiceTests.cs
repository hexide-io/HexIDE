using System.Reflection;
using System.Text.Json;
using Avalonia.Labs.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform;
using AwesomeAssertions.Execution;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;
using HexIDE.Keymaps;

namespace HexIDE.Tests.Keymaps;

public sealed class KeymapServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HexIDE-keymaps-" + Guid.NewGuid());
    private readonly Dictionary<string, KeyGesture[]> _gestures = CommandKeyMapping.Table
        .ToDictionary(pair => pair.Key, pair => pair.Value.Gestures.ToArray());

    public KeymapServiceTests() => Directory.CreateDirectory(_directory);

    [AvaloniaFact]
    public void SavingRetainsPreviousKeymapBytesForRecovery()
    {
        WriteOverrides("\"CopyCommand\":\"F12\"");
        var path = Path.Combine(_directory, "overrides", "bindings.json");
        var original = File.ReadAllBytes(path);
        new KeymapService(_directory).SaveOverrides(new Dictionary<string, string>());
        var recovery = Directory.GetFiles(Path.GetDirectoryName(path)!, "*.recovery").Should().ContainSingle().Subject;
        File.ReadAllBytes(recovery).Should().Equal(original);
        JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("bindings").EnumerateObject().Should().BeEmpty();
    }

    [AvaloniaFact]
    public void DefaultPackDocumentsEveryDeclaredBinding()
    {
        using var stream = AssetLoader.Open(new Uri("avares://HexIDE/Keymaps/Packs/Default.json"));
        var pack = JsonSerializer.Deserialize(stream, KeymapJsonContext.Default.KeymapPackRecord)!;
        pack.Bindings.Should().NotBeNull().And.NotBeEmpty();
        using var scope = new AssertionScope();
        foreach (var (name, text) in pack.Bindings!)
        {
            CommandKeyMapping.Table.Should().ContainKey(name);
            var expected = string.IsNullOrEmpty(text) ? [] : new[] { KeyGesture.Parse(text) };
            if (OperatingSystem.IsMacOS())
            {
                expected = expected.Select(gesture => new KeyGesture(gesture.Key,
                    gesture.KeyModifiers.HasFlag(KeyModifiers.Control)
                        ? (gesture.KeyModifiers & ~KeyModifiers.Control) | KeyModifiers.Meta
                        : gesture.KeyModifiers)).ToArray();
                if (name == nameof(ApplicationCommands.ReplaceCommand))
                    expected = [new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Alt)];
                if (name == nameof(ApplicationCommands.ToggleFoldCommand))
                    expected = [];
            }
            CommandKeyMapping.Table[name].Gestures.Should().Equal(expected, "{0} must match Default.json", name);
        }
    }

    [Fact]
    public void CommandTableMatchesEveryRoutedCommandByNameAndInstance()
    {
        var commands = typeof(ApplicationCommands).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(RoutedCommand))
            .ToDictionary(field => field.Name, field => (RoutedCommand)field.GetValue(null)!);

        CommandKeyMapping.Table.Keys.Should().BeEquivalentTo(commands.Keys);
        foreach (var (name, command) in commands)
            CommandKeyMapping.Table[name].Should().BeSameAs(command);
    }

    [AvaloniaTheory]
    [InlineData("Default")]
    [InlineData("VB6")]
    public void EditWatchRetainsItsDefaultGesture(string keymap)
    {
        var service = new KeymapService(_directory);
        service.Apply(keymap);

        var gesture = new KeyGesture(Key.W, ApplicationCommands.CopyCommand.Gestures.Single().KeyModifiers);
        ApplicationCommands.EditWatchCommand.Gestures.Should().Equal(gesture);
        CommandKeyMapping.Table.Where(pair => pair.Value.Gestures.Contains(gesture))
            .Select(pair => pair.Key).Should().Equal(nameof(ApplicationCommands.EditWatchCommand));
    }

    [AvaloniaTheory]
    [InlineData(nameof(ApplicationCommands.OpenCallStackCommand))]
    [InlineData(nameof(ApplicationCommands.ResetWindowLayoutCommand))]
    [InlineData(nameof(ApplicationCommands.GoToDeclarationCommand))]
    public void DiskPackRebindsAndClearsPreviouslyMissingCommandAndDefaultRestoresIt(string name)
    {
        var command = (RoutedCommand)typeof(ApplicationCommands).GetField(name)!.GetValue(null)!;
        var original = command.Gestures.ToArray();
        File.WriteAllText(Path.Combine(_directory, "Custom.json"),
            $$$"""{"name":"Custom","bindings":{"{{{name}}}":"Shift+F12"}}""");
        var service = new KeymapService(_directory);

        service.Apply("Custom");
        command.Gestures.Should().Equal(KeyGesture.Parse("Shift+F12"));
        File.WriteAllText(Path.Combine(_directory, "Custom.json"),
            $$$"""{"name":"Custom","bindings":{"{{{name}}}":""}}""");
        service.Apply("Custom");
        command.Gestures.Should().BeEmpty();
        service.Apply("Default");
        command.Gestures.Should().Equal(original);
    }

    [AvaloniaFact]
    public void OptionsListsDiskPacksByFilenameWithoutReplacingBuiltins()
    {
        File.WriteAllText(Path.Combine(_directory, "Custom.json"), Pack);
        File.WriteAllText(Path.Combine(_directory, "VB6.json"), Pack);
        File.WriteAllText(Path.Combine(_directory, "default.json"), Pack);
        File.WriteAllText(Path.Combine(_directory, "Ignored.txt"), Pack);
        var service = new KeymapService(_directory);
        var settings = Substitute.For<ISettingsService>();
        settings.ActiveKeymap.Returns("Default");

        new KeymapPageViewModel(settings, service).AvailableKeymaps.Should().Equal("Default", "VB6", "Custom");
    }

    [AvaloniaFact]
    public void SavedSelectionLoadsDiskBindingsAndDefaultRestoresGestures()
    {
        File.WriteAllText(Path.Combine(_directory, "Custom.json"), Pack);
        var service = new KeymapService(_directory);
        var settings = Substitute.For<ISettingsService>();
        settings.ActiveKeymap.Returns("Custom");
        var page = new KeymapPageViewModel(settings, service);
        page.SaveToSettings();

        service.ActiveKeymap.Should().Be("Custom");
        ApplicationCommands.CopyCommand.Gestures.Should().Equal(KeyGesture.Parse("Shift+F12"));
        ApplicationCommands.CutCommand.Gestures.Should().BeEmpty();
        ApplicationCommands.PasteCommand.Gestures.Should().Equal(_gestures["PasteCommand"]);
        service.Apply("Default");
        ApplicationCommands.CopyCommand.Gestures.Should().Equal(_gestures["CopyCommand"]);
        ApplicationCommands.CutCommand.Gestures.Should().Equal(_gestures["CutCommand"]);
    }

    [AvaloniaFact]
    public void InvalidDiskGestureRestoresDefaultAfterPartialApplication()
    {
        File.WriteAllText(Path.Combine(_directory, "Custom.json"), Pack);
        var service = new KeymapService(_directory);
        service.Apply("Custom");
        service.ActiveKeymap.Should().Be("Custom");
        File.WriteAllText(Path.Combine(_directory, "Custom.json"),
            """{"name":"Custom","bindings":{"CopyCommand":"F11","CutCommand":"NotAKey"}}""");

        service.Apply("Custom");

        service.ActiveKeymap.Should().Be("Default");
        foreach (var (name, command) in CommandKeyMapping.Table)
            command.Gestures.Should().Equal(_gestures[name]);
    }

    private void WriteOverrides(string bindings)
    {
        var directory = Path.Combine(_directory, "overrides");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "bindings.json"),
            "{\"bindings\":{" + bindings + "}}");
    }

    [AvaloniaFact]
    public void UserOverridesRebindClearAndAddWithoutChangingOtherCommands()
    {
        WriteOverrides("\"CopyCommand\":\"Shift+F12\",\"CutCommand\":\"\",\"ExpandSelectionCommand\":\"Shift+F11\"");
        var service = new KeymapService(_directory);
        service.Apply("Default");

        ApplicationCommands.CopyCommand.Gestures.Should().Equal(KeyGesture.Parse("Shift+F12"));
        ApplicationCommands.CutCommand.Gestures.Should().BeEmpty();
        ApplicationCommands.ExpandSelectionCommand.Gestures.Should().Equal(KeyGesture.Parse("Shift+F11"));
        ApplicationCommands.PasteCommand.Gestures.Should().Equal(_gestures["PasteCommand"]);
        service.AvailableKeymaps.Should().Equal("Default", "VB6");
    }

    [AvaloniaFact]
    public void UserOverridesSurvivePackSwitchUpdateAndServiceRecreation()
    {
        File.WriteAllText(Path.Combine(_directory, "Custom.json"), Pack);
        WriteOverrides("\"CopyCommand\":\"Shift+F11\"");
        var service = new KeymapService(_directory);
        foreach (var keymap in new[] { "Custom", "VB6", "Default" })
        {
            service.Apply(keymap);
            ApplicationCommands.CopyCommand.Gestures.Should().Equal(KeyGesture.Parse("Shift+F11"));
            service.ActiveKeymap.Should().Be(keymap);
        }
        File.WriteAllText(Path.Combine(_directory, "Custom.json"),
            """{"bindings":{"CopyCommand":"F12","PasteCommand":"Shift+F10"}}""");
        service.Apply("Custom");
        ApplicationCommands.CopyCommand.Gestures.Should().Equal(KeyGesture.Parse("Shift+F11"));
        ApplicationCommands.PasteCommand.Gestures.Should().Equal(KeyGesture.Parse("Shift+F10"));
        new KeymapService(_directory).Apply("Custom");
        ApplicationCommands.CopyCommand.Gestures.Should().Equal(KeyGesture.Parse("Shift+F11"));
    }

    [AvaloniaFact]
    public void RemovingOverrideRestoresSelectedPackBinding()
    {
        File.WriteAllText(Path.Combine(_directory, "Custom.json"), Pack);
        WriteOverrides("\"CopyCommand\":\"Shift+F11\"");
        var service = new KeymapService(_directory);
        service.Apply("Custom");
        ApplicationCommands.CopyCommand.Gestures.Should().Equal(KeyGesture.Parse("Shift+F11"));
        WriteOverrides("");
        service.Apply("Custom");
        ApplicationCommands.CopyCommand.Gestures.Should().Equal(KeyGesture.Parse("Shift+F12"));
    }

    [AvaloniaFact]
    public void UnknownOverrideCommandDoesNotDiscardKnownOverrides()
    {
        WriteOverrides("\"UnknownCommand\":\"NotAKey\",\"CopyCommand\":\"Shift+F11\"");
        new KeymapService(_directory).Apply("Default");
        ApplicationCommands.CopyCommand.Gestures.Should().Equal(KeyGesture.Parse("Shift+F11"));
    }

    [AvaloniaFact]
    public void InvalidOverridesAreRejectedAsAWholeWithoutDiscardingSelectedPack()
    {
        File.WriteAllText(Path.Combine(_directory, "Custom.json"), Pack);
        WriteOverrides("\"CopyCommand\":\"Shift+F11\"");
        var service = new KeymapService(_directory);
        service.Apply("Custom");
        ApplicationCommands.CopyCommand.Gestures.Should().Equal(KeyGesture.Parse("Shift+F11"));
        foreach (var content in new[]
                 {
                     """{"bindings":{"CopyCommand":"F11","CutCommand":"NotAKey"}}""",
                     "{",
                     "null",
                     """{"bindings":{"CopyCommand":42}}"""
                 })
        {
            File.WriteAllText(Path.Combine(_directory, "overrides", "bindings.json"), content);
            service.Apply("Custom");
            service.ActiveKeymap.Should().Be("Custom");
            ApplicationCommands.CopyCommand.Gestures.Should().Equal(KeyGesture.Parse("Shift+F12"));
            ApplicationCommands.CutCommand.Gestures.Should().BeEmpty();
        }
    }

    [AvaloniaFact]
    public void RecoveryBypassesUserOverrides()
    {
        WriteOverrides("\"CopyCommand\":\"Shift+F11\"");
        var service = new KeymapService(_directory);
        service.Apply("Default");
        ApplicationCommands.CopyCommand.Gestures.Should().Equal(KeyGesture.Parse("Shift+F11"));
        var previous = Static.IsRecoveryMode;
        try
        {
            Static.IsRecoveryMode = true;
            service.Apply("Default");
            ApplicationCommands.CopyCommand.Gestures.Should().Equal(_gestures["CopyCommand"]);
        }
        finally
        {
            Static.IsRecoveryMode = previous;
        }
    }

    private const string Pack = """
        {"name":"Display metadata","bindings":{"CopyCommand":"Shift+F12","CutCommand":"","UnknownCommand":"F11"}}
        """;

    public void Dispose()
    {
        foreach (var (name, command) in CommandKeyMapping.Table)
        {
            command.Gestures.Clear();
            foreach (var gesture in _gestures[name])
                command.Gestures.Add(gesture);
        }
        Directory.Delete(_directory, true);
    }
}
