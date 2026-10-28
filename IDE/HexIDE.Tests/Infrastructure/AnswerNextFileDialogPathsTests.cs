using Avalonia.Platform.Storage;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// answer_next_file_dialog arms several files as one answer, for an Open that lets a person select them (#2408). It
/// used to arm one path, so a multi-select Open could only ever be answered with one file.
/// </summary>
public class AnswerNextFileDialogPathsTests
{
    // The tool reads nothing from the IDE.
    private readonly HexIdeTools tools = new(null!);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public AnswerNextFileDialogPathsTests()
    {
        ScriptedFileDialogs.Clear();
        ScriptedFileDialogs.TakeUnreportedRefusal();
    }

    [Fact]
    public async Task Several_paths_are_armed_as_one_answer_and_listed()
    {
        var reply = await tools.AnswerNextFileDialogAsync(paths: [@"C:\x\a.bas", @"C:\x\b.bas"], ct: Ct);

        reply.Success.Should().BeTrue();
        reply.Paths.Should().Equal([@"C:\x\a.bas", @"C:\x\b.bas"]);
        reply.Path.Should().BeNull("Path is the one file a dialog returns, and this answer returns two");
        reply.Pending.Should().Be(1);
        reply.Note.Should().StartWith("The next file dialog will return these 2 files, in this order");
    }

    [Fact]
    public async Task A_path_is_listed_as_the_one_file_and_a_cancel_as_none()
    {
        (await tools.AnswerNextFileDialogAsync(path: @"C:\x\a.bas", ct: Ct)).Paths.Should().Equal([@"C:\x\a.bas"]);
        (await tools.AnswerNextFileDialogAsync(ct: Ct)).Paths.Should().BeNull();
    }

    [Theory]
    [InlineData(@"C:\x\a.bas", new[] { @"C:\x\b.bas" }, "not both")]
    [InlineData(null, new string[0], "at least one file")]
    [InlineData(null, new[] { "a", "" }, "none of them blank")]
    public async Task A_path_and_paths_together_or_an_empty_or_blank_path_is_refused_with_nothing_armed(
        string? path, string[] paths, string because)
    {
        var reply = await tools.AnswerNextFileDialogAsync(path, paths, Ct);

        reply.Success.Should().BeFalse();
        reply.Error.Should().Contain(because);
        reply.Note.Should().Be("Nothing was armed.");
        ScriptedFileDialogs.Pending.Should().Be(0);
    }

    [Fact]
    public async Task An_Open_that_allows_several_returns_them_all()
    {
        await tools.AnswerNextFileDialogAsync(paths: [@"C:\x\a.bas", @"C:\x\b.bas"], ct: Ct);

        var picked = await new WindowManager(Substitute.For<ILocalizationService>())
            .OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true });

        picked.Should().Equal([@"C:\x\a.bas", @"C:\x\b.bas"]);
    }

    [Fact]
    public async Task An_Open_that_takes_one_file_answers_several_as_cancelled_and_the_next_reply_says_so()
    {
        await tools.AnswerNextFileDialogAsync(paths: ["a", "b"], ct: Ct);

        var picked = await new WindowManager(Substitute.For<ILocalizationService>())
            .OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false });

        picked.Should().BeNull();
        (await tools.ClearFileDialogAnswersAsync(Ct)).Note.Should().Be("Nothing was armed. Earlier, an Open picker that "
            + "takes one file took an answer of 2 paths, which it cannot take, so it answered as cancelled.");
    }

    [Fact]
    public async Task A_save_picker_answers_several_as_cancelled_and_arming_again_says_so()
    {
        await tools.AnswerNextFileDialogAsync(paths: ["a", "b"], ct: Ct);

        var saved = await new WindowManager(Substitute.For<ILocalizationService>())
            .SaveFilePickerAsync(new FilePickerSaveOptions());

        saved.Should().BeNull();
        (await tools.AnswerNextFileDialogAsync(path: "c", ct: Ct)).Note.Should().EndWith(" Earlier, a save picker took an "
            + "answer of 2 paths, which it cannot take, so it answered as cancelled.");
    }

    [Fact]
    public void Paths_is_optional_on_the_wire()
    {
        var tool = ToolSource.Tools.Single(t => t.Name == "answer_next_file_dialog");

        tool.Parameters.Should().Contain(p => p.Name == "paths" && p.Optional);
    }
}
