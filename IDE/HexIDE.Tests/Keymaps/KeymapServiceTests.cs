using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
