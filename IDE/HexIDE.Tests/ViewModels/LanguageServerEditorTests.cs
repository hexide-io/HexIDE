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
    public void FieldJsonLocationsCountLinesAndCharactersFromOne()
    {
        var form = Form("bundled", true);
        Set(form, "environment", "{\n\"é\": !}");
        form.Problems.Should().Contain("environment:2:6").And.NotContain("BytePositionInLine").And.NotContain("LineNumber");
    }

    [Fact]
    public void EditInsertsANewPropertyOnItsOwnLineInDocumentedOrder()
    {
        var original = """
            {"servers": [
              {
                "id": "probe",
                "enabled": false // retained
              }
            ]}
            """;
        File.WriteAllText(Config, original);
        var form = Form("probe");
        Set(form, "displayName", "New");
        form.SaveCommand.Execute(null);
        File.ReadAllText(Config).ReplaceLineEndings("\n").Should().Be(original.ReplaceLineEndings("\n")
            .Replace("\"enabled\": false", "\"displayName\": \"New\",\n    \"enabled\": false", StringComparison.Ordinal));
    }

    [Fact]
    public void NewEntryUsesInlineArraysWhenItsNeighboursDo()
    {
        File.WriteAllText(Config, "{\"servers\": [\n  " + Entry + "\n]}");
        var form = Form();
        Set(form, "id", "new");
        Set(form, "extensions", "[\".a\", \".b\"]");
        Set(form, "enabled", "false");
        form.SaveCommand.Execute(null);
        File.ReadAllText(Config).Should().Contain("\"extensions\": [\".a\", \".b\"]").And.Contain(Entry);
    }

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

    [Theory]
    [InlineData("arguments")]
    [InlineData("environment")]
    [InlineData("initializationOptions")]
    [InlineData("workspaceArtifact.settings")]
    public void InvalidJsonNamesTheFieldLabel(string name)
    {
        var form = Form("bundled", true);
        Set(form, name, "{");
        form.CanSave.Should().BeFalse();
        form.Problems.Should().Contain(name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomiseInheritsUntouchedFieldsAndWritesNullForClearedValues(bool clear)
    {
        var form = Form("bundled", true);
        Set(form, "displayName", "Custom");
        if (clear) Set(form, "arguments", "");
        form.CanSave.Should().BeTrue();
        form.SaveCommand.Execute(null);
        using var document = LanguageServerEditorStore.Parse(File.ReadAllText(Config));
        var entry = document.RootElement.GetProperty("servers")[0];
        if (clear) entry.GetProperty("arguments").ValueKind.Should().Be(JsonValueKind.Null);
        else entry.TryGetProperty("arguments", out _).Should().BeFalse();
        var effective = Store().Load().Entries.Single(e => e.Id == "bundled");
        if (clear) effective.Arguments.Should().BeNull();
        else effective.Arguments!.Text.Should().Be(Default.Arguments!.Text);
    }

    [Fact]
    public void HelpExplainsInheritanceAndClearing()
    {
        new LocalizationService().GetString("Str.Tool.LanguageServers.Editor.Help").ToLowerInvariant()
            .Should().Contain("inherit").And.Contain("clear");
    }

    [Theory]
    [InlineData("  ")]
    [InlineData("\t")]
    public void AddUsesNeighbourIndentationAndDocumentedPropertyOrder(string indent)
    {
        var other = indent + indent + """{"id":"other", /* untouched */ "enabled":false}""";
        var original = "{\n" + indent + "\"servers\": [\n" + other + "\n" + indent + "]\n}";
        File.WriteAllText(Config, original);
        var form = Form();
        Set(form, "displayName", "New");
        Set(form, "enabled", "false");
        Set(form, "id", "new");
        Set(form, "arguments", """["one", "two"]""");
        form.SaveCommand.Execute(null);
        var saved = File.ReadAllText(Config).ReplaceLineEndings("\n");
        var expected = "{\n" + indent + indent + indent + "\"id\": \"new\",\n"
            + indent + indent + indent + "\"arguments\": [\n"
            + indent + indent + indent + indent + "\"one\",\n"
            + indent + indent + indent + indent + "\"two\"\n"
            + indent + indent + indent + "],\n"
            + indent + indent + indent + "\"displayName\": \"New\",\n"
            + indent + indent + indent + "\"enabled\": false\n"
            + indent + indent + "}";
        saved.Should().Contain(expected);
        saved.Should().Contain(other);
        saved.Replace(expected + ",\n" + indent + indent, "", StringComparison.Ordinal)
            .Should().Be(original.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void AddAndEditWriteTextAsTyped()
    {
        var form = Form();
        Set(form, "id", "typed");
        Set(form, "extensions", """[".x"]""");
        Set(form, "languageId", "x");
        Set(form, "transport", "stdio");
        Set(form, "command", "server");
        Set(form, "displayName", "C++ <check> & café");
        Set(form, "environment", """{"PATH_EXTRA": "a&b <c> + café"}""");
        form.CanSave.Should().BeTrue(form.Problems);
        form.SaveCommand.Execute(null);
        var saved = File.ReadAllText(Config);
        saved.Should().Contain("\"C++ <check> & café\"").And.Contain("\"a&b <c> + café\"").And.NotContain(@"\u");

        var edit = Form("typed");
        edit.Fields.Single(f => f.Name == "environment").Text.Should().Contain("a&b <c> + café");
        Set(edit, "workingDirectory", "C:/Projets/été");
        edit.CanSave.Should().BeTrue(edit.Problems);
        edit.SaveCommand.Execute(null);
        File.ReadAllText(Config).Should().Contain("\"C:/Projets/été\"").And.NotContain(@"\u");
    }

    [Fact]
    public void TcpRoleIsEditableValidatedAndSaved()
    {
        var form = Form();
        form.Fields.Select(f => f.Name).Should().Contain("tcpRole");
        Set(form, "id", "tcp");
        Set(form, "extensions", """[".x"]""");
        Set(form, "languageId", "x");
        Set(form, "transport", "tcp");
        Set(form, "endpoint", "tcp://localhost:6005");
        Set(form, "tcpRole", "invalid");
        form.CanSave.Should().BeFalse();
        Set(form, "tcpRole", "listen");
        form.CanSave.Should().BeTrue();
        form.SaveCommand.Execute(null);
        Store().Load().Entries.Single(e => e.Id == "tcp").TcpRole.Should().Be("listen");
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
        names.Should().Equal("id", "extensions", "languageId", "transport", "command", "arguments",
            "environment", "workingDirectory", "pipeName", "pipeRole", "endpoint", "tcpRole", "websocketRole", "displayName", "initializationOptions",
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
