using System.Xml.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Avalonia;
using Avalonia.Threading;
using HexIDE.Addins;
using HexIDE.AIChat;
using HexIDE.AIChat.Views;
using HexIDE.Localization;

namespace HexIDE.Tests.AIChat;

public class ChatTextLocalizationTests
{
    [AvaloniaTheory]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    [InlineData("Error")]
    [InlineData("NoActiveCodeWindow")]
    [InlineData("Applied")]
    [InlineData("NotAppliedTo")]
    [InlineData("NotApplied")]
    [InlineData("UsingTool")]
    [InlineData("ToolFailed")]
    [InlineData("AllowReplace")]
    [InlineData("AllowReplaceUnnamed")]
    [InlineData("AppliedUnnamed")]
    [InlineData("NotAppliedToUnnamed")]
    [InlineData("NotAppliedToNoReason")]
    [InlineData("NotAppliedToUnnamedNoReason")]
    [InlineData("ToolFailedNoReason")]
    [InlineData("AllowRunProject")]
    [InlineData("AllowStopProject")]
    [InlineData("AllowTool")]
    public void ChatKeysResolveInEnglish(string name)
    {
        var key = "Str.AIChat." + name;
        new LocalizationService().GetStringFrom("en", key).Should().NotBe(key).And.NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("Approve", "Content")]
    [InlineData("Reject", "Content")]
    [InlineData("Apply", "Content")]
    [InlineData("Send", "Content")]
    [InlineData("New", "Content")]
    [InlineData("InputPlaceholder", "PlaceholderText")]
    [InlineData("NewTip", "ToolTip.Tip")]
    public void ChatViewUsesDynamicResources(string key, string attribute)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Join(root.FullName, "IDE", "HexIDE.AIChat"))) root = root.Parent;
        root.Should().NotBeNull();
        var view = System.Xml.Linq.XDocument.Load(Path.Join(root!.FullName, "IDE", "HexIDE.AIChat", "Views", "ChatView.axaml"));
        view.Descendants().Attributes(attribute).Select(a => a.Value)
            .Should().Contain("{DynamicResource Str.AIChat." + key + "}");
    }

    [AvaloniaFact]
    public async Task ApprovalPromptUsesGermanAndPreservesFileName()
    {
        var localization = new LocalizationService();
        localization.Apply("de");
        var directory = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Join(directory, "chat-settings.json"), """{"baseUrl":"http://localhost/v1"}""");
            using var client = new ChatClient(new HttpClient(new ApprovalHandler()));
            var chat = new ChatViewModel(Substitute.For<IHexIdeHost>(), directory, client) { InputText = "edit" };
            var send = chat.SendAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            ChatMessageViewModel? approval;
            while ((approval = chat.Messages.FirstOrDefault(m => m.IsApproval)) is null)
                await Task.Delay(10, timeout.Token);
            var prompt = approval.RawContent;
            approval.Reject();
            await send.WaitAsync(timeout.Token);
            prompt.Should().Be("Der KI erlauben, den gesamten Inhalt von „Module1.bas“ zu ersetzen?");
            chat.SessionLog.Single(m => m.Role == "tool").Content.Should().Contain("The user declined this action.");
        }
        finally
        {
            localization.Apply("en");
            Directory.Delete(directory, true);
        }
    }

    private sealed class ApprovalHandler : HttpMessageHandler
    {
        private int requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var data = requests++ == 0
                ? """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call","function":{"name":"apply_edit","arguments":"{\"file_name\":\"Module1.bas\"}"}}]},"finish_reason":null}]}"""
                : """{"choices":[{"delta":{"content":"done"},"finish_reason":null}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: " + data + "\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream")
            });
        }
    }

    [AvaloniaTheory]
    [InlineData(true, "✔ Genehmigt")]
    [InlineData(false, "✗ Abgelehnt")]
    public void ApprovalOutcomeUsesGerman(bool approved, string expected)
    {
        var localization = new LocalizationService();
        localization.Apply("de");
        try
        {
            var message = new ChatMessageViewModel("assistant", isApproval: true);
            if (approved) message.Approve(); else message.Reject();
            message.ApprovalOutcome.Should().Be(expected);
        }
        finally { localization.Apply("en"); }
    }

    [AvaloniaTheory]
    [InlineData("apply_edit", "{}", "AllowReplaceUnnamed", "Replace a file")]
    [InlineData("apply_edit", "{\"file_name\":\"Module1.bas\"}", "AllowReplace", "Replace «Module1.bas»")]
    [InlineData("run_project", "{}", "AllowRunProject", "Run")]
    [InlineData("stop_project", "{}", "AllowStopProject", "Stop")]
    [InlineData("other_tool", "{}", "AllowTool", "Tool other_tool")]
    public void ApprovalDescriptionsUseResources(string tool, string arguments, string key, string expected)
    {
        Application.Current!.Resources["Str.AIChat." + key] = key switch
        {
            "AllowReplace" => "Replace «{0}»", "AllowReplaceUnnamed" => "Replace a file", "AllowTool" => "Tool {0}",
            "AllowRunProject" => "Run", _ => "Stop"
        };
        var prompt = typeof(ChatViewModel).GetMethod("DescribeWriteTool", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [new ToolCall("id", tool, arguments)]);
        prompt.Should().Be(expected);
    }

    [AvaloniaTheory]
    [InlineData("apply_edit", "{\"success\":true}", "Applied unnamed")]
    [InlineData("apply_edit", "{\"success\":false}", "Refused unnamed without reason")]
    [InlineData("run_project", "{\"success\":false}", "Failed run_project without reason")]
    [InlineData("stop_project", "{\"error\":\"raw detail\"}", "Failed stop_project: raw detail")]
    public void WriteOutcomesUseResources(string tool, string result, string expected)
    {
        var resources = Application.Current!.Resources;
        resources["Str.AIChat.Applied"] = "Applied {0}";
        resources["Str.AIChat.NotAppliedTo"] = "Refused {0}: {1}";
        resources["Str.AIChat.ToolFailed"] = "Failed {0}: {1}";
        resources["Str.AIChat.AppliedUnnamed"] = "Applied unnamed";
        resources["Str.AIChat.NotAppliedToUnnamedNoReason"] = "Refused unnamed without reason";
        resources["Str.AIChat.ToolFailedNoReason"] = "Failed {0} without reason";
        var outcome = (ChatMessageViewModel)typeof(ChatViewModel)
            .GetMethod("DescribeWriteOutcome", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [new ToolCall("id", tool, "{}"), result])!;
        outcome.RawContent.Should().Be(expected);
    }

    [AvaloniaTheory]
    [InlineData("{}", "{\"success\":true}", "AppliedUnnamed")]
    [InlineData("{\"file_name\":\"Module1.bas\"}", "{\"success\":true}", "Applied")]
    [InlineData("{}", "{\"success\":false,\"error\":\"detail\"}", "NotAppliedToUnnamed")]
    [InlineData("{\"file_name\":\"Module1.bas\"}", "{\"success\":false,\"error\":\"detail\"}", "NotAppliedTo")]
    [InlineData("{}", "{\"success\":false}", "NotAppliedToUnnamedNoReason")]
    [InlineData("{\"file_name\":\"Module1.bas\"}", "{\"success\":false}", "NotAppliedToNoReason")]
    public void EditOutcomeSelectsAWholeSentence(string arguments, string result, string key)
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        try
        {
            Application.Current!.Resources["Str.AIChat." + key] = "WHOLE SENTENCE";
            var outcome = (ChatMessageViewModel)typeof(ChatViewModel)
                .GetMethod("DescribeWriteOutcome", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [new ToolCall("id", "apply_edit", arguments), result])!;
            outcome.RawContent.Should().Be("WHOLE SENTENCE");
        }
        finally { localization.Apply("en"); }
    }

    [Fact]
    public void ChatDoesNotPassLocalizedFragmentsIntoOtherLocalizedSentences()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Join(root.FullName, "IDE", "HexIDE.AIChat"))) root = root.Parent;
        root.Should().NotBeNull();
        var localization = new LocalizationService();
        string? English(string key) => localization.GetStringFrom("en", key) is var value && value != key ? value : null;
        foreach (var file in Directory.EnumerateFiles(Path.Join(root!.FullName, "IDE", "HexIDE.AIChat", "Views"), "*.cs"))
        {
            var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(file), cancellationToken: TestContext.Current.CancellationToken)
                .GetRoot(TestContext.Current.CancellationToken);
            ComposedFragments(syntax, English).Should().BeEmpty($"{file}: localized sentences must be complete");
        }
    }

    // The guard's one exemption has to bite: a localized value inserted mid-sentence still fails, wherever it is.
    [Fact]
    public void A_localized_value_mid_sentence_is_refused_and_one_after_a_closing_colon_is_not()
    {
        var syntax = CSharpSyntaxTree.ParseText("""
            class C
            {
                void M(string tool)
                {
                    var reason = ChatStrings.Format("Str.Probe.Reason", 1);
                    var shown = ChatStrings.Format("Str.Probe.Frame", tool, reason);
                    var middle = ChatStrings.Format("Str.Probe.Middle", reason, tool);
                    var word = ChatStrings.Format("Str.Probe.Middle", ChatStrings.GetString("Str.Probe.Word"), tool);
                }
            }
            """, cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken);
        var english = new Dictionary<string, string>
        {
            ["Str.Probe.Reason"] = "The server isn't running.",
            ["Str.Probe.Frame"] = "`{0}` failed: {1}",
            ["Str.Probe.Middle"] = "Could not {0} the file {1}.",
        };

        ComposedFragments(syntax, key => english.GetValueOrDefault(key)).Should().Equal(
            "Str.Probe.Middle {0}: reason", "Str.Probe.Middle {0}: ChatStrings.GetString(\"Str.Probe.Word\")");
    }

    [Theory]
    [InlineData("`{0}` failed: {1}", 1, true)]
    [InlineData("Not applied to {0}: {1}", 1, true)]
    [InlineData("Not applied to the file: {0}", 0, true)]
    [InlineData("Error: {0}.", 0, true)]
    [InlineData("Not applied to {0}: {1}", 0, false)]
    [InlineData("Could not open {0} today", 0, false)]
    [InlineData("{0} failed: see {1} for details", 1, false)]
    [InlineData("Failed: {1}!", 1, false)]
    [InlineData("Failed: {10}", 1, false)]
    [InlineData("Failed {1}", 1, false)]
    public void A_whole_sentence_may_fill_only_a_placeholder_ending_the_sentence_after_a_colon(string english, int index, bool allowed) =>
        PlaceholderTakesAWholeSentence(english, index).Should().Be(allowed);

    /// <summary>
    /// A localized whole sentence may fill a placeholder only where the composing sentence's English ends with it after a
    /// colon, as <c>: {n}</c> or <c>: {n}.</c>, so the inserted text stands as its own sentence in every language (#4332).
    /// Anywhere else it would be a translated fragment inside another translated sentence, whose word order other languages
    /// don't share. This is the guard's only exemption: a rule about the sentence, not a list of call sites.
    /// </summary>
    private static bool PlaceholderTakesAWholeSentence(string english, int index) =>
        english.EndsWith($": {{{index}}}", StringComparison.Ordinal) || english.EndsWith($": {{{index}}}.", StringComparison.Ordinal);

    /// <summary>
    /// Each <c>ChatStrings.Format</c> argument that carries a localized value, directly or through a variable, into a
    /// placeholder <see cref="PlaceholderTakesAWholeSentence"/> doesn't allow, as "key {n}: argument".
    /// </summary>
    private static List<string> ComposedFragments(SyntaxNode root, Func<string, string?> english)
    {
        static bool IsLocalizedCall(InvocationExpressionSyntax call) =>
            call.Expression.ToString() is "ChatStrings.GetString" or "ChatStrings.Format";
        var localizedVariables = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Where(v => v.Initializer?.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(IsLocalizedCall) == true)
            .Select(v => (v.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault(), v.Identifier.ValueText)).ToHashSet();
        var found = new List<string>();
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                     .Where(c => c.Expression.ToString() == "ChatStrings.Format"))
        {
            var key = call.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal ? literal.Token.ValueText : null;
            var sentence = key is null ? null : english(key);
            foreach (var (argument, index) in call.ArgumentList.Arguments.Skip(1).Select((a, i) => (a, i)))
            {
                if (sentence is not null && PlaceholderTakesAWholeSentence(sentence, index))
                    continue;
                var localized = argument.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(IsLocalizedCall)
                    || argument.DescendantNodes().OfType<IdentifierNameSyntax>().Any(i =>
                        localizedVariables.Contains((i.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault(), i.Identifier.ValueText)));
                if (localized)
                    found.Add($"{key ?? call.ArgumentList.Arguments[0].ToString()} {{{index}}}: {argument}");
            }
        }
        return found;
    }

    [AvaloniaTheory]
    [InlineData("no-window", "NO WINDOW")]
    [InlineData("accepted", "APPLIED Module1")]
    [InlineData("refused", "REFUSED Module1: detail")]
    [InlineData("exception", "NOT APPLIED boom")]
    public async Task ApplyMessagesUseResourcesAndModelNotesStayEnglish(string scenario, string expected)
    {
        var resources = Application.Current!.Resources;
        resources["Str.AIChat.NoActiveCodeWindow"] = "NO WINDOW";
        resources["Str.AIChat.Applied"] = "APPLIED {0}";
        resources["Str.AIChat.NotAppliedTo"] = "REFUSED {0}: {1}";
        resources["Str.AIChat.NotApplied"] = "NOT APPLIED {0}";
        var directory = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var host = Substitute.For<IHexIdeHost>();
            if (scenario != "no-window") host.Editor.GetActiveDocument().Returns(
                new AddinDocument("Module1", "Module1.bas", "old", AddinDocumentKind.Module));
            if (scenario == "exception") host.Editor.TrySetContent("Module1", "new", null)
                .Returns(Task.FromException<AddinEditResult>(new InvalidOperationException("boom")));
            else host.Editor.TrySetContent("Module1", "new", null)
                .Returns(new AddinEditResult(scenario == "accepted", "detail"));
            var chat = new ChatViewModel(host, directory);
            await chat.ApplyToActiveFileAsync("new");
            chat.Messages.Single().RawContent.Should().Be(expected);
            if (scenario != "no-window") chat.SessionLog.Single().Content.Should().StartWith(
                "[Note from the IDE] The developer applied a code block from this chat to Module1");
        }
        finally { Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public async Task ToolStatusUsesResourcesAndResultStaysEnglish()
    {
        Application.Current!.Resources["Str.AIChat.UsingTool"] = "TOOL {0}";
        Application.Current.Resources["Str.AIChat.ToolFailed"] = "FAILED {0}: {1}";
        var directory = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var chat = new ChatViewModel(Substitute.For<IHexIdeHost>(), directory);
            await chat.RunToolCallAsync(new ToolCall("id", "stop_project", "{}"));
            Dispatcher.UIThread.RunJobs();
            chat.Messages.First().RawContent.Should().Be("TOOL stop_project");
            chat.SessionLog.Single().Content.Should().Contain("No project is currently running");
        }
        finally { Directory.Delete(directory, true); }
    }

    [AvaloniaTheory]
    [InlineData("ServerToolNotRunning", "The language server 'Probe' isn't running, or no longer offers AI Chat tools.", "LOCAL Probe")]
    [InlineData("ServerToolListFailed", "The language server 'Probe' didn't list its tools, so nothing was called.", "LOCAL Probe")]
    [InlineData("ServerToolNotOffered", "The language server 'Probe' doesn't offer a tool called 'read'.", "LOCAL Probe read")]
    [InlineData("ServerToolCallFailed", "The language server 'Probe' didn't complete the call: raw exception", "LOCAL Probe raw exception")]
    [InlineData("ServerToolsUnavailable", "Language servers' AI Chat tools are available only to HexIDE's own add-ins.", "LOCAL")]
    [InlineData("server-error", "server's own error", "server's own error")]
    public async Task ServerRefusalUsesResourcesAndResultStaysEnglish(string scenario, string english, string shown)
    {
        var key = "Str.AIChat." + scenario;
        var resources = Application.Current!.Resources;
        var hadKey = resources.TryGetValue(key, out var previous);
        resources[key] = scenario switch
        {
            "ServerToolNotOffered" or "ServerToolCallFailed" => "LOCAL {0} {1}",
            "ServerToolsUnavailable" => "LOCAL",
            _ => "LOCAL {0}",
        };
        resources["Str.AIChat.ToolFailed"] = "FAILED {0}: {1}";
        var directory = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var tool = new ServerChatTool("probe", "Probe", "read", "", JsonDocument.Parse("{}").RootElement.Clone(), false);
            Task<JsonElement?> Request(string method, JsonElement? arguments, TimeSpan? timeout, CancellationToken token)
            {
                if (scenario == "ServerToolListFailed" || (scenario == "ServerToolCallFailed" && method == "call"))
                    throw new InvalidOperationException("raw exception");
                return Task.FromResult<JsonElement?>(JsonDocument.Parse(method == "list"
                    ? scenario == "ServerToolNotOffered" ? """{"tools":[]}""" : """{"tools":[{"name":"read","annotations":{"readOnlyHint":true,"destructiveHint":false}}]}"""
                    : """{"isError":true,"content":[{"type":"text","text":"server's own error"}]}""").RootElement.Clone());
            }
            IServerChatTools tools = scenario == "ServerToolsUnavailable"
                ? ServerChatToolsUnavailable.Instance
                : new ServerChatToolsService(() => scenario == "ServerToolNotRunning" ? [] :
                    [new ChatToolsServer("probe", "Probe", ServerChatToolsPolicy.Hints,
                        new ServerChatToolsCapability("list", "call", null), Request)], NullLogger.Instance);
            // The assistant saw the tool before the server stopped/listing failed. List it for this send,
            // then let the real host service decide what the call does now.
            var offered = Substitute.For<IServerChatTools>();
            offered.ListAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ServerChatTool>>([tool]));
            offered.CallAsync(Arg.Any<ServerChatTool>(), Arg.Any<JsonElement?>(), Arg.Any<ServerChatToolApproval>(),
                Arg.Any<CancellationToken>()).Returns(call => tools.CallAsync(call.ArgAt<ServerChatTool>(0),
                    call.ArgAt<JsonElement?>(1), call.ArgAt<ServerChatToolApproval>(2), call.ArgAt<CancellationToken>(3)));
            var host = Substitute.For<IHexIdeHost>();
            host.ServerChatTools.Returns(offered);
            File.WriteAllText(Path.Join(directory, "chat-settings.json"), """{"baseUrl":"http://localhost/v1"}""");
            var handler = new ServerChatToolsLoopTests.ToolCallHandler("lsp1_read", "{}");
            using var client = new ChatClient(new HttpClient(handler));
            var chat = new ChatViewModel(host, directory, client) { InputText = "go" };
            await chat.SendAsync().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();

            chat.Messages.Single(m => m.IsError).RawContent.Should().Be("FAILED Probe: read: " + shown);
            var sessionResult = chat.SessionLog.Single(m => m.Role == "tool").Content;
            JsonNode.Parse(sessionResult)!["result"]!.GetValue<string>().Should().Be(english);
            var sentResult = handler.Bodies[1]["messages"]!.AsArray().Single(m => m!["role"]!.GetValue<string>() == "tool")!["content"]!.GetValue<string>();
            JsonNode.Parse(sentResult)!["result"]!.GetValue<string>().Should().Be(english);
        }
        finally
        {
            if (hadKey) resources[key] = previous!; else resources.Remove(key);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task LocalizedServerRefusalIsCutAfterTranslation()
    {
        var resources = Application.Current!.Resources;
        const string key = "Str.AIChat.ServerToolCallFailed";
        var hadKey = resources.TryGetValue(key, out var previous);
        resources[key] = new string('L', 310) + " {0}: {1}";
        resources["Str.AIChat.ToolFailed"] = "FAILED {0}: {1}";
        var directory = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var tools = Substitute.For<IServerChatTools>();
            tools.CallAsync(Arg.Any<ServerChatTool>(), Arg.Any<JsonElement?>(), Arg.Any<ServerChatToolApproval>(),
                Arg.Any<CancellationToken>()).Returns(new ServerChatToolResult(true, "English")
                    { ReasonKey = key, ReasonArguments = ["Probe", "raw exception"] });
            var host = Substitute.For<IHexIdeHost>();
            host.ServerChatTools.Returns(tools);
            var chat = new ChatViewModel(host, directory);
            var tool = new ServerChatTool("probe", "Probe", "read", "", JsonDocument.Parse("{}").RootElement.Clone(), false);
            await chat.RunServerToolCallAsync(new ToolCall("id", "lsp1_read", "{}"), tool, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();

            chat.Messages.Last().RawContent.Should().Be("FAILED Probe: read: " + new string('L', 300) + "…");
            JsonNode.Parse(chat.SessionLog.Single().Content)!["result"]!.GetValue<string>().Should().Be("English");
        }
        finally
        {
            if (hadKey) resources[key] = previous!; else resources.Remove(key);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public void ErrorPrefixUsesResourcesAndExceptionTextIsPreserved()
    {
        Application.Current!.Resources["Str.AIChat.Error"] = "ERROR {0}";
        var directory = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var chat = new ChatViewModel(Substitute.For<IHexIdeHost>(), directory);
            typeof(ChatViewModel).GetMethod("ShowError", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(chat, [new InvalidOperationException("raw exception")]);
            chat.Messages.Single().RawContent.Should().Be("ERROR raw exception");
        }
        finally { Directory.Delete(directory, true); }
    }

}
