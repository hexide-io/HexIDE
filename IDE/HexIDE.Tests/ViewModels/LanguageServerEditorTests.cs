using System.Text;
using System.Text.Json;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Tools.LanguageServers;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexIDE.Tests.ViewModels;

public sealed class LanguageServerEditorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "hexide-editor-" + Guid.NewGuid().ToString("N"));
    private string Config => Path.Combine(_directory, "lsp-servers.json");
    private string Seen => Path.Combine(_directory, "seen.json");
    private const string Entry = """{"id":"probe","extensions":[".x"],"languageId":"x","transport":"stdio","command":"server","arguments":["old"]} """;
    private static LanguageServerEntry Default => new()
    {
        Id = "bundled", Extensions = [".bas"], LanguageId = "vb6", Transport = "pipe", PipeName = "hexide.probe",
        Command = "server", Arguments = "--pipe {pipe} --pid {pid} --workspace {workspaceUri}", Priority = -100,
    };

    public LanguageServerEditorTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    private LanguageServerEditorStore Store() => new(Config, [Default],
        NullLogger<LanguageServerConfigLoader>.Instance, new LanguageServerCommandStore(Seen));

    private LanguageServerEditorViewModel Form(string? id = null, bool customise = false, Action? close = null)
    {
        var workspace = Substitute.For<ILspWorkspace>();
        workspace.Directory.Returns(_directory);
        return new LanguageServerEditorViewModel(Store(), new LocalizationService(), workspace, id, customise, close ?? (() => { }));
    }

    private static void Set(LanguageServerEditorViewModel vm, string name, string value) => vm.Fields.Single(f => f.Name == name).Text = value;

    [Fact]
    public void AddValidatesAsYouTypeAndSavesThePreviewedCommand()
    {
        var closed = false;
        var form = Form(close: () => closed = true);
        form.CanSave.Should().BeFalse();
        Set(form, "id", "probe");
        Set(form, "extensions", "[\".x\"]");
        Set(form, "languageId", "x");
        Set(form, "transport", "stdio");
        Set(form, "command", "server");
        Set(form, "arguments", "[\"--host\",\"two words\",\"\"]");
        form.CanSave.Should().BeTrue();
        form.Preview.Should().Contain("server").And.Contain("--host").And.Contain("two words");
        Set(form, "environment", "{");
        form.CanSave.Should().BeFalse();
        form.Problems.Should().Contain("JSON");
        Set(form, "environment", "{\"OPTIONS\":\"value\"}");
        form.Preview.Should().Contain("OPTIONS=value");
        form.SaveCommand.Execute(null);
        closed.Should().BeTrue();
        var store = Store();
        var saved = store.Preview(store.Read()).Entries.Single(e => e.Id == "probe");
        saved.Arguments!.List.Should().Equal("--host", "two words", "");
        new LanguageServerCommandStore(Seen).IsNewOrChanged("probe", LanguageServerConfigLoader.LaunchedCommandOf(saved)!).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditChangesOnlyTheNamedValuesIncludingNestedUnknownFieldsAndComments(bool utf16)
    {
        var original = """
            { // root comment é
              "version": 1, "futureRoot": {"x":  1},
              "servers": [
                {"id":"other", /* untouched */ "future": [ 1, 2 ], "enabled":false},
                {"id":"probe","extensions":[".x"],"languageId":"x","transport":"stdio",
                 "command": /* before */ "server" /* after */, "future": { "kept":  2 },
                 "capture": { /* nested */ "frameBytes": 4096, "future": [ 3,  4 ], },},
              ],
            }
            """;
        var encoding = utf16 ? Encoding.Unicode : new UTF8Encoding(true);
        File.WriteAllText(Config, original, encoding);
        var form = Form("probe");
        form.Fields.Single(f => f.Name == "id").IsReadOnly.Should().BeTrue();
        Set(form, "command", "replacement");
        Set(form, "capture.frameBytes", "8192");
        form.SaveCommand.Execute(null);
        File.ReadAllText(Config).Should().Be(original.Replace("\"server\"", "\"replacement\"", StringComparison.Ordinal)
            .Replace("4096", "8192", StringComparison.Ordinal));
        File.ReadAllBytes(Config).AsSpan().StartsWith(encoding.GetPreamble()).Should().BeTrue();
    }

    [Fact]
    public void CustomiseWritesOnlyChangesAndResolvesPipePlaceholdersWithoutLaunching()
    {
        var form = Form("bundled", true);
        form.CanSave.Should().BeFalse();
        Set(form, "arguments", "\"--pipe {pipe} --pid {pid} --workspace {workspaceUri} --directory {workspaceDir}\"");
        form.Preview.Should().Contain("--pipe hexide.probe").And.Contain($"--pid {Environment.ProcessId}")
            .And.Contain(LspWorkspaceUri.For(_directory)!).And.Contain(_directory).And.NotContain("{workspace");
        form.SaveCommand.Execute(null);
        using var document = LanguageServerEditorStore.Parse(File.ReadAllText(Config));
        var entry = document.RootElement.GetProperty("servers")[0];
        entry.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("id", "arguments");
        var effective = Store().Preview(Store().Read()).Entries.Single(e => e.Id == "bundled");
        effective.Priority.Should().Be(-100);
        effective.Command.Should().Be("server");
        new LanguageServerCommandStore(Seen).IsNewOrChanged("bundled", LanguageServerConfigLoader.LaunchedCommandOf(effective)!).Should().BeFalse();
    }

    [Fact]
    public void AConcurrentHandEditIsNeverOverwrittenOrRecordedAsSeen()
    {
        File.WriteAllText(Config, "{\"servers\":[" + Entry + "]}");
        var form = Form("probe");
        Set(form, "command", "replacement");
        var handEdit = File.ReadAllText(Config) + "\n// hand edit";
        File.WriteAllText(Config, handEdit);
        form.SaveCommand.Execute(null);
        File.ReadAllText(Config).Should().Be(handEdit);
        form.CanSave.Should().BeFalse();
        form.Problems.Should().Contain("changed");
        File.Exists(Seen).Should().BeFalse();
    }

    [Fact]
    public void FormCoversEveryDocumentedEntryFieldIncludingNestedDeclarations()
    {
        var names = Form().Fields.Select(f => f.Name).ToArray();
        names.Should().BeEquivalentTo("id", "displayName", "extensions", "languageId", "transport", "command", "arguments",
            "environment", "workingDirectory", "pipeName", "pipeRole", "endpoint", "websocketRole", "initializationOptions",
            "trace", "priority", "enabled", "capture.envelopeEntries", "capture.prologueEntries", "capture.payloadBytesPerConnection",
            "capture.frameBytes", "workspaceArtifact.path", "workspaceArtifact.provider", "workspaceArtifact.settings",
            "workspaceArtifact.restartOnChange", "projectImport.command", "projectImport.arguments");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"version\":999,\"servers\":[]}")]
    [InlineData("{ /* invalid")]
    public void UnusableFilesAreReportedThroughTheLoaderAndNeverWritten(string original)
    {
        File.WriteAllText(Config, original);
        var form = Form();
        Set(form, "id", "probe");
        Set(form, "enabled", "false");
        form.CanSave.Should().BeFalse();
        form.Problems.Should().NotBeNullOrWhiteSpace();
        form.SaveCommand.Execute(null);
        File.ReadAllText(Config).Should().Be(original);
    }

    [Fact]
    public void DuplicateIdsAreRejectedAndCancelDoesNotWrite()
    {
        File.WriteAllText(Config, "{\"servers\":[" + Entry + "]}");
        var original = File.ReadAllText(Config);
        var closed = false;
        var form = Form(close: () => closed = true);
        Set(form, "id", "probe");
        form.CanSave.Should().BeFalse();
        form.Problems.Should().Contain("already exists");
        form.CancelCommand.Execute(null);
        closed.Should().BeTrue();
        File.ReadAllText(Config).Should().Be(original);
    }

    [Theory]
    [InlineData("pipe", "pipeName", "hexide.remote")]
    [InlineData("websocket", "endpoint", "ws://localhost:12345/")]
    public void FormSupportsEveryTransport(string transport, string field, string endpoint)
    {
        var form = Form();
        Set(form, "id", "probe");
        Set(form, "extensions", "[\".x\"]");
        Set(form, "languageId", "x");
        Set(form, "transport", transport);
        Set(form, field, endpoint);
        form.CanSave.Should().BeTrue();
        form.Preview.Should().Be(endpoint);
        form.SaveCommand.Execute(null);
        Store().Preview(Store().Read()).Entries.Single(e => e.Id == "probe").Transport.Should().Be(transport);
    }

    [Theory]
    [InlineData("{ /* preserve */ \"version\":1}")]
    [InlineData("{ /* preserve */ \"servers\":null}")]
    public void AddCanCreateTheServersArrayWithoutRewritingTheRoot(string original)
    {
        File.WriteAllText(Config, original);
        var form = Form();
        Set(form, "id", "disabled");
        Set(form, "enabled", "false");
        form.CanSave.Should().BeTrue();
        form.SaveCommand.Execute(null);
        File.ReadAllText(Config).Should().Contain("/* preserve */");
        Store().Preview(Store().Read()).Entries.Single(e => e.Id == "disabled").Enabled.Should().BeFalse();
    }

    [Fact]
    public void EditCanClearOptionalValuesWithoutTouchingOtherFields()
    {
        File.WriteAllText(Config, "{\"servers\":[" + Entry + "]}");
        var form = Form("probe");
        Set(form, "arguments", "null");
        Set(form, "initializationOptions", "{\"nested\": [true, {\"count\": 2}]}");
        form.SaveCommand.Execute(null);
        var saved = Store().Preview(Store().Read()).Entries.Single(e => e.Id == "probe");
        saved.Arguments.Should().BeNull();
        saved.InitializationOptions!.Value.GetProperty("nested")[1].GetProperty("count").GetInt32().Should().Be(2);
    }

    [Fact]
    public void EditCanRepairAFieldWithTheWrongJsonType()
    {
        File.WriteAllText(Config, "{\"servers\":[" + Entry.Replace("\"arguments\":[\"old\"]", "\"priority\":\"high\"", StringComparison.Ordinal) + "]}");
        var form = Form("probe");
        form.CanSave.Should().BeFalse();
        Set(form, "priority", "20");
        form.CanSave.Should().BeTrue();
        form.SaveCommand.Execute(null);
        Store().Preview(Store().Read()).Entries.Single(e => e.Id == "probe").Priority.Should().Be(20);
    }

    [Fact]
    public void EditCanRepairANonStringCommandWithoutCrashingTheForm()
    {
        File.WriteAllText(Config, "{\"servers\":[" + Entry.Replace("\"command\":\"server\"", "\"command\":42", StringComparison.Ordinal) + "]}");
        var form = Form("probe");
        form.CanSave.Should().BeFalse();
        Set(form, "command", "repaired");
        form.CanSave.Should().BeTrue();
        form.SaveCommand.Execute(null);
        Store().Preview(Store().Read()).Entries.Single(e => e.Id == "probe").Command.Should().Be("repaired");
    }

    [Fact]
    public void MissingWorkspaceLeavesPlaceholdersVisibleAndExplainsWhy()
    {
        var form = new LanguageServerEditorViewModel(Store(), new LocalizationService(), null, "bundled", true, () => { });
        Set(form, "displayName", "Custom server");
        form.CanSave.Should().BeTrue();
        form.Preview.Should().Contain("{workspaceUri}").And.Contain("project folder");
    }
}
