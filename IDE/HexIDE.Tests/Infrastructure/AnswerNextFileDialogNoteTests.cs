using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// answer_next_file_dialog's note says which picker an answer goes to, and that an open picker can't take it (#3913).
/// An answer armed behind another was described as "the next file dialog", and arming under an open native picker
/// said nothing about the picker, which every other tool that can meet one reports.
/// </summary>
public class AnswerNextFileDialogNoteTests : IDisposable
{
    // The tool reads nothing from the IDE.
    private readonly HexIdeTools tools = new(null!);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public AnswerNextFileDialogNoteTests()
    {
        ScriptedFileDialogs.Clear();
        ScriptedFileDialogs.TakeUnreportedRefusal();
    }

    // The answers are process-wide, so one left armed is taken by the next test that opens a picker (#3745).
    public void Dispose()
    {
        ScriptedFileDialogs.Clear();
        ScriptedFileDialogs.TakeUnreportedRefusal();
    }

    [Fact]
    public async Task An_answer_armed_behind_others_names_its_place_in_the_queue()
    {
        (await tools.AnswerNextFileDialogAsync(path: @"C:\x\Form1.frm", ct: Ct)).Note
            .Should().Be(@"The next file dialog will return 'C:\x\Form1.frm'. Nothing has been created there.");
        (await tools.AnswerNextFileDialogAsync(path: @"C:\x\Project1.vbp", ct: Ct)).Note
            .Should().Be(@"The 2nd file dialog to open from now will return 'C:\x\Project1.vbp'. The answer armed "
                         + "before it is spent first. Nothing has been created there.");
        (await tools.AnswerNextFileDialogAsync(ct: Ct)).Note
            .Should().Be("The 3rd file dialog to open from now will answer as cancelled. The 2 answers armed before it "
                         + "are spent first.");

        var several = await tools.AnswerNextFileDialogAsync(paths: [@"C:\x\a.bas", @"C:\x\b.bas"], ct: Ct);
        several.Pending.Should().Be(4);
        several.Note.Should().StartWith("The 4th file dialog to open from now will return these 2 files, in this order")
            .And.Contain("The 3 answers armed before it are spent first.");
        (await tools.AnswerNextFileDialogAsync(paths: [@"C:\x\c.bas"], ct: Ct)).Note
            .Should().Be(@"The 5th file dialog to open from now will return 'C:\x\c.bas'. The 4 answers armed before it "
                         + "are spent first. Nothing has been created there.");
    }

    [Theory]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    [InlineData(23, "23rd")]
    public async Task The_place_is_an_English_ordinal(int place, string ordinal)
    {
        for (var i = 1; i < place; i++)
            ScriptedFileDialogs.AnswerNextWith("x");

        (await tools.AnswerNextFileDialogAsync(path: "y", ct: Ct)).Note
            .Should().StartWith($"The {ordinal} file dialog to open from now will return 'y'.");
    }

    [Fact]
    public async Task Arming_under_an_open_picker_says_the_answer_cannot_reach_it()
    {
        var completion = new TaskCompletionSource<string?>();
        var picker = new WindowManager(Substitute.For<ILocalizationService>())
            .TrackNativeDialog(() => completion.Task, null, "Project1.vbp");
        try
        {
            var reply = await tools.AnswerNextFileDialogAsync(path: @"C:\x\Project1.vbp", ct: Ct);

            reply.Success.Should().BeTrue("the answer is still armed, for the next picker to open");
            reply.Note.Should().StartWith(@"The next file dialog will return 'C:\x\Project1.vbp'. Nothing has been created there. ")
                .And.Contain("An open native file picker is outside the control tree")
                .And.Contain("Arming an answer cannot answer an already open picker");
        }
        finally
        {
            completion.SetResult(null);
            await picker;
        }
    }

    [Fact]
    public async Task A_refused_arming_under_an_open_picker_names_the_picker_too()
    {
        var completion = new TaskCompletionSource<string?>();
        var picker = new WindowManager(Substitute.For<ILocalizationService>())
            .TrackNativeDialog(() => completion.Task, "Save As", "Project1.vbp");
        try
        {
            var reply = await tools.AnswerNextFileDialogAsync(path: "a", paths: ["b"], ct: Ct);

            reply.Success.Should().BeFalse();
            reply.Note.Should().StartWith("Nothing was armed. An open native file picker is outside the control tree: "
                                          + "title 'Save As', suggested file name 'Project1.vbp'.");
        }
        finally
        {
            completion.SetResult(null);
            await picker;
        }
    }

    [Fact]
    public async Task A_picker_HexIDE_gave_no_title_is_not_described_by_a_made_up_one()
    {
        var completion = new TaskCompletionSource<string?>();
        var picker = new WindowManager(Substitute.For<ILocalizationService>())
            .TrackNativeDialog(() => completion.Task, null, "Project1.vbp");
        try
        {
            ScriptedFileDialogs.NativePickerNote.Should()
                .Contain("no title from HexIDE (the operating system shows its own, such as 'Save As' or 'Open'), "
                         + "suggested file name 'Project1.vbp'")
                .And.NotContain("(not specified)");
        }
        finally
        {
            completion.SetResult(null);
            await picker;
        }
    }
}
