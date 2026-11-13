using System.Xml.Linq;
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
    public void ChatDoesNotPassLocalizedValuesIntoOtherLocalizedSentences()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Join(root.FullName, "IDE", "HexIDE.AIChat"))) root = root.Parent;
        root.Should().NotBeNull();
        foreach (var file in Directory.EnumerateFiles(Path.Join(root!.FullName, "IDE", "HexIDE.AIChat", "Views"), "*.cs"))
        {
            var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(file), cancellationToken: TestContext.Current.CancellationToken)
                .GetRoot(TestContext.Current.CancellationToken);
            static bool IsLocalizedCall(InvocationExpressionSyntax call) =>
                call.Expression.ToString() is "ChatStrings.GetString" or "ChatStrings.Format";
            var localizedVariables = syntax.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Where(v => v.Initializer?.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(IsLocalizedCall) == true)
                .Select(v => v.Identifier.ValueText).ToHashSet();
            foreach (var call in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
                         .Where(c => c.Expression.ToString() == "ChatStrings.Format"))
            foreach (var argument in call.ArgumentList.Arguments.Skip(1))
            {
                argument.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(IsLocalizedCall)
                    .Should().BeEmpty($"{file}: localized sentences must be complete");
                argument.DescendantNodes().OfType<IdentifierNameSyntax>()
                    .Where(i => localizedVariables.Contains(i.Identifier.ValueText))
                    .Should().BeEmpty($"{file}: localized fragments must not be passed through variables");
            }
        }
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
