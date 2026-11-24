using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Debugging;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.IDE;

/// <summary>
/// Code from outside this computer runs only once the developer says so (#1121): what marks a file, what the question
/// covers, and what a start does while it waits on the answer.
/// </summary>
public class CodeTrustTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-trust-" + Guid.NewGuid().ToString("N"));
    private readonly HashSet<string> markedNow = new(StringComparer.OrdinalIgnoreCase);
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private readonly ILocalizationService localization = Substitute.For<ILocalizationService>();
    private readonly List<string> asked = [];
    private TaskCompletionSource<MessageBoxResult> answer = new();

    public CodeTrustTests()
    {
        Directory.CreateDirectory(directory);
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() switch
        {
            "Str.CodeTrust.Prompt" => "Run {0}? {1}",
            "Str.CodeTrust.Downloaded" => "{0} (downloaded)",
            "Str.CodeTrust.Office" => "{0} (Office)",
            var key => key,
        });
        windows.MessageBox(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(),
                Arg.Any<Window?>(), Arg.Any<bool>(), Arg.Any<MessageBoxLayout>())
            .Returns(call =>
            {
                asked.Add(call.ArgAt<string>(0));
                return answer.Task;
            });
    }

    public void Dispose()
    {
        try { Directory.Delete(directory, true); } catch (IOException) { }
    }

    private CodeTrust Trust() => new(windows, localization, path => markedNow.Contains(Path.GetFullPath(path)));

    private string File(string name)
    {
        var path = Path.Combine(directory, name);
        System.IO.File.WriteAllText(path, "x");
        return Path.GetFullPath(path);
    }

    private ProjectDefinition Project(params string[] modules)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Invoices") { AbsolutePath = File("Invoices.vbp") };
        foreach (var name in modules)
            project.AddModule(new ModuleDefinition(project, Path.GetFileNameWithoutExtension(name), ModuleKind.StandardModule)
                { AbsolutePath = File(name) });
        return project;
    }

    // --- What marks a file ---

    [Theory]
    [InlineData("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.invalid/x.zip\r\n", true)]
    [InlineData("[ZoneTransfer]\nZoneId=4\n", true)]
    [InlineData("[zonetransfer]\r\n zoneid = 3 \r\n", true)]
    [InlineData("\uFEFF[ZoneTransfer]\r\nZoneId=3\r\n", true)]
    [InlineData("[ZoneTransfer]\r\nZoneId=0\r\n", false)]
    [InlineData("[ZoneTransfer]\r\nZoneId=1\r\n", false)]
    [InlineData("[ZoneTransfer]\r\nZoneId=2\r\n", false)]
    [InlineData("[ZoneTransfer]\r\nZoneId=-1\r\n", true)]
    [InlineData("[ZoneTransfer]\r\nZoneId=three\r\n", true)]
    [InlineData("[Other]\r\nZoneId=0\r\n", true)]
    [InlineData("", true)]
    public void A_zone_marks_a_file_unless_it_is_this_computer_the_intranet_or_a_trusted_site(string stream, bool downloaded) =>
        DownloadMark.ZoneIsDownloaded(stream).Should().Be(downloaded);

    [WindowsOnlyFact]
    public void Windows_reads_the_Zone_Identifier_stream_beside_a_file()
    {
        var internet = File("Internet.bas");
        System.IO.File.WriteAllText(internet + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        var local = File("Local.bas");
        System.IO.File.WriteAllText(local + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=0\r\n");
        var unmarked = File("Unmarked.bas");

        DownloadMark.IsDownloaded(internet).Should().BeTrue();
        DownloadMark.IsDownloaded(local).Should().BeFalse();
        DownloadMark.IsDownloaded(unmarked).Should().BeFalse();
        DownloadMark.IsDownloaded(Path.Combine(directory, "Missing.bas")).Should().BeFalse();
    }

    [HexIDE.Tests.Repack.MacOnlyFact]
    public void MacOS_reads_the_quarantine_attribute()
    {
        var quarantined = File("Quarantined.bas");
        using (var xattr = Process.Start(new ProcessStartInfo("/usr/bin/xattr",
                   ["-w", "com.apple.quarantine", "0081;00000000;HexIDE test;", quarantined]))!)
            xattr.WaitForExit();
        var unmarked = File("Unmarked.bas");

        DownloadMark.IsDownloaded(quarantined).Should().BeTrue();
        DownloadMark.IsDownloaded(unmarked).Should().BeFalse();
    }

    // --- What the question covers ---

    [Fact]
    public void A_project_with_one_downloaded_module_names_only_that_module()
    {
        var project = Project("Module1.bas", "Module2.bas");
        markedNow.Add(Path.Combine(directory, "Module2.bas"));

        Trust().Unapproved(project).Should().Equal(new UntrustedFile(Path.Combine(directory, "Module2.bas"), CodeOrigin.Downloaded));
    }

    [Fact]
    public void A_project_nothing_marks_is_not_asked_about() =>
        Trust().Unapproved(Project("Module1.bas")).Should().BeEmpty();

    [Theory]
    [InlineData("Book1.xlsm")]
    [InlineData("Report.DOCM")]
    [InlineData("Legacy.xls")]
    [InlineData("Data.accdb")]
    public void A_project_read_from_an_Office_document_is_asked_about_whatever_marks_it(string document)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Book") { AbsolutePath = File(document) };

        Trust().Unapproved(project).Should().Equal(new UntrustedFile(Path.Combine(directory, document), CodeOrigin.OfficeDocument));
    }

    [Fact]
    public void A_mark_read_once_stands_after_a_save_writes_the_file_again_without_it()
    {
        var project = Project("Module1.bas");
        var module = Path.Combine(directory, "Module1.bas");
        markedNow.Add(module);
        var trust = Trust();
        trust.Observe(module);

        markedNow.Clear();

        trust.Unapproved(project).Select(file => file.Path).Should().Equal(module);
    }

    [Fact]
    public void A_downloaded_file_saved_under_a_new_name_is_still_asked_about()
    {
        var old = File("Module1.bas");
        markedNow.Add(old);
        var trust = Trust();
        trust.Observe(old);
        var project = Project("Renamed.bas");

        trust.Moved(old, Path.Combine(directory, "Renamed.bas"));

        trust.Unapproved(project).Select(file => file.Path).Should().Equal(Path.Combine(directory, "Renamed.bas"));
    }

    [Fact]
    public async Task A_Yes_holds_for_the_session_and_a_newly_marked_file_is_asked_about_on_its_own()
    {
        var project = Project("Module1.bas", "Module2.bas");
        markedNow.Add(Path.Combine(directory, "Module1.bas"));
        var trust = Trust();
        answer.SetResult(MessageBoxResult.Yes);

        (await trust.AskAsync(project.Name, trust.Unapproved(project))).Should().BeTrue();
        trust.Unapproved(project).Should().BeEmpty();

        markedNow.Add(Path.Combine(directory, "Module2.bas"));
        trust.Unapproved(project).Select(file => Path.GetFileName(file.Path)).Should().Equal("Module2.bas");
    }

    [Fact]
    public async Task A_No_allows_nothing()
    {
        var project = Project("Module1.bas");
        markedNow.Add(Path.Combine(directory, "Module1.bas"));
        var trust = Trust();
        answer.SetResult(MessageBoxResult.No);

        (await trust.AskAsync(project.Name, trust.Unapproved(project))).Should().BeFalse();

        trust.Unapproved(project).Should().HaveCount(1);
    }

    [Fact]
    public async Task The_question_names_each_file_and_where_it_came_from_with_No_the_default()
    {
        var trust = Trust();
        answer.SetResult(MessageBoxResult.No);

        await trust.AskAsync("Invoices", [
            new UntrustedFile(Path.Combine(directory, "Module1.bas"), CodeOrigin.Downloaded),
            new UntrustedFile(Path.Combine(directory, "Book1.xlsm"), CodeOrigin.OfficeDocument)]);

        asked.Should().Equal("Run Invoices?   • Module1.bas (downloaded)\n  • Book1.xlsm (Office)");
        await windows.Received(1).MessageBox(Arg.Any<string>(), "Str.CodeTrust.Caption", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, Arg.Any<Window?>(), false, MessageBoxLayout.SecondButtonDefault | MessageBoxLayout.SecurityPrompt);
    }

    [Fact]
    public void A_name_from_a_file_cannot_reverse_or_hide_part_of_itself()
    {
        CodeTrust.Shown("Invoice\u202Esab.exe").Should().Be("Invoicesab.exe");
        CodeTrust.Shown("Pay\u200Broll\r\nFake").Should().Be("Payroll Fake");
        CodeTrust.Shown(new string('a', 200)).Should().HaveLength(80).And.EndWith("\u2026");
    }

    [AvaloniaFact]
    public async Task A_hostile_project_name_follows_HexIDE_s_own_sentence_quoted_on_one_line()
    {
        var english = new LocalizationService();
        var trust = new CodeTrust(windows, english, _ => false);
        answer.SetResult(MessageBoxResult.No);
        const string hostile = "Invoices\r\nVerified safe by Microsoft. This project was scanned and is safe to run.";

        await trust.AskAsync(hostile, [new UntrustedFile(Path.Combine(directory, "Module1.bas"), CodeOrigin.Downloaded)]);

        var lines = asked.Single().Split('\n');
        lines[0].Should().Be("Code that came from the internet or another computer is about to run.");
        lines[1].Should().BeEmpty();
        lines[2].Should().StartWith("\u201cInvoices Verified safe by Microsoft.").And.EndWith("\u2026\u201d");
        lines[2].Should().HaveLength(CodeTrust.MostOfAName + 2, "the name is cut at 60, between its two quotes");
        lines[3].Should().Be("  \u2022 Module1.bas, marked as downloaded");
        lines.Last().Should().Be("Run it?", "the closing question doesn't repeat the name");
    }

    [Fact]
    public void Automation_is_told_a_security_prompt_is_open_and_not_how_to_answer_it()
    {
        var reply = HexIDE.Desktop.Server.HexIdeTools.AskingToRunReply(
            [new UntrustedFile(Path.Combine(directory, "Module1.bas"), CodeOrigin.Downloaded)]);

        reply.Should().Contain("security prompt is open").And.Contain("Module1.bas, marked as downloaded")
            .And.Contain("their security decision");
        reply.Should().NotContainAny("interact", "dump_visual_tree", "Yes");
    }

    // --- What a start does while it waits ---

    private (ProjectRunnerService Runner, StandInRunEngine Engine, IDebugGate Debugger, ProjectDefinition Project) Runner()
    {
        var project = Project("Module1.bas");
        markedNow.Add(Path.Combine(directory, "Module1.bas"));
        var projects = Substitute.For<IProjectManager>();
        projects.StartupProject.Returns(project);
        var engine = new StandInRunEngine();
        var debugger = Substitute.For<IDebugGate>();
        var runner = new ProjectRunnerService(Substitute.For<IEventBus>(), projects, localization, debugger,
            Substitute.For<IBreakpointService>(), new WatchService(), new RunScope(), engine, trust: Trust());
        return (runner, engine, debugger, project);
    }

    [AvaloniaFact]
    public void A_start_waits_on_the_question_and_a_second_start_does_not_ask_again()
    {
        var (runner, engine, debugger, _) = Runner();

        runner.RunStartupProject();
        runner.RunStartupProject();

        asked.Should().HaveCount(1);
        runner.AskingToRun!.Select(file => Path.GetFileName(file.Path)).Should().Equal("Module1.bas");
        engine.Prepared.Should().Be(0);
        runner.RunningProject.Should().BeNull();
        debugger.DidNotReceive().Reset();
    }

    [AvaloniaFact]
    public async Task A_No_starts_nothing_and_leaves_no_Run_To_Cursor_armed()
    {
        var (runner, engine, debugger, project) = Runner();

        runner.RunToCursorProject(DocumentIdentity.For(project.Modules[0]), 3);
        answer.SetResult(MessageBoxResult.No);
        await Task.Yield();

        runner.AskingToRun.Should().BeNull();
        engine.Prepared.Should().Be(0);
        runner.RunningProject.Should().BeNull();
        runner.HasPendingRunToCursor.Should().BeFalse();
        debugger.DidNotReceive().Reset();
    }

    [AvaloniaFact]
    public async Task A_Yes_starts_the_run_and_the_next_start_does_not_ask()
    {
        var (runner, engine, _, _) = Runner();

        runner.RunStartupProject();
        answer.SetResult(MessageBoxResult.Yes);
        await Task.Yield();

        engine.Prepared.Should().Be(1);
        runner.EndProject();
        runner.RunStartupProject();
        asked.Should().HaveCount(1);
        engine.Prepared.Should().Be(2);
    }

    [AvaloniaFact]
    public async Task A_downloaded_launch_file_is_asked_about_and_an_unmarked_one_is_not()
    {
        var (runner, _, _, _) = Runner();
        var launch = File("launch.json");
        var local = File("local.json");
        markedNow.Add(launch);
        answer.SetResult(MessageBoxResult.No);

        (await runner.MayRunFileAsync(launch)).Should().BeFalse();
        (await runner.MayRunFileAsync(local)).Should().BeTrue();
        asked.Should().HaveCount(1);
    }

    // --- Opening runs nothing ---

    [AvaloniaFact]
    public async Task Opening_a_downloaded_project_runs_none_of_its_code_and_records_its_marks_before_any_save()
    {
        var ran = Path.Combine(directory, "ran.txt");
        var vbp = Path.Combine(directory, "Opened.vbp");
        System.IO.File.WriteAllText(vbp, Lines("Type=Exe", "Form=Form1.frm", "Module=Module1; Module1.bas", "Startup=`Form1`", "Name=`Opened`"));
        System.IO.File.WriteAllText(Path.Combine(directory, "Form1.frm"), Lines(
            "VERSION 5.00", "Begin VB.Form Form1 ", "   Caption         =   `Form1`", "   ClientHeight    =   3000",
            "   ClientWidth     =   4000", "End", "Attribute VB_Name = `Form1`", "Attribute VB_GlobalNameSpace = False",
            "Attribute VB_Creatable = False", "Attribute VB_PredeclaredId = True", "Attribute VB_Exposed = False",
            "Private Sub Form_Load()", $"    Open `{ran}` For Output As #1", "    Print #1, `Form_Load`", "    Close #1", "End Sub"));
        System.IO.File.WriteAllText(Path.Combine(directory, "Module1.bas"), Lines(
            "Attribute VB_Name = `Module1`", "Sub Main()", $"    Open `{ran}` For Output As #1", "    Print #1, `Main`", "    Close #1",
            "End Sub"));
        markedNow.Add(Path.Combine(directory, "Module1.bas"));
        var trust = Trust();
        var (service, opened) = Service(trust);

        await service.OpenProject(vbp);
        markedNow.Clear();   // as a save leaves the file: written again, with no mark

        opened().Should().NotBeNull();
        System.IO.File.Exists(ran).Should().BeFalse("opening a project runs none of its code");
        trust.Unapproved(opened()!).Select(file => Path.GetFileName(file.Path)).Should().Equal("Module1.bas");
    }

    // A form's companion is recorded as read like every other file, though only code is asked about (#4432). Adding an
    // existing form reads it through ReadCompanion, and reloading the form reads it again.
    [AvaloniaFact]
    public async Task A_forms_companion_is_reported_to_CodeTrust_when_added_and_when_reloaded()
    {
        var vbp = Path.Combine(directory, "Opened.vbp");
        System.IO.File.WriteAllText(vbp, Lines("Type=Exe", "Module=Module1; Module1.bas", "Name=`Opened`"));
        System.IO.File.WriteAllText(Path.Combine(directory, "Module1.bas"), Lines("Attribute VB_Name = `Module1`"));
        var frm = Path.Combine(directory, "Form2.frm");
        System.IO.File.WriteAllText(frm, Lines(
            "VERSION 5.00", "Begin VB.Form Form2 ", "   Caption         =   `Form2`", "   ClientHeight    =   3000",
            "   ClientWidth     =   4000", "End", "Attribute VB_Name = `Form2`"));
        System.IO.File.WriteAllBytes(Path.Combine(directory, "Form2.frx"), [1, 2, 3, 4]);
        var observed = new List<string>();
        var trust = new CodeTrust(windows, localization, path =>
        {
            lock (observed)
                observed.Add(Path.GetFileName(path));
            return false;
        });
        var (service, opened) = Service(trust);
        await service.OpenProject(vbp);

        var form = (await service.AddExistingForm(opened()!, frm)).Document;
        observed.Should().Contain("Form2.frx", "adding the form reads its companion");
        observed.Clear();
        (await service.ReloadFormFromDisk(form!)).Should().BeTrue();

        observed.Should().Contain("Form2.frx", "reloading the form reads its companion again");
    }

    private (ProjectService Service, Func<ProjectDefinition?> Opened) Service(CodeTrust trust)
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([]);
        ProjectDefinition? opened = null;
        projects.When(manager => manager.AddProject(Arg.Any<ProjectDefinition>())).Do(call => opened = call.Arg<ProjectDefinition>());
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<HexIDE.Forms.ViewModels.BaseEditorWindowViewModel>());
        var service = new ProjectService(() => throw new InvalidOperationException("no new-project dialog"),
            windows, Substitute.For<IEventBus>(), projects, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), sidecar, new FileBaselineStore(), localization,
            new HeaderRefresher(new EventBus(), dock, new BreakpointService(), new HexIDE.Bookmarks.BookmarkService()),
            InBoxRegistry.Create(), trust: trust);
        return (service, () => opened);
    }

    // A VB6 file's lines, with ` for each double quote.
    private static string Lines(params string[] lines) => string.Join("\r\n", lines).Replace('`', '"') + "\r\n";
}
