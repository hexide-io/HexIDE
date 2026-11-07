using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// A Make reports what it ended with, before any message box saying so, so invoke_menu_item can reply with the program
/// it wrote or why it wrote none (#2671). The menu command discards Make's task, and the reply used to say "Invoked".
/// </summary>
public class MakeOutcomesTests : IDisposable
{
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private readonly IEventBus events = Substitute.For<IEventBus>();
    private readonly ILocalizationService localization = Substitute.For<ILocalizationService>();
    private readonly string ide = Directory.CreateTempSubdirectory("hexide-make-").FullName;
    // A box that is never answered: an outcome that waited on it would never arrive.
    private readonly TaskCompletionSource<MessageBoxResult> unanswered = new();

    public MakeOutcomesTests()
    {
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        windows.MessageBox(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>())
            .ReturnsForAnyArgs(unanswered.Task);
    }

    public void Dispose() => Directory.Delete(ide, true);

    private ProjectService Service() => new(
        () => throw new InvalidOperationException(), windows, events, Substitute.For<IProjectManager>(),
        Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(),
        Substitute.For<IUserSidecarService>(), new FileBaselineStore(), localization,
        Substitute.For<IHeaderRefresher>()) { RuntimeDirectory = ide };

    private void Runner(string content = "the runner")
    {
        Directory.CreateDirectory(Path.Join(ide, "standalone"));
        File.WriteAllText(Path.Join(ide, "standalone", ProjectService.StandaloneRunnerName()!), content);
    }

    private static Task Make(ProjectService service) =>
        service.MakeProject(new ProjectDefinition(VBProjectType.EXE, "Project1"));

    private static async Task<MakeOutcomes.Outcome> Ended(MakeOutcomes.Make make) =>
        await make.Ended.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_made_program_is_reported_with_its_path_and_size_and_whether_it_wrote_over_one()
    {
        Runner("twelve bytes");
        var exe = Path.Join(ide, "Made.exe");
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(exe);

        await Make(Service());
        var first = await Ended(MakeOutcomes.Latest!);
        // The second Make finds the first's archive and asks before replacing it (#2504); answer Yes, as a user would.
        windows.MessageBox(Arg.Is<string>(text => text.StartsWith("Str.Tool.ProtocolInspector.ReplaceFiles")), Arg.Any<string?>(),
            MessageBoxButtons.YesNo, Arg.Any<MessageBoxIcon>()).Returns(MessageBoxResult.Yes);
        await Make(Service());
        var second = await Ended(MakeOutcomes.Latest!);

        first.Should().Match<MakeOutcomes.Outcome>(o =>
            o.ProgramPath == exe && o.ProgramSize == 12 && !o.Replaced && o.WhyNot == null
            && o.ProjectPath == Path.Join(ide, "Made.dll") && o.ProjectSize > 0);
        second.Replaced.Should().BeTrue("the second Make wrote over the first's program");
        MakeReply.Describe(first).Should().Be($"Made {exe} (12 bytes). It is the standalone runner, which carries .NET, "
                                              + "and it runs the project zipped beside it in "
                                              + $"{Path.Join(ide, "Made.dll")} ({first.ProjectSize.ToString("N0", CultureInfo.InvariantCulture)} bytes).");
        MakeReply.Describe(second).Should().Contain("(12 bytes), writing over the file that was there. It is the standalone");
    }

    // The second Make finds the first's archive and asks before replacing it. A No is its own reason, not a cancelled save
    // dialog, which it used to be reported as (#2718).
    [Fact]
    public async Task A_No_to_replacing_the_archive_is_reported_as_that_with_no_box()
    {
        Runner();
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(Path.Join(ide, "Made.exe"));
        await Make(Service());
        await Ended(MakeOutcomes.Latest!);
        windows.MessageBox(Arg.Is<string>(text => text.StartsWith("Str.Tool.ProtocolInspector.ReplaceFiles")), Arg.Any<string?>(),
            MessageBoxButtons.YesNo, Arg.Any<MessageBoxIcon>()).Returns(MessageBoxResult.No);

        Func<Task> second = () => Make(Service());
        await second.Should().ThrowAsync<OperationCanceledException>();
        var outcome = await Ended(MakeOutcomes.Latest!);

        var archive = Path.Join(ide, "Made.dll");
        outcome.Should().Match<MakeOutcomes.Outcome>(o =>
            o.WhyNot == $"replacing {archive}, which was already there, was declined, so nothing was written" && !o.BoxOpen);
        MakeReply.Describe(outcome).Should().Be(
            $"No program was made: replacing {archive}, which was already there, was declined, so nothing was written.");
    }

    // While the question is open the reply says Make waits on it, at once, rather than waiting out its limit and saying
    // Make is still writing (#2718).
    [Fact]
    public async Task A_replace_question_nobody_answers_is_reported_as_where_Make_waits()
    {
        Runner();
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(Path.Join(ide, "Made.exe"));
        await Make(Service());
        await Ended(MakeOutcomes.Latest!);

        _ = Make(Service());
        var make = MakeOutcomes.Latest!;
        var reply = await MakeReply.AwaitAsync(make, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(10));

        make.Current.Should().Be(MakeOutcomes.Stage.Asking);
        reply.Should().Be(MakeReply.Asking(Path.Join(ide, "Made.dll")));
        (await MakeReply.AfterAnswerAsync(make, TestContext.Current.CancellationToken))
            .Should().BeNull("an action that didn't answer the box leaves Make asking, and has nothing to add");

        // An interact that answers it is told what Make did then.
        unanswered.SetResult(MessageBoxResult.Yes);
        (await MakeReply.AfterAnswerAsync(make, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(10)))
            .Should().StartWith($"Made {Path.Join(ide, "Made.exe")} (").And.Contain("writing over the file that was there");
    }

    [Fact]
    public async Task A_missing_runner_is_reported_while_its_box_is_still_open()
    {
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(Path.Join(ide, "Made.exe"));

        _ = Make(Service());
        var outcome = await Ended(MakeOutcomes.Latest!);

        outcome.WhyNot.Should().Be($"the standalone runner, {ProjectService.StandaloneRunnerName()}, isn't in "
                                   + $"{Path.Join(ide, "standalone")}, so nothing was written to "
                                   + $"{Path.Join(ide, "Made.exe")}, which the save dialog gave");
        outcome.BoxOpen.Should().BeTrue();
        MakeReply.Describe(outcome).Should().EndWith(
            "which the save dialog gave. A message box saying so is open, and Make waits on it: answer it with interact.");
    }

    [Fact]
    public async Task A_cancelled_save_dialog_is_reported_with_no_box()
    {
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns((string?)null);

        Func<Task> make = () => Make(Service());
        await make.Should().ThrowAsync<OperationCanceledException>();
        var outcome = await Ended(MakeOutcomes.Latest!);

        outcome.Should().Match<MakeOutcomes.Outcome>(o =>
            o.WhyNot == "the save dialog was cancelled, so nothing was written" && !o.BoxOpen);
    }

    [Fact]
    public async Task A_failure_is_reported_with_its_message_before_the_box_that_shows_it()
    {
        events.When(x => x.Publish(Arg.Any<HexIDE.Events.ApplyAllUnsavedChangesEvent>()))
            .Do(_ => throw new IOException("failure detail"));

        _ = Make(Service());
        var outcome = await Ended(MakeOutcomes.Latest!);

        outcome.Should().Match<MakeOutcomes.Outcome>(o => o.WhyNot == "Make failed: failure detail" && o.BoxOpen);
    }

    // A save dialog nothing answered stays open, and nothing in the tree reaches it, so the reply says so rather than
    // waiting out its limit.
    [Fact]
    public async Task A_save_dialog_nobody_answers_is_reported_as_where_Make_waits()
    {
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(new TaskCompletionSource<string?>().Task);

        _ = Make(Service());
        var make = MakeOutcomes.Latest!;
        var reply = await MakeReply.AwaitAsync(make, TestContext.Current.CancellationToken);

        make.Current.Should().Be(MakeOutcomes.Stage.AtPicker);
        reply.Should().Be(MakeReply.AtPicker);
    }

    [Fact]
    public async Task Each_Make_is_numbered_so_a_caller_can_tell_its_own_from_an_earlier_one()
    {
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns((string?)null);
        var before = MakeOutcomes.Started;

        try { await Make(Service()); } catch (OperationCanceledException) { }

        MakeOutcomes.Latest!.Number.Should().BeGreaterThan(before);
    }
}
