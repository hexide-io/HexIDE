using System.IO;
using System.Text.Json;
using HexIDE.IDE;

namespace HexIDE.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HexIDE_SettingsTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string SettingsPath => Path.Combine(_tempDir, "settings.json");

    private SettingsService CreateSut()
    {
        // Use a temp path so the test is isolated from any real settings file.
        return new SettingsService(SettingsPath);
    }

    // ── Defaults ────────────────────────────────────────────────

    [Fact]
    public void Defaults_MatchSettingsDefaults()
    {
        var sut = CreateSut();

        sut.RequireVariableDeclaration.Should().Be(SettingsDefaults.RequireVariableDeclaration);
        sut.AutoListMembers.Should().Be(SettingsDefaults.AutoListMembers);
        sut.AutoQuickInfo.Should().Be(SettingsDefaults.AutoQuickInfo);
        sut.AutoIndent.Should().Be(SettingsDefaults.AutoIndent);
        sut.TabWidth.Should().Be(SettingsDefaults.TabWidth);
        sut.FormatOnSave.Should().Be(SettingsDefaults.FormatOnSave);
        sut.ShowGrid.Should().Be(SettingsDefaults.ShowGrid);
        sut.GridWidth.Should().Be(SettingsDefaults.GridWidth);
        sut.GridHeight.Should().Be(SettingsDefaults.GridHeight);
        sut.AlignToGrid.Should().Be(SettingsDefaults.AlignToGrid);
        sut.PromptForProjectOnStartup.Should().Be(SettingsDefaults.PromptForProjectOnStartup);
        sut.ActiveTheme.Should().Be(SettingsDefaults.ActiveTheme);
        sut.ActiveKeymap.Should().Be(SettingsDefaults.ActiveKeymap);
        sut.ActiveLanguage.Should().Be(SettingsDefaults.ActiveLanguage);
        sut.IsStandardToolbarVisible.Should().Be(SettingsDefaults.IsStandardToolbarVisible);
        sut.IsEditToolbarVisible.Should().Be(SettingsDefaults.IsEditToolbarVisible);
        sut.IsDebugToolbarVisible.Should().Be(SettingsDefaults.IsDebugToolbarVisible);
        sut.IsFormEditorToolbarVisible.Should().Be(SettingsDefaults.IsFormEditorToolbarVisible);
        sut.IsMinimapVisible.Should().Be(SettingsDefaults.IsMinimapVisible);
        sut.ReloadFilesChangedOutsideIde.Should().Be(SettingsDefaults.ReloadFilesChangedOutsideIde);
    }

    [Fact]
    public void Defaults_ReloadFilesChangedOutsideIde_IsTrue()
    {
        var sut = CreateSut();
        sut.ReloadFilesChangedOutsideIde.Should().BeTrue();
    }

    [Fact]
    public void ReloadFilesChangedOutsideIde_RoundTrips_ThroughSaveAndLoad()
    {
        var sut = CreateSut();
        sut.ReloadFilesChangedOutsideIde = false;
        sut.Save();

        var reloaded = CreateSut();
        reloaded.ReloadFilesChangedOutsideIde.Should().BeFalse();
    }

    [Fact]
    public void Defaults_RequireVariableDeclaration_IsFalse()
    {
        var sut = CreateSut();
        sut.RequireVariableDeclaration.Should().BeFalse();
    }

    [Fact]
    public void Defaults_AutoListMembers_IsTrue()
    {
        var sut = CreateSut();
        sut.AutoListMembers.Should().BeTrue();
    }

    [Fact]
    public void Defaults_AutoQuickInfo_IsTrue()
    {
        var sut = CreateSut();
        sut.AutoQuickInfo.Should().BeTrue();
    }

    [Fact]
    public void Defaults_AutoIndent_IsTrue()
    {
        var sut = CreateSut();
        sut.AutoIndent.Should().BeTrue();
    }

    [Fact]
    public void Defaults_TabWidth_Is4()
    {
        var sut = CreateSut();
        sut.TabWidth.Should().Be(4);
    }

    [Fact]
    public void Defaults_FormatOnSave_IsTrue()
    {
        var sut = CreateSut();
        sut.FormatOnSave.Should().BeTrue();
    }

    [Fact]
    public void Defaults_ShowGrid_IsTrue()
    {
        var sut = CreateSut();
        sut.ShowGrid.Should().BeTrue();
    }

    [Fact]
    public void Defaults_GridWidth_Is8()
    {
        var sut = CreateSut();
        sut.GridWidth.Should().Be(8);
    }

    [Fact]
    public void Defaults_GridHeight_Is8()
    {
        var sut = CreateSut();
        sut.GridHeight.Should().Be(8);
    }

    [Fact]
    public void Defaults_AlignToGrid_IsTrue()
    {
        var sut = CreateSut();
        sut.AlignToGrid.Should().BeTrue();
    }

    [Fact]
    public void Defaults_PromptForProjectOnStartup_IsTrue()
    {
        var sut = CreateSut();
        sut.PromptForProjectOnStartup.Should().BeTrue();
    }

    // ── Round-trip ──────────────────────────────────────────────

    [Fact]
    public void ActiveLanguage_RoundTrips_ThroughSaveAndLoad()
    {
        var sut = CreateSut();
        sut.ActiveLanguage = "de";
        sut.Save();

        var reloaded = CreateSut();
        reloaded.ActiveLanguage.Should().Be("de");
    }

    // ── Validation ──────────────────────────────────────────────

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(33, 32)]
    [InlineData(100, 32)]
    public void TabWidth_Clamps_OutOfRange(int input, int expected)
    {
        var sut = CreateSut();
        sut.TabWidth = input;
        sut.TabWidth.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(32, 32)]
    public void TabWidth_Accepts_ValidRange(int input, int expected)
    {
        var sut = CreateSut();
        sut.TabWidth = input;
        sut.TabWidth.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(65, 64)]
    public void GridWidth_Clamps_OutOfRange(int input, int expected)
    {
        var sut = CreateSut();
        sut.GridWidth = input;
        sut.GridWidth.Should().Be(expected);
    }

    // ── PropertyChanged ─────────────────────────────────────────

    [Fact]
    public void PropertyChanged_Fires_WhenSettingChanges()
    {
        var sut = CreateSut();
        using var monitor = sut.Monitor();

        sut.TabWidth = 8;

        monitor.Should().RaisePropertyChangeFor(s => s.TabWidth);
    }

    [Fact]
    public void PropertyChanged_DoesNotFire_WhenSameValue()
    {
        var sut = CreateSut();
        sut.AutoListMembers = true; // already true
        using var monitor = sut.Monitor();

        sut.AutoListMembers = true;

        monitor.Should().NotRaisePropertyChangeFor(s => s.AutoListMembers);
    }
}
