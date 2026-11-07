using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HexIDE.IDE;
using HexIDE.Keymaps;
using HexIDE.Profiles;
using HexIDE.Tests.Sidecar;

namespace HexIDE.Tests.Profiles;

public sealed class ProfileKeymapTests : IDisposable
{
    private readonly string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "hexide-profile-keymap-" + Guid.NewGuid());
    private readonly Dictionary<string, KeyGesture[]> gestures = CommandKeyMapping.Table
        .ToDictionary(pair => pair.Key, pair => pair.Value.Gestures.ToArray());
    private string SettingsPath => Path.Combine(root, "user-settings.json");

    public ProfileKeymapTests() => Directory.CreateDirectory(root);

    private ProfileSnapshot Profile(string bindings = "\"CopyCommand\":\"Shift+F12\",\"CutCommand\":\"\"")
    {
        File.WriteAllText(Path.Combine(root, "profile.json"),
            """{"schemaVersion":1,"id":"custom","keymap":"keymap.json","settingsDefaults":"defaults.json"}""");
        File.WriteAllText(Path.Combine(root, "defaults.json"), """{"activeKeymap":"VB6"}""");
        File.WriteAllText(Path.Combine(root, "keymap.json"),
            $$$"""{"name":"Custom","bindings":{ {{{bindings}}} }}""");
        return new ProfileResolver(root, root).Resolve(root)!;
    }

    [AvaloniaFact]
    public void PayloadUsesSnapshotAndOverridesDefaultWithoutSavingIt()
    {
        var profile = Profile();
        File.WriteAllText(Path.Combine(root, "keymap.json"), "{}");
        var settings = new SettingsService(SettingsPath, profile);
        var keymaps = new KeymapService(root);
        keymaps.Apply(profile, settings);
        keymaps.ActiveKeymap.Should().Be("Custom");
        ApplicationCommands.CopyCommand.Gestures.Should().Equal(KeyGesture.Parse("Shift+F12"));
        ApplicationCommands.CutCommand.Gestures.Should().BeEmpty();
        ApplicationCommands.PasteCommand.Gestures.Should().Equal(gestures["PasteCommand"]);
        settings.Save();
        File.Exists(SettingsPath).Should().BeFalse();
        settings.ActiveKeymap = "Default";
        keymaps.Apply(profile, settings);
        keymaps.ActiveKeymap.Should().Be("Default");
        settings.Save();
        keymaps.Apply(profile, settings);
        keymaps.ActiveKeymap.Should().Be("Default");
        ApplicationCommands.CopyCommand.Gestures.Should().Equal(gestures["CopyCommand"]);
    }

    [AvaloniaTheory]
    [InlineData("Default")]
    [InlineData("VB6")]
    public void SavedChoiceWins(string choice)
    {
        File.WriteAllText(SettingsPath, $$"""{"activeKeymap":"{{choice}}"}""");
        var profile = Profile();
        var keymaps = new KeymapService(root);
        keymaps.Apply(profile, new SettingsService(SettingsPath, profile));
        keymaps.ActiveKeymap.Should().Be(choice);
    }

    [AvaloniaFact]
    public void UnrelatedSavedSettingDoesNotSuppressPayload()
    {
        File.WriteAllText(SettingsPath, """{"tabWidth":8}""");
        var profile = Profile();
        var keymaps = new KeymapService(root);
        keymaps.Apply(profile, new SettingsService(SettingsPath, profile));
        keymaps.ActiveKeymap.Should().Be("Custom");
    }

    [AvaloniaFact]
    public void UnknownCommandIsSkippedWithAttribution()
    {
        using var log = new CapturedLog();
        var profile = Profile("\"UnknownCommand\":\"NotAKey\",\"CopyCommand\":\"Shift+F12\"");
        var keymaps = new KeymapService(root);
        keymaps.Apply(profile, new SettingsService(SettingsPath, profile));
        ApplicationCommands.CopyCommand.Gestures.Should().Equal(KeyGesture.Parse("Shift+F12"));
        log.Messages.Should().Contain(message => message.Contains("UnknownCommand")
            && message.Contains(root) && message.Contains("keymap.json"));
    }

    [AvaloniaFact]
    public void InvalidKnownGestureRejectsBundleWithoutChangingCommands()
    {
        var keymaps = new KeymapService(root);
        var before = CommandKeyMapping.Table.ToDictionary(pair => pair.Key, pair => pair.Value.Gestures.ToArray());
        Action load = () => Profile("\"CopyCommand\":\"F11\",\"CutCommand\":\"NotAKey\"");
        var error = load.Should().Throw<ProfileLoadException>().Which;
        error.SourceId.Should().Be(root);
        error.File.Should().Be("keymap.json");
        error.Error.Should().Be(ProfileError.InvalidPayload);
        keymaps.ActiveKeymap.Should().Be("Default");
        foreach (var (name, command) in CommandKeyMapping.Table)
            command.Gestures.Should().Equal(before[name]);
    }

    [AvaloniaFact]
    public void MissingPayloadUsesProfileSettingsDefault()
    {
        var profile = Profile() with { Keymap = null };
        var keymaps = new KeymapService(root);
        keymaps.Apply(profile, new SettingsService(SettingsPath, profile));
        keymaps.ActiveKeymap.Should().Be("VB6");
    }

    public void Dispose()
    {
        foreach (var (name, command) in CommandKeyMapping.Table)
        {
            command.Gestures.Clear();
            foreach (var gesture in gestures[name])
                command.Gestures.Add(gesture);
        }
        Directory.Delete(root, true);
    }
}
