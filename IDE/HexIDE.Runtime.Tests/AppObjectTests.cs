using HexIDE.IDE;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The global <c>App</c> object (issue #136).
///
/// <para>Every expectation here is the <b>design-time</b> answer, measured against real VB6 under F5 — see
/// the App section of <c>docs/vb6-fidelity-oracle.md</c>. That matters because VB6's own answers differ
/// between F5 and a compiled exe, and HexIDE's interpreter is permanently in F5's position: there is no
/// built executable, so no version resource to read.</para>
///
/// <para>The row worth having measured is <c>App.EXEName</c>: at design time VB6 reports the <b>project
/// file's</b> name, not the project <c>Name</c> and not <c>ExeName32</c>. The probe set all three to
/// different strings, and the obvious guess — the project name — was wrong.</para>
/// </summary>
public class AppObjectTests
{
    private sealed class Recording : IBasicStandardLibrary
    {
        public string? LastCaption { get; private set; }
        public bool Called { get; private set; }
        public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons b, MessageBoxIcon i)
        { LastCaption = caption; Called = true; return Task.FromResult(MessageBoxResult.Ok); }
        public Task<string?> InputBox(string p, string? t, string d)
        { LastCaption = t; Called = true; return Task.FromResult<string?>(""); }
        public void DebugPrint(Vb6Value value) { }
    }

    private static readonly AppInfo Probe = new(
        Title: "TitleKey",
        ExeName: "DesignTime",
        Path: @"C:\hexide-designtime-probe",
        Major: 1, Minor: 0, Revision: 0);

    private static async Task<List<Vb6Value>> Run(string code, AppInfo? app = null)
    {
        var log = new List<Vb6Value>();
        var lib = new CapturingLib(log);
        var vb = new BasicInterpreter(lib, new ModuleExecutionContext(), new ExecutionEnvironment(), code);
        if (app is not null) vb.SetAppInfo(app);
        await vb.Execute();
        return log;
    }

    private sealed class CapturingLib(List<Vb6Value> log) : IBasicStandardLibrary
    {
        public Task<MessageBoxResult> MsgBox(string t, string? c, MessageBoxButtons b, MessageBoxIcon i)
            => Task.FromResult(MessageBoxResult.Ok);
        public Task<string?> InputBox(string p, string? t, string d) => Task.FromResult<string?>("");
        public void DebugPrint(Vb6Value value) => log.Add(value);
    }

    [Fact]
    public async Task App_reports_the_projects_title()
    {
        var log = await Run("Debug.Print App.Title", Probe);
        log.Should().ContainSingle().Which.Value.Should().Be("TitleKey");
    }

    [Fact]
    public async Task App_EXEName_is_the_project_file_not_the_project_name()
    {
        // The measured surprise. `Name=` was "NameKey" and `ExeName32=` was "ExeNameKey.exe"; VB6 under
        // F5 returned neither — it returned the .vbp's own filename.
        var log = await Run("Debug.Print App.EXEName", Probe);
        log.Should().ContainSingle().Which.Value.Should().Be("DesignTime");
    }

    [Fact]
    public async Task App_Path_is_the_projects_folder()
    {
        var log = await Run("Debug.Print App.Path", Probe);
        log.Should().ContainSingle().Which.Value.Should().Be(@"C:\hexide-designtime-probe");
    }

    [Fact]
    public async Task App_ProductName_is_empty_at_design_time()
    {
        // Not an omission: ProductName comes from a compiled exe's version resource, and there is no exe.
        // VB6 under F5 returns empty here too, even with Title set — measured.
        var log = await Run("Debug.Print App.ProductName", Probe);
        log.Should().ContainSingle().Which.Value.Should().Be("");
    }

    [Fact]
    public async Task App_PrevInstance_is_false()
    {
        var log = await Run("Debug.Print App.PrevInstance", Probe);
        log.Should().ContainSingle().Which.Value.Should().Be(false);
    }

    [Fact]
    public async Task App_version_parts_are_readable()
    {
        var log = await Run("""
            Debug.Print App.Major
            Debug.Print App.Minor
            Debug.Print App.Revision
            """, Probe with { Major = 2, Minor = 7, Revision = 13 });

        log.Select(v => v.Value).Should().Equal(2, 7, 13);
    }

    [Fact]
    public async Task App_Title_is_writable_at_run_time()
    {
        // VB6 allows it; the rest of App is read-only.
        var log = await Run("""
            App.Title = "Renamed"
            Debug.Print App.Title
            """, Probe);

        log.Should().ContainSingle().Which.Value.Should().Be("Renamed");
    }

    [Fact]
    public async Task Without_a_project_App_reports_empty_rather_than_inventing_an_identity()
    {
        var log = await Run("Debug.Print App.Title");
        log.Should().ContainSingle().Which.Value.Should().Be("");
    }

    // ── deriving AppInfo from a real project ────────────────────────────────────────────────────────

    private static ProjectElements.ProjectDefinition ProjectWith(string name, params string[] preSectionLines)
    {
        var p = new ProjectElements.ProjectDefinition(ProjectElements.VBProjectType.EXE, name)
        {
            AbsolutePath = @"C:\some\where\TheProjectFile.vbp",
        };
        for (var i = 0; i < preSectionLines.Length; i++)
            p.UnknownPreSectionLines.Add((i, preSectionLines[i]));
        return p;
    }

    [Fact]
    public void FromProject_strips_both_quotes_from_Title()
    {
        // VB6 writes this key quoted — `Title="Project1"` is in its own templates — and its reader then
        // strips the LEADING quote only, so a real VB6 app shows `Project1"` in App.Title. A trailing
        // quote in an application's name is a defect, not a design, so HexIDE removes both.
        // See docs/vb6-fidelity-oracle.md.
        var info = AppInfo.FromProject(ProjectWith("NameKey", "Title=\"TitleKey\""));

        info.Title.Should().Be("TitleKey");
    }

    [Fact]
    public void FromProject_falls_back_to_the_project_name_when_there_is_no_Title()
    {
        var info = AppInfo.FromProject(ProjectWith("NameKey"));

        info.Title.Should().Be("NameKey");
    }

    [Fact]
    public void FromProject_takes_EXEName_from_the_project_file()
    {
        // Not the project Name, and not ExeName32 — both are present here and neither is the answer.
        var info = AppInfo.FromProject(ProjectWith("NameKey", "ExeName32=\"ExeNameKey.exe\""));

        info.ExeName.Should().Be("TheProjectFile");
        info.Path.Should().Be(@"C:\some\where");
    }

    [Fact]
    public void FromProject_reads_the_version_keys()
    {
        var info = AppInfo.FromProject(ProjectWith("NameKey", "MajorVer=3", "MinorVer=14", "RevisionVer=159"));

        (info.Major, info.Minor, info.Revision).Should().Be((3, 14, 159));
    }

    [Fact]
    public void FromProject_defaults_the_version_to_one_zero_zero()
    {
        // What VB6 reports for a project that has never had its version set — measured.
        var info = AppInfo.FromProject(ProjectWith("NameKey"));

        (info.Major, info.Minor, info.Revision).Should().Be((1, 0, 0));
    }

    [Fact]
    public void FromProject_reads_the_version_info_strings()
    {
        var info = AppInfo.FromProject(ProjectWith("NameKey", "VersionCompanyName=\"Microsoft Corporation\""));

        info.CompanyName.Should().Be("Microsoft Corporation");
        info.LegalCopyright.Should().BeEmpty("the key is absent, and VB6 reports empty rather than guessing");
    }

    // ── the reason #136 was on the critical path ────────────────────────────────────────────────────

    [Fact]
    public async Task An_omitted_MsgBox_title_takes_App_Title()
    {
        var lib = new Recording();
        var vb = new BasicInterpreter(lib, new ModuleExecutionContext(), new ExecutionEnvironment(),
                                      "MsgBox \"hello\"");
        vb.SetAppInfo(Probe);
        await vb.Execute();

        lib.Called.Should().BeTrue();
        lib.LastCaption.Should().Be("TitleKey");
    }

    [Fact]
    public async Task An_explicitly_empty_MsgBox_title_still_stays_empty()
    {
        // The #131 distinction has to survive App landing: App.Title fills an OMITTED title, never one the
        // author deliberately blanked.
        var lib = new Recording();
        var vb = new BasicInterpreter(lib, new ModuleExecutionContext(), new ExecutionEnvironment(),
                                      "MsgBox \"hello\", vbOKOnly, \"\"");
        vb.SetAppInfo(Probe);
        await vb.Execute();

        lib.LastCaption.Should().BeEmpty();
    }

    [Fact]
    public async Task With_no_project_an_omitted_title_stays_null_for_the_host_to_fill()
    {
        // No App.Title to substitute, so "omitted" must reach the host intact rather than becoming a
        // blank caption — the host has its own last-resort name.
        var lib = new Recording();
        var vb = new BasicInterpreter(lib, new ModuleExecutionContext(), new ExecutionEnvironment(),
                                      "MsgBox \"hello\"");
        await vb.Execute();

        lib.LastCaption.Should().BeNull();
    }

    [Fact]
    public async Task An_omitted_InputBox_title_takes_App_Title_too()
    {
        var lib = new Recording();
        var vb = new BasicInterpreter(lib, new ModuleExecutionContext(), new ExecutionEnvironment(),
                                      "Dim s As String\ns = InputBox(\"prompt\")");
        vb.SetAppInfo(Probe);
        await vb.Execute();

        lib.LastCaption.Should().Be("TitleKey");
    }
}
