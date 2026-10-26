using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Providers;

namespace HexIDE.Tests.LspClient;

public sealed class RdcoreProjectProviderTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdproj-root", "Project");

    private static WorkspaceProjectFile File(string relative, WorkspaceProjectFileKind kind) =>
        new(relative.Replace('/', '\\'), Path.GetFullPath(Path.Combine(Root, relative)), kind);

    private static JsonDocument Produce(WorkspaceProjectSnapshot project, string? settings = null)
    {
        using var configured = settings is null ? null : JsonDocument.Parse(settings);
        var output = new RdcoreProjectProvider().Produce(
            new WorkspaceArtifactContext(Root, project, configured?.RootElement.Clone()));
        output.Should().NotBeNull();
        return JsonDocument.Parse(output!);
    }

    [Fact]
    public void ListsEveryCodeFileAsAModuleRelativeToTheRoot()
    {
        var project = new WorkspaceProjectSnapshot("Probe", Path.Combine(Root, "Probe.vbp"), [
            File("frmMain.frm", WorkspaceProjectFileKind.Form),
            File("Main.bas", WorkspaceProjectFileKind.Module),
            File("Classes/Thing.cls", WorkspaceProjectFileKind.Class),
            File("../Shared/Common.bas", WorkspaceProjectFileKind.Module),
            File("ucDial.ctl", WorkspaceProjectFileKind.UserControl),
            File("probe.c", WorkspaceProjectFileKind.Carried),
            File("odd.xyz", WorkspaceProjectFileKind.Unknown)], []);
        using var json = Produce(project);
        var info = json.RootElement.GetProperty("ProjectInfo");
        info.GetProperty("Modules").EnumerateArray().Select(m => m.GetProperty("RelativeUri").GetString()).Should().Equal(
            "frmMain.frm", "Main.bas", Path.Combine("Classes", "Thing.cls"), Path.Combine("..", "Shared", "Common.bas"),
            "ucDial.ctl");
        info.GetProperty("OtherFiles").GetArrayLength().Should().Be(0);
        info.GetProperty("Name").GetString().Should().Be("Probe");
    }

    [Fact]
    public void KeysArePascalCaseBecauseRDCoreReadsThemCaseSensitively()
    {
        var project = new WorkspaceProjectSnapshot("Probe", null, [File("Main.bas", WorkspaceProjectFileKind.Module)],
            [new("stdole")], new Dictionary<string, short> { ["DEBUG"] = 1 });
        using var json = Produce(project);
        json.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("Version", "Configuration", "ProjectInfo");
        var info = json.RootElement.GetProperty("ProjectInfo");
        info.EnumerateObject().Select(p => p.Name).Should().Equal(
            "Name", "References", "Modules", "OtherFiles", "Folders", "PrecompilerConstants");
        info.GetProperty("References")[0].EnumerateObject().Select(p => p.Name).Should().Equal(
            "Name", "AbsolutePath", "Guid", "Major", "Minor", "TypeLibInfoPath", "IsUnremovable");
        info.GetProperty("Modules")[0].EnumerateObject().Select(p => p.Name).Should().Equal("RelativeUri");
    }

    [Theory]
    [InlineData(null, "0.1.0")]
    [InlineData("""{"version":"1.0.0"}""", "1.0.0")]
    [InlineData("""{"version":3}""", "0.1.0")]
    public void VersionIsTheOldestRDCoreReportsUnlessSettingsSayOtherwise(string? settings, string expected)
    {
        using var json = Produce(new("Probe", null, [File("Main.bas", WorkspaceProjectFileKind.Module)], []), settings);
        json.RootElement.GetProperty("Version").GetString().Should().Be(expected);
    }

    [Fact]
    public void ReferencesCarryWhateverTheProjectRecorded()
    {
        var guid = Guid.Parse("00020430-0000-0000-C000-000000000046");
        var stdole = Path.Combine(Path.GetTempPath(), "stdole2.tlb");
        var project = new WorkspaceProjectSnapshot("Probe", null, [File("Main.bas", WorkspaceProjectFileKind.Module)], [
            new("stdole", guid, 2, 0, 0, stdole),
            new("NameOnly"),
            new("")]);
        using var json = Produce(project);
        var references = json.RootElement.GetProperty("ProjectInfo").GetProperty("References");
        references.GetArrayLength().Should().Be(2);
        references[0].GetProperty("Name").GetString().Should().Be("stdole");
        references[0].GetProperty("Guid").GetGuid().Should().Be(guid);
        references[0].GetProperty("Major").GetInt32().Should().Be(2);
        references[0].GetProperty("Minor").GetInt32().Should().Be(0);
        references[0].GetProperty("AbsolutePath").GetString().Should().Be(stdole);
        references[0].GetProperty("IsUnremovable").GetBoolean().Should().BeFalse();
        references[1].GetProperty("Guid").ValueKind.Should().Be(JsonValueKind.Null);
        references[1].GetProperty("Major").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void CompilationArgumentsBecomePrecompilerConstants()
    {
        var arguments = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase) { ["Trace"] = -2, ["DEBUG"] = 1 };
        using var json = Produce(new("Probe", null, [File("Main.bas", WorkspaceProjectFileKind.Module)], [], arguments));
        json.RootElement.GetProperty("ProjectInfo").GetProperty("PrecompilerConstants").EnumerateObject()
            .Select(p => (p.Name, p.Value.GetString())).Should().Equal(("DEBUG", "1"), ("Trace", "-2"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NothingToAnalyseIsNothingToDescribe(bool carried)
    {
        WorkspaceProjectFile[] files = carried ? [File("probe.c", WorkspaceProjectFileKind.Carried)] : [];
        new RdcoreProjectProvider().Produce(new WorkspaceArtifactContext(Root, new("Probe", null, files, [])))
            .Should().BeNull();
    }

    [Fact]
    public void SameProjectGivesTheSameBytes()
    {
        var project = new WorkspaceProjectSnapshot("Probe", null, [File("Main.bas", WorkspaceProjectFileKind.Module)], [],
            new Dictionary<string, short> { ["B"] = 2, ["A"] = 1 });
        var provider = new RdcoreProjectProvider();
        provider.Produce(new(Root, project)).Should().Be(provider.Produce(new(Root, project with { })));
    }
}
