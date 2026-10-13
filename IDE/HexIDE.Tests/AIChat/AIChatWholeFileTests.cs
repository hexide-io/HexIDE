using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using HexIDE.Addins;
using HexIDE.AIChat;
using HexIDE.AIChat.Views;

namespace HexIDE.Tests.AIChat;

/// <summary>
/// The AI Chat add-in now that a document's content is its whole file (hexide-io/HexIDE#273 task 3.19): what the
/// model is told, and what it and the developer are told when the IDE refuses a write.
/// </summary>
/// <remarks>
/// Driven through a substitute host, because what is under test is the add-in's use of the surface; the surface's
/// own answers are proved in <c>AddinWholeFileTests</c>.
/// </remarks>
public class AIChatWholeFileTests : IDisposable
{
    private const string FormFile =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Orders\"\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n" +
        "Option Explicit\r\n" +
        "Private Sub Form_Load()\r\n" +
        "    Undeclared = 1\r\n" +
        "End Sub\r\n";

    private const string HeaderRefusal =
        "The content would change the document's header (its designer block, class header or leading Attribute "
        + "lines), which only the IDE changes.";

    private readonly IHexIdeHost host = Substitute.For<IHexIdeHost>();
    private readonly IEditorAccess editor = Substitute.For<IEditorAccess>();
    private readonly IDiagnosticsAccess diagnostics = Substitute.For<IDiagnosticsAccess>();
    private readonly IProjectAccess projects = Substitute.For<IProjectAccess>();
    private readonly string settingsDir = Path.Join(Path.GetTempPath(), "hexide-aichat-" + Guid.NewGuid().ToString("N"));

    public AIChatWholeFileTests()
    {
        Directory.CreateDirectory(settingsDir);
        host.Editor.Returns(editor);
        host.Diagnostics.Returns(diagnostics);
        host.Project.Returns(projects);
        editor.GetActiveDocument().Returns(new AddinDocument("Form1", @"C:\p\Form1.frm", FormFile, AddinDocumentKind.Form, "Orders"));
    }

    public void Dispose()
    {
        try { Directory.Delete(settingsDir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private Task<JsonObject> Execute(string tool, object arguments) =>
        new IdeToolExecutor(host)
            .ExecuteAsync(new ToolCall("call_1", tool, JsonSerializer.Serialize(arguments)))
            .ContinueWith(t => (JsonObject)JsonNode.Parse(t.Result)!, TaskScheduler.Default);

    private static ToolCall Call(string tool, object arguments) => new("call_1", tool, JsonSerializer.Serialize(arguments));

    /// <summary>The loaded projects as the add-in surface lists them: Orders (Form1, Module1) and Stock (Module1).</summary>
    private void TwoProjectsLoaded() =>
        projects.GetProjects().Returns(
        [
            new AddinProjectInfo("Orders", @"C:\p\Orders.vbp",
            [
                new AddinFileInfo("Form1", @"C:\p\Form1.frm", AddinDocumentKind.Form, "Orders"),
                new AddinFileInfo("Module1", @"C:\p\Module1.bas", AddinDocumentKind.Module, "Orders"),
            ]),
            new AddinProjectInfo("Stock", @"C:\s\Stock.vbp",
            [
                new AddinFileInfo("Module1", @"C:\s\Module1.bas", AddinDocumentKind.Module, "Stock"),
            ]),
        ]);

    private static JsonObject ToolResult(ConversationMessage message)
    {
        message.Role.Should().Be("tool");
        return (JsonObject)JsonNode.Parse(message.Content)!;
    }

    // ── The ai-chat delta's scenarios ───────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task TheAssistantsEditIsRefused()
    {
        // Both halves of the requirement: the assistant is told, through the tool result, and so is the developer
        // who approved the edit, in the transcript. The transcript said only "Using tool" until the 3.19 review.
        editor.TrySetContent("Form1", Arg.Any<string>(), Arg.Is<string?>(p => p == null)).Returns(new AddinEditResult(false, HeaderRefusal));
        var chat = new ChatViewModel(host, settingsDir);

        await chat.RunToolCallAsync(Call("apply_edit", new { file_name = "Form1", content = FormFile.Replace("Orders", "Sales") }));
        Dispatcher.UIThread.RunJobs();

        var told = ToolResult(chat.SessionLog.Last());
        told["success"]!.GetValue<bool>().Should().BeFalse("the model must not be told an edit landed that did not");
        told["error"]!.GetValue<string>().Should().Be(HeaderRefusal, "the model is told why, so it can send the code alone instead");

        var shown = chat.Messages.Last();
        shown.IsError.Should().BeTrue("the developer approved this edit and must see that it did not land");
        shown.RawContent.Should().Be($"Not applied to Form1: {HeaderRefusal}");

        // The member that answers only true or false would let a refusal pass for success, so it is not the one used.
        _ = editor.Received(1).TrySetContent("Form1", FormFile.Replace("Orders", "Sales"), null);
        _ = editor.DidNotReceive().SetContent(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task ApplyingABlockTheIdeRefuses()
    {
        // Both halves again: the developer sees it, and the assistant is told, where before only the transcript said
        // and the assistant's next turn went on as though its code were in the file.
        editor.TrySetContent("Form1", Arg.Any<string>(), "Orders").Returns(new AddinEditResult(false, HeaderRefusal));
        var chat = new ChatViewModel(host, settingsDir);

        await chat.ApplyToActiveFileAsync(FormFile.Replace("Orders", "Sales"));

        var shown = chat.Messages.Last();
        shown.IsError.Should().BeTrue();
        shown.RawContent.Should().Contain("Not applied to Form1").And.Contain(HeaderRefusal);
        _ = editor.Received(1).TrySetContent("Form1", FormFile.Replace("Orders", "Sales"), "Orders");

        var note = chat.SessionLog.Last();
        note.FromIde.Should().BeTrue();
        note.Role.Should().Be("user", "the only role that can carry a note with no tool call to answer");
        note.Content.Should().Contain("Form1 (project Orders)").And.Contain("refused").And.Contain(HeaderRefusal);
    }

    // ── apply_edit and the Apply button ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnEditTheIdeAcceptsIsReportedAsApplied()
    {
        editor.TrySetContent("Form1", "Option Explicit\r\n", null).Returns(new AddinEditResult(true, HeaderKept: true));

        var reply = await Execute("apply_edit", new { file_name = "Form1", content = "Option Explicit\r\n" });

        reply["success"]!.GetValue<bool>().Should().BeTrue();
        reply["headerKept"]!.GetValue<bool>().Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task AnApprovedEditTheIdeAcceptsSaysSoInTheTranscript()
    {
        editor.TrySetContent("Form1", "Option Explicit\r\n", null).Returns(new AddinEditResult(true, HeaderKept: true));
        var chat = new ChatViewModel(host, settingsDir);

        await chat.RunToolCallAsync(Call("apply_edit", new { file_name = "Form1", content = "Option Explicit\r\n" }));
        Dispatcher.UIThread.RunJobs();

        chat.Messages.Select(m => m.RawContent).Should().Equal("Using tool: `apply_edit`", "Applied to Form1.");
        chat.Messages.Last().IsError.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task AReadToolAddsNothingToTheTranscriptButItsStatusLine()
    {
        var chat = new ChatViewModel(host, settingsDir);

        await chat.RunToolCallAsync(Call("get_active_file", new { }));
        Dispatcher.UIThread.RunJobs();

        chat.Messages.Select(m => m.RawContent).Should().Equal("Using tool: `get_active_file`");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n")]
    public async Task AnEditWithNoContentIsRefusedRatherThanEmptyingTheFile(string? content)
    {
        // The IDE takes text with no header as the code alone, so empty content would keep the header and erase
        // the code, and the reply said success. A truncated call is likelier than a model that means that.
        var arguments = content is null
            ? (object)new { file_name = "Form1" }
            : new { file_name = "Form1", content };

        var reply = await Execute("apply_edit", arguments);

        reply["success"]!.GetValue<bool>().Should().BeFalse();
        reply["error"]!.GetValue<string>().Should().Contain("No content was sent, so nothing was changed");
        _ = editor.DidNotReceiveWithAnyArgs().TrySetContent(default!, default!, default);
    }

    [Fact]
    public async Task AnEditNamesItsProjectWhenTheModelGivesOne()
    {
        // Without a project argument, a name two loaded projects share could only ever be refused.
        editor.TrySetContent("Module1", "x", "Stock").Returns(new AddinEditResult(true));

        (await Execute("apply_edit", new { file_name = "Module1", content = "x", project = "Stock" }))["success"]!
            .GetValue<bool>().Should().BeTrue();

        _ = editor.Received(1).TrySetContent("Module1", "x", "Stock");
    }

    [Fact]
    public async Task AnEditToANameTwoProjectsShareIsRefusedWithTheIdesReason()
    {
        // The startup project alone used to decide whether the name existed, and the write then went to whichever
        // project the IDE resolved: the model was told success either way.
        const string ambiguous = "'Module1' names more than one document: Shop/Module1, Stock/Module1. Pass `project` to say which.";
        editor.TrySetContent("Module1", "x", null).Returns(new AddinEditResult(false, ambiguous));

        var reply = await Execute("apply_edit", new { file_name = "Module1", content = "x" });

        reply["success"]!.GetValue<bool>().Should().BeFalse();
        reply["error"]!.GetValue<string>().Should().Be(ambiguous);
    }

    [Fact]
    public async Task ABlockTheIdeAcceptsIsReportedInTheTranscriptAndToTheAssistant()
    {
        editor.TrySetContent("Form1", "Option Explicit", "Orders").Returns(new AddinEditResult(true, HeaderKept: true));
        var chat = new ChatViewModel(host, settingsDir);

        await chat.ApplyToActiveFileAsync("Option Explicit");

        chat.Messages.Last().IsError.Should().BeFalse();
        chat.Messages.Last().RawContent.Should().Be("Applied to Form1.");
        chat.SessionLog.Last().FromIde.Should().BeTrue();
        chat.SessionLog.Last().Content.Should().Contain("accepted").And.Contain("keeping the file's own header");
    }

    [Fact]
    public async Task ABlockAppliedWithNoCodeWindowActiveSaysSo()
    {
        editor.GetActiveDocument().Returns((AddinDocument?)null);
        var chat = new ChatViewModel(host, settingsDir);

        await chat.ApplyToActiveFileAsync("Option Explicit");

        chat.Messages.Last().IsError.Should().BeTrue();
        chat.Messages.Last().RawContent.Should().Contain("no code window is active");
        chat.SessionLog.Should().BeEmpty("no change was put to the IDE, so there is nothing to tell the assistant");
    }

    [Fact]
    public async Task ABlockAppliedWhileTheAssistantIsRespondingIsToldOnlyWhereANoteMayGo()
    {
        // The Apply button stays live while the assistant works, including between a tool call and its result,
        // where a note would make the endpoint refuse the request and leave the saved history unusable.
        editor.TrySetContent("Form1", "Option Explicit", "Orders").Returns(new AddinEditResult(false, HeaderRefusal));
        var chat = new ChatViewModel(host, settingsDir) { IsResponding = true };

        await chat.ApplyToActiveFileAsync("Option Explicit");

        chat.SessionLog.Should().BeEmpty("the note is held while the assistant is responding");
        chat.FlushHeldNotes();
        chat.SessionLog.Should().ContainSingle().Which.Content.Should().Contain(HeaderRefusal);
    }

    [Fact]
    public async Task ANoteFromTheIdeIsNotShownAsTheDevelopersOwnMessageWhenTheChatReopens()
    {
        editor.TrySetContent("Form1", "Option Explicit", "Orders").Returns(new AddinEditResult(false, HeaderRefusal));
        await new ChatViewModel(host, settingsDir).ApplyToActiveFileAsync("Option Explicit");

        var reopened = new ChatViewModel(host, settingsDir);

        reopened.SessionLog.Should().ContainSingle().Which.FromIde.Should().BeTrue("the note is saved with the history");
        var shown = reopened.Messages.Should().ContainSingle().Subject;
        shown.IsUser.Should().BeFalse("the developer did not type it");
        shown.IsStatusLine.Should().BeTrue();
        shown.RawContent.Should().Contain(HeaderRefusal);
    }

    [Fact]
    public async Task ABlockWhoseWriteThrowsSaysSoRatherThanVanishing()
    {
        // The Apply button discards the task this returns, so an exception escaping it would be seen by nobody.
        editor.TrySetContent(default!, default!, default).ReturnsForAnyArgs(Task.FromException<AddinEditResult>(new InvalidOperationException("boom")));
        var chat = new ChatViewModel(host, settingsDir);

        await chat.ApplyToActiveFileAsync("Option Explicit");

        chat.Messages.Last().IsError.Should().BeTrue();
        chat.Messages.Last().RawContent.Should().Contain("boom");
    }

    // ── navigate_to ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NavigatingToADocumentTheIdeCannotFindIsReported()
    {
        const string missing = "No form or module named 'Form9' in any loaded project. Orders holds forms and UserControls Form1.";
        editor.TryNavigateTo("Form9", 3, 1, null).Returns(new AddinEditResult(false, missing));

        var reply = await Execute("navigate_to", new { file_name = "Form9", line = 3 });

        reply["success"]!.GetValue<bool>().Should().BeFalse("it used to answer success whatever happened");
        reply["error"]!.GetValue<string>().Should().Be(missing);
    }

    [Fact]
    public async Task NavigatingToALinePassesItAsTheModelGaveIt()
    {
        editor.TryNavigateTo("Form1", 8, 1, "Orders").Returns(new AddinEditResult(true));

        var reply = await Execute("navigate_to", new { file_name = "Form1", line = 8, project = "Orders" });

        reply["success"]!.GetValue<bool>().Should().BeTrue();
    }

    // ── Naming a file among every loaded project ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheFileListCoversEveryLoadedProject()
    {
        // The ambiguity refusals tell the model to pass a project; this is where it learns which there are.
        TwoProjectsLoaded();

        var files = (await Execute("list_files", new { }))["files"]!.AsArray()
            .Select(f => $"{f!["project"]!.GetValue<string>()}/{f["fileName"]!.GetValue<string>()}");

        files.Should().Equal("Orders/Form1", "Orders/Module1", "Stock/Module1");
    }

    [Fact]
    public async Task ReadingANameTwoProjectsShareIsRefusedWithBothProjects()
    {
        TwoProjectsLoaded();

        var reply = await Execute("get_file_content", new { file_name = "module1" });

        reply["error"]!.GetValue<string>().Should().Contain("more than one project").And.Contain("Orders, Stock")
            .And.Contain("Pass project");
        _ = editor.DidNotReceiveWithAnyArgs().TryNavigateTo(default!, default, default, default);
    }

    [Fact]
    public async Task ReadingAFileInTheProjectNamedOpensThatOneAndReturnsIt()
    {
        TwoProjectsLoaded();
        const string stock = "Attribute VB_Name = \"Module1\"\r\nOption Explicit\r\n";
        editor.TryNavigateTo("Module1", 1, 1, "Stock").Returns(new AddinEditResult(true));
        editor.GetActiveDocument().Returns(new AddinDocument("Module1", @"C:\s\Module1.bas", stock, AddinDocumentKind.Module, "Stock"));

        var reply = await Execute("get_file_content", new { file_name = "Module1", project = "Stock" });

        reply["content"]!.GetValue<string>().Should().Be(stock);
        reply["project"]!.GetValue<string>().Should().Be("Stock");
    }

    [Fact]
    public async Task ReadingAFileThatDidNotComeToTheFrontIsRefusedRatherThanAnsweredWithAnother()
    {
        // The file is read as the active document. When the window in front is another file, that file's content
        // came back under the name asked for.
        TwoProjectsLoaded();
        editor.TryNavigateTo("Module1", 1, 1, "Stock").Returns(new AddinEditResult(true));

        var reply = await Execute("get_file_content", new { file_name = "Module1", project = "Stock" });

        reply["content"].Should().BeNull("Form1 is in front, and its content is not Module1's");
        reply["error"]!.GetValue<string>().Should().Contain("Could not read Module1").And.Contain("holds Form1 in project Orders");
    }

    [Fact]
    public async Task ReadingAFileTheIdeCannotOpenSaysWhy()
    {
        TwoProjectsLoaded();
        editor.TryNavigateTo("Form1", 1, 1, "Orders").Returns(new AddinEditResult(false, "No code window could be opened on Orders/Form1."));

        var reply = await Execute("get_file_content", new { file_name = "Form1" });

        reply["error"]!.GetValue<string>().Should().Be("Could not open Form1: No code window could be opened on Orders/Form1.");
    }

    [Fact]
    public async Task DiagnosticsForANameTwoProjectsShareAreRefusedNotAnsweredEmpty()
    {
        // The host answers an empty list for a name it cannot resolve to one document, which reads as "no errors".
        TwoProjectsLoaded();
        diagnostics.GetFor(default!, default).ReturnsForAnyArgs([]);

        var reply = await Execute("get_diagnostics", new { file_name = "Module1" });

        reply["diagnostics"].Should().BeNull();
        reply["error"]!.GetValue<string>().Should().Contain("more than one project").And.Contain("Orders, Stock");
    }

    [Fact]
    public async Task DiagnosticsForANameNoProjectHasAreRefusedNotAnsweredEmpty()
    {
        TwoProjectsLoaded();
        diagnostics.GetFor(default!, default).ReturnsForAnyArgs([]);

        var reply = await Execute("get_diagnostics", new { file_name = "Modul1" });

        reply["error"]!.GetValue<string>().Should().Contain("File not found: Modul1").And.Contain("list_files");
    }

    [Fact]
    public async Task DiagnosticsForAFileAreAskedForInTheProjectItIsIn()
    {
        TwoProjectsLoaded();
        diagnostics.GetFor("Module1", "Stock").Returns(
            [new AddinDiagnostic("Module1", 2, 1, "Syntax error", AddinDiagnosticSeverity.Error, Project: "Stock")]);

        var reply = await Execute("get_diagnostics", new { file_name = "module1", project = "stock" });

        var item = reply["diagnostics"]!.AsArray().Should().ContainSingle().Subject!;
        item["message"]!.GetValue<string>().Should().Be("Syntax error");
        item["project"]!.GetValue<string>().Should().Be("Stock");
        diagnostics.Received(1).GetFor("Module1", "Stock");
    }

    [Fact]
    public async Task DiagnosticsWithNoFileNamedAreEveryFilesDiagnostics()
    {
        diagnostics.GetAll().Returns(
            [new AddinDiagnostic("Form1", 8, 5, "Variable not defined", AddinDiagnosticSeverity.Error, Project: "Orders")]);

        var reply = await Execute("get_diagnostics", new { });

        reply["diagnostics"]!.AsArray().Should().ContainSingle();
        diagnostics.DidNotReceiveWithAnyArgs().GetFor(default!, default);
    }

    // ── What the model is shown ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheActiveFileIsItsWholeFileAndNamesItsProject()
    {
        var reply = await Execute("get_active_file", new { });

        reply["content"]!.GetValue<string>().Should().Be(FormFile);
        reply["project"]!.GetValue<string>().Should().Be("Orders");
    }

    [Fact]
    public async Task EveryReplyNamesItsFieldsOneWay()
    {
        // get_active_file answered "Project" and list_files "project"; apply_edit's "fileName" sat beside the read
        // tools' "FileName". A model keying on one spelling found nothing under the other.
        TwoProjectsLoaded();
        editor.TrySetContent("Form1", "x", null).Returns(new AddinEditResult(true));
        diagnostics.GetAll().Returns(
            [new AddinDiagnostic("Form1", 8, 5, "Variable not defined", AddinDiagnosticSeverity.Error, "E1", "vb6", "Orders")]);

        var keys = new List<string>();
        keys.AddRange(Keys(await Execute("get_active_file", new { })));
        keys.AddRange(Keys((JsonObject)(await Execute("list_files", new { }))["files"]![0]!));
        keys.AddRange(Keys((JsonObject)(await Execute("get_diagnostics", new { }))["diagnostics"]![0]!));
        keys.AddRange(Keys(await Execute("apply_edit", new { file_name = "Form1", content = "x" })));

        keys.Should().Contain(new[] { "fileName", "project", "content", "kind", "line", "column", "message", "severity", "headerKept" })
            .And.OnlyContain(k => char.IsLower(k[0]), "one spelling for every field, the one apply_edit already used");

        static IEnumerable<string> Keys(JsonObject o) => o.Select(p => p.Key);
    }

    [Fact]
    public void ThePromptShowsTheWholeFileAndItsDiagnosticsOnTheLinesOfThatFile()
    {
        // The diagnostic is on line 8, counted from the top of the file. Before, the prompt showed the code alone,
        // so line 8 of what the model saw was a different line.
        diagnostics.GetFor("Form1", "Orders").Returns(
            [new AddinDiagnostic("Form1", 8, 5, "Variable not defined", AddinDiagnosticSeverity.Error, Project: "Orders")]);
        var chat = new ChatViewModel(host, settingsDir);

        var prompt = chat.BuildSystemPrompt();

        prompt.Should().Contain(FormFile, "the model is shown the whole file");
        prompt.Should().Contain("Line 8, Col 5: Variable not defined");
        FormFile.Split("\r\n")[8 - 1].Should().Be("    Undeclared = 1", "the diagnostic's line is a line of what the model was shown");
        prompt.Should().Contain("in project Orders");
        diagnostics.Received().GetFor("Form1", "Orders");
    }

    [Fact]
    public void ThePromptSaysTheHeaderIsReadOnlyAndWhatApplyEditTakes()
    {
        var prompt = new ChatViewModel(host, settingsDir).BuildSystemPrompt();

        prompt.Should().Contain("The header is read-only")
            .And.Contain("either the whole file with the header exactly as shown, or the code alone")
            .And.Contain("count from 1 at the top of the file, header included");
    }

    [Fact]
    public void TheToolDescriptionsSayContentIsTheWholeFile()
    {
        var descriptions = IdeToolExecutor.ToolDefinitions
            .Select(t => t!["function"]!)
            .ToDictionary(f => f["name"]!.GetValue<string>(), f => f["description"]!.GetValue<string>());

        descriptions["get_active_file"].Should().Contain("WHOLE file");
        descriptions["get_file_content"].Should().Contain("WHOLE file");
        descriptions["apply_edit"].Should().Contain("cannot be changed here").And.Contain("or the code alone");
        descriptions["navigate_to"].Should().Contain("top of the file");
        descriptions["get_diagnostics"].Should().Contain("top of the file");
    }

    // ── What leaves the machine ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheEgressNoticeSaysTheActiveFilesContentsAreSent()
    {
        var chat = new ChatViewModel(host, settingsDir);

        chat.MaybeShowEgressNotice();

        chat.Messages.Single().RawContent.Should().Contain("the active file's contents (for a form, its layout and captions too)")
            .And.NotContain("file's code");
    }

    [Fact]
    public void ADeveloperWhoAcknowledgedTheOldNoticeIsShownTheNewOneOnce()
    {
        // The old notice said "code"; more now leaves the machine than that developer was told.
        File.WriteAllText(Path.Join(settingsDir, ".ai-egress-ack"), "shown");

        var first = new ChatViewModel(host, settingsDir);
        first.MaybeShowEgressNotice();
        var second = new ChatViewModel(host, settingsDir);
        second.MaybeShowEgressNotice();

        first.Messages.Should().ContainSingle();
        second.Messages.Should().BeEmpty("the new notice is acknowledged once shown");
    }
}
