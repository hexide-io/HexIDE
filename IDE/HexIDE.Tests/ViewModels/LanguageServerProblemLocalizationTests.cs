using HexIDE.Conversations;
using HexIDE.Events;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Redaction;
using HexIDE.Tools.LanguageServers;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexIDE.Tests.ViewModels;

public class LanguageServerProblemLocalizationTests
{
    public static TheoryData<string> Configurations => new()
    {
        """{"servers":[{"id":"s","transport":"stdio","command":"server","extensions":[".x"],"languageId":"x"}]}""",
        "null",
        "{",
        """{"version":2}""",
        """{"servers":[{}]}""",
        """{"servers":[{"id":"s"}]}""",
        """{"servers":[{"id":"s","transport":"bad"}]}""",
        """{"servers":[{"id":"s","enabled":false,"typo":1}]}""",
        """{"servers":[{"id":"s","enabled":false,"typo1":1,"typo2":2}]}""",
        """{"servers":[{"id":"s","enabled":false,"trace":"bad"}]}""",
        """{"servers":[{"id":"s","enabled":false,"transport":"pipe","pipeRole":"bad"}]}""",
        """{"servers":[{"id":"s","transport":"stdio","endpoint":"ws://localhost"}]}""",
        """{"servers":[{"id":"s","transport":"pipe","pipeName":"PIPE"}]}""",
        """{"arguments":{"s":["--extra"]}}""",
        """{"servers":[{"id":"s","transport":"websocket","endpoint":"ws://localhost"}],"arguments":{"s":["--extra"]}}""",
        """{"servers":[{"id":"s","transport":"stdio","command":"server","arguments":"--own"}],"arguments":{"s":["--extra"]}}""",
        """{"servers":[{"id":"s","transport":"stdio","command":"server","arguments":["--own"]}],"arguments":{"s":"--extra"}}""",
        """{"capture":{"envelopeEntries":1,"frameBytes":99999999,"globalPayloadBytes":1,"payloadBytesPerConnection":1,"prologueEntries":99999999}}""",
        """{"servers":[{"id":"s","enabled":false,"capture":{"globalPayloadBytes":1,"frameBytes":1}}]}""",
        """{"servers":[{"id":"s","enabled":false,"projectImport":{"typo":1}}]}""",
        """{"servers":[{"id":"s","enabled":false,"workspaceArtifact":{"typo":1}}]}""",
        """{"servers":[{"id":"s","enabled":false,"workspaceArtifact":{"path":"file"}}]}""",
        """{"servers":[{"id":"s","enabled":false,"workspaceArtifact":{"path":"file","provider":"missing"}}]}""",
        """{"servers":[{"id":"s","enabled":false,"workspaceArtifact":{"path":"/absolute/file","provider":"p"}}]}""",
        """{"servers":[{"id":"s","enabled":false,"workspaceArtifact":{"path":"../outside","provider":"p"}}]}""",
    };

    [AvaloniaTheory]
    [MemberData(nameof(Configurations))]
    public async Task EveryLoaderProblemUsesLocalizedProse(string json)
    {
        var file = Path.GetTempFileName();
        var seen = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, json.Replace("PIPE", new string('x', PipeNameLimit.Max + 1)));
            var result = new LanguageServerConfigLoader(file, NullLogger<LanguageServerConfigLoader>.Instance).Load([],
                new LanguageServerCommandStore(seen), new WorkspaceArtifactProviderRegistry([]));
            result.Problems.Should().NotBeEmpty();
            var registry = Substitute.For<ILanguageConnectionRegistry>();
            registry.Connections.Returns([]);
            registry.ConfigurationProblems.Returns(result.Problems);
            var loc = new LocalizationService();
            loc.Apply("bg");
            await using var capture = new ConversationLog();
            var vm = new LanguageServersToolViewModel(registry, loc, capture,
                Substitute.For<IEventBus>(), new Pseudonymiser());

            vm.Problems.Should().HaveCount(result.Problems.Count);
            for (var index = 0; index < result.Problems.Count; index++)
            {
                vm.Problems[index].Message.Should().NotBe(result.Problems[index].Message);
                vm.Problems[index].Message.Should().NotContain("Str.Tool.");
            }
            if (json == "{")
            {
                var problem = result.Problems.Single();
                vm.Problems.Single().Message.Should().Contain(file).And.Contain((string)problem.Arguments[3],
                    "the file and JSON path must survive translation");
            }
        }
        finally { File.Delete(file); File.Delete(seen); }
    }

    [AvaloniaTheory]
    [InlineData("en", "line 1, column 23")]
    [InlineData("bg", "ред 1, колона 23")]
    public async Task JsonSyntaxPositionUsesLocalizedLabels(string language, string position)
    {
        await CheckProblem("""{"servers":[{"id":"é" "enabled":false}]}""",
            language, "JsonSyntax", position, "$.servers[0]");
    }

    [AvaloniaTheory]
    [InlineData("en", 1, "Unrecognized field: colour.")]
    [InlineData("en", 2, "Unrecognized fields: colour, size.")]
    [InlineData("bg", 1, "Неразпознато поле: colour.")]
    [InlineData("bg", 2, "Неразпознати полета: colour, size.")]
    public async Task FieldCountSelectsLocalizedHeading(string language, int count, string expected)
    {
        var json = count == 1
            ? """{"servers":[{"id":"s","enabled":false,"colour":1}]}"""
            : """{"servers":[{"id":"s","enabled":false,"colour":1,"size":2}]}""";
        await CheckProblem(json, language, count == 1 ? "UnknownField" : "UnknownFields", expected);
    }

    [AvaloniaTheory]
    [InlineData("Trace", "trace", "„loud“", "„off“, „messages“, „verbose“")]
    [InlineData("PipeRole", "pipeRole", "„loud“", "„connect“, „listen“")]
    public async Task BulgarianOptionsUseTheTemplatesQuoteMarks(
        string code, string field, string requested, string options)
    {
        var json = "{\"servers\":[{\"id\":\"s\",\"enabled\":false,\"transport\":\"pipe\",\""
            + field + "\":\"loud\"}]}";
        await CheckProblem(json, "bg", code, requested, options);
    }

    [AvaloniaTheory]
    [InlineData("en", "'bad'", "'stdio', 'pipe', 'websocket', 'tcp'")]
    [InlineData("bg", "„bad“", "„stdio“, „pipe“, „websocket“, „tcp“")]
    public async Task UnknownTransportOptionsUseTheTemplatesQuoteMarks(string language, string value, string options)
    {
        await CheckProblem("""{"servers":[{"id":"s","transport":"bad"}]}""",
            language, "UnknownTransport", value, options);
    }

    [AvaloniaFact]
    public async Task InvalidEscapeGuidanceUsesLocalizedProseAndKeepsTheLocation()
    {
        await CheckProblem("""{"servers":[{"id":"s","command":"C:\Utilities\server.exe"}]}""",
            "bg", "JsonSyntaxEscape", "ред 1, колона", "Използвайте наклонени черти",
            "$.servers[0].command", "C:/tools/new/...");
    }

    [AvaloniaFact]
    public async Task ControlWarningUsesLocalizedProseAndKeepsTheVariableAndPath()
    {
        await CheckProblem("""{"servers":[{"id":"s","enabled":false,"environment":{"SERVER_PATH":"C:\tools\new"}}]}""",
            "bg", "PathControlWarning", "контролен знак", "environment.SERVER_PATH", @"C:\tools\new");
    }

    private static async Task CheckProblem(string json, string language, string code, params string[] expected)
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, json);
            var result = new LanguageServerConfigLoader(file, NullLogger<LanguageServerConfigLoader>.Instance).Load([]);
            var registry = Substitute.For<ILanguageConnectionRegistry>();
            registry.Connections.Returns([]);
            registry.ConfigurationProblems.Returns(result.Problems);
            var loc = new LocalizationService();
            loc.Apply(language);
            await using var capture = new ConversationLog();
            var vm = new LanguageServersToolViewModel(registry, loc, capture,
                Substitute.For<IEventBus>(), new Pseudonymiser());
            foreach (var text in expected)
                vm.Problems.Single(p => p.Code == code || p.Code == code + "s").Message.Should().Contain(text);
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public async Task SwitchingToBulgarianTranslatesProblemAndKeepsQuotedJson()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, """{"servers":[{"id":"hexide.vb6","enabled":false}]}""");
            var result = new LanguageServerConfigLoader(file, NullLogger<LanguageServerConfigLoader>.Instance).Load([]);
            var registry = Substitute.For<ILanguageConnectionRegistry>();
            registry.Connections.Returns([]);
            registry.ConfigurationProblems.Returns(result.Problems);
            var loc = new LocalizationService();
            loc.Apply("en");
            await using var capture = new ConversationLog();
            var vm = new LanguageServersToolViewModel(registry, loc, capture,
                Substitute.For<IEventBus>(), new Pseudonymiser());
            vm.Problems.Single().Message.Should().Contain("is switched off");

            loc.Apply("bg");

            vm.Problems.Single().Message.Should().Contain("е изключен");
            vm.Problems.Single().Message.Should().Contain("hexide.vb6").And.Contain("\"enabled\": false");
            vm.ReportText.Should().Contain("е изключен").And.Contain("\"enabled\": false");
            result.Problems.Single().Message.Should().Contain("is switched off", "logs keep English");
            loc.Apply("en");
            vm.Problems.Single().Message.Should().Be(result.Problems.Single().Message);
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public async Task JapaneseFieldListDoesNotAssembleEnglishPlurals()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, """{"servers":[{"id":"s","enabled":false,"typo1":1,"typo2":2}]}""");
            var result = new LanguageServerConfigLoader(file, NullLogger<LanguageServerConfigLoader>.Instance).Load([]);
            var registry = Substitute.For<ILanguageConnectionRegistry>();
            registry.Connections.Returns([]);
            registry.ConfigurationProblems.Returns(result.Problems);
            var loc = new LocalizationService();
            loc.Apply("ja");
            await using var capture = new ConversationLog();
            var vm = new LanguageServersToolViewModel(registry, loc, capture,
                Substitute.For<IEventBus>(), new Pseudonymiser());

            vm.Problems.Single(p => p.Kind == LanguageServerConfigProblemKind.UnrecognisedField).Message
                .Should().Contain("フィールド").And.Contain("typo1, typo2").And.NotContain("Unrecognised");
        }
        finally { File.Delete(file); }
    }
}
