using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    // ── The ai-chat delta's scenarios ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheAssistantsEditIsRefused()
    {
        editor.TrySetContent("Form1", Arg.Any<string>(), Arg.Is<string?>(p => p == null)).Returns(new AddinEditResult(false, HeaderRefusal));

        var reply = await Execute("apply_edit", new { file_name = "Form1", content = FormFile.Replace("Orders", "Sales") });

        reply["success"]!.GetValue<bool>().Should().BeFalse("the model must not be told an edit landed that did not");
        reply["error"]!.GetValue<string>().Should().Be(HeaderRefusal, "the model is told why, so it can send the code alone instead");
        _ = editor.DidNotReceiveWithAnyArgs().SetContent(default!, default!);
    }

    [Fact]
    public async Task ApplyingABlockTheIdeRefuses()
    {
        editor.TrySetContent("Form1", Arg.Any<string>(), "Orders").Returns(new AddinEditResult(false, HeaderRefusal));
        var chat = new ChatViewModel(host, settingsDir);

        await chat.ApplyToActiveFileAsync(FormFile.Replace("Orders", "Sales"));

        var shown = chat.Messages.Last();
        shown.IsError.Should().BeTrue();
        shown.RawContent.Should().Contain("Not applied to Form1").And.Contain(HeaderRefusal);
        _ = editor.Received(1).TrySetContent("Form1", FormFile.Replace("Orders", "Sales"), "Orders");
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
    public async Task ABlockTheIdeAcceptsIsReportedInTheTranscript()
    {
        editor.TrySetContent("Form1", "Option Explicit", "Orders").Returns(new AddinEditResult(true, HeaderKept: true));
        var chat = new ChatViewModel(host, settingsDir);

        await chat.ApplyToActiveFileAsync("Option Explicit");

        chat.Messages.Last().IsError.Should().BeFalse();
        chat.Messages.Last().RawContent.Should().Be("Applied to Form1.");
    }

    [Fact]
    public async Task ABlockAppliedWithNoCodeWindowActiveSaysSo()
    {
        editor.GetActiveDocument().Returns((AddinDocument?)null);
        var chat = new ChatViewModel(host, settingsDir);

        await chat.ApplyToActiveFileAsync("Option Explicit");

        chat.Messages.Last().IsError.Should().BeTrue();
        chat.Messages.Last().RawContent.Should().Contain("no code window is active");
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

    // ── What the model is shown ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheActiveFileIsItsWholeFileAndNamesItsProject()
    {
        var reply = await Execute("get_active_file", new { });

        reply["Content"]!.GetValue<string>().Should().Be(FormFile);
        reply["Project"]!.GetValue<string>().Should().Be("Orders");
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
