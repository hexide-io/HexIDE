using System.Runtime.Versioning;
using HexIDE.Runtime.Interpreter;
using Microsoft.Win32;

namespace HexIDE.Tests.Runtime;

/// <summary>
/// The stores a program's settings are kept in (#1562): the registry, where vb6.exe keeps them, and the file used where
/// there is no registry. What the four statements do is pinned over the in-memory store by SettingsProgramTests; these pin
/// what the stores themselves keep.
/// </summary>
public sealed class VbSettingsStoreTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("hexide_settings_").FullName;
    private readonly string registryRoot = @"Software\HexIDE.Tests\" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
        if (!OperatingSystem.IsWindows())
            return;
        Registry.CurrentUser.DeleteSubKeyTree(registryRoot, throwOnMissingSubKey: false);
        using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\HexIDE.Tests"))
            if (parent is null || parent.SubKeyCount > 0 || parent.ValueCount > 0)
                return;
        Registry.CurrentUser.DeleteSubKey(@"Software\HexIDE.Tests", throwOnMissingSubKey: false);
    }

    private static readonly string[] Section = ["App", "Section"];

    private static void KeepsWhatVb6Keeps(IVbSettingsStore store, Func<IVbSettingsStore> reopened)
    {
        store.SetValue(Section, "b", "2");
        store.SetValue(Section, "a", "1");
        store.SetValue(Section, "B", "two");
        store.SetValue(Section, "nul", "a\0b");
        store.SetValue(Section, "unicode", "\u4E00\u0101x");
        store.SetValue(["App", "Section", "Sub"], "k", "v");

        var again = reopened();
        again.Values(["APP", "section"]).Should().Equal(
            new VbSettingValue("b", "two"), new VbSettingValue("a", "1"), new VbSettingValue("nul", "a\0b"),
            new VbSettingValue("unicode", "\u4E00\u0101x"));
        again.SubKeys(["App"]).Should().Equal("Section");
        again.SubKeys(Section).Should().Equal("Sub");
        again.Values(["App", "Missing"]).Should().BeNull();
        again.SubKeys(["Missing"]).Should().BeNull();

        again.DeleteValue(Section, "A").Should().BeTrue();
        again.DeleteValue(Section, "a").Should().BeFalse();
        again.DeleteValue(["App", "Missing"], "a").Should().BeFalse();
        again.DeleteKey(["App", "Section", "Sub"]).Should().BeTrue();
        again.DeleteKey(["App", "Section", "Sub"]).Should().BeFalse();
        reopened().SubKeys(Section).Should().BeEmpty();
        reopened().Values(Section)!.Select(v => v.Name).Should().Equal("b", "nul", "unicode");
    }

    [Fact]
    public void The_file_keeps_what_the_registry_keeps_from_one_run_to_the_next()
    {
        var path = Path.Combine(directory, "program-settings.json");

        KeepsWhatVb6Keeps(new FileVbSettingsStore(path), () => new FileVbSettingsStore(path));
    }

    // A program's name can hold anything, and none of it reaches a path: every program is in the one file.
    [Fact]
    public void A_programs_name_never_becomes_a_path()
    {
        var path = Path.Combine(directory, "settings", "program-settings.json");
        var store = new FileVbSettingsStore(path);

        store.SetValue(["..", "..", "escaped"], "k", "v");
        store.SetValue([@"C:\Windows", "s"], "k", "v");

        Directory.GetFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            .Select(e => Path.GetRelativePath(directory, e)).Should().BeEquivalentTo("settings", Path.Combine("settings", "program-settings.json"));
        new FileVbSettingsStore(path).Values(["..", "..", "escaped"]).Should().Equal(new VbSettingValue("k", "v"));
    }

    [Fact]
    public void A_file_that_is_not_settings_is_an_error_not_an_empty_store()
    {
        var path = Path.Combine(directory, "program-settings.json");
        File.WriteAllText(path, "{ not json");

        var read = () => new FileVbSettingsStore(path).Values(Section);

        read.Should().Throw<IOException>().WithMessage($"*{path}*");
    }

    // The registry, under a key of the test's own rather than VB6's, removed afterwards. VB6's key is where vb6.exe keeps
    // them (measured): each value REG_SZ, a number saved as its text.
    [WindowsOnlyFact]
    [SupportedOSPlatform("windows")]
    public void The_registry_keeps_each_setting_as_text_as_vb6_exe_does()
    {
        KeepsWhatVb6Keeps(new RegistryVbSettingsStore(registryRoot), () => new RegistryVbSettingsStore(registryRoot));

        new RegistryVbSettingsStore(registryRoot).SetValue(Section, "Num", "42");
        using var key = Registry.CurrentUser.OpenSubKey(registryRoot + @"\App\Section");
        key!.GetValueKind("Num").Should().Be(RegistryValueKind.String);
        key.GetValue("Num").Should().Be("42");
        RegistryVbSettingsStore.Vb6Root.Should().Be(@"Software\VB and VBA Program Settings");
    }

    [WindowsOnlyFact]
    [SupportedOSPlatform("windows")]
    public void A_value_another_program_wrote_that_is_not_text_reads_as_no_text()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(registryRoot + @"\App\Section"))
        {
            key.SetValue("Sz", "abc", RegistryValueKind.String);
            key.SetValue("Dword", 42, RegistryValueKind.DWord);
            key.SetValue("Expand", "%WINDIR%x", RegistryValueKind.ExpandString);
        }

        new RegistryVbSettingsStore(registryRoot).Values(Section).Should().Equal(
            new VbSettingValue("Sz", "abc"), new VbSettingValue("Dword", null), new VbSettingValue("Expand", null));
    }
}
