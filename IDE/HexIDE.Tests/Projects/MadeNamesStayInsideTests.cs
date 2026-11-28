using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using HexIDE.IDE;
using HexIDE.IO;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;
using HexIDE.Runtime.Making;

namespace HexIDE.Tests.Projects;

/// <summary>
/// A file a project names stays in the folder it belongs in (#5622). A member's name is its file's name when Make packages
/// it, and <c>ExeName32</c>, or the project's <c>Name</c>, is the made program's. A project from elsewhere chooses them all,
/// so each is a plain file name or nothing is written.
/// </summary>
/// <remarks>
/// Run against main before the change, 12 of the 14 tests above the rule's own failed: Make wrote <c>x.bas</c> into the
/// folder above its staging folder and reported the program made, and Make Project Group wrote the program beside the
/// project's folder. The two that passed are the controls, a <c>Path32</c> above the project's folder and a module's own
/// name suggested (measured, #5622).
/// </remarks>
public class MadeNamesStayInsideTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("hexide-confine-").FullName;
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private readonly IProjectManager manager = Substitute.For<IProjectManager>();
    private readonly ILocalizationService localization = Substitute.For<ILocalizationService>();

    // The leaf an escaping name ends in, unique to this run, so nothing another run left can pass or fail a check.
    private readonly string leaf = "Escaped" + Guid.NewGuid().ToString("N")[..8];

    public MadeNamesStayInsideTests()
    {
        // The key itself, except the refusal's, whose three placeholders the assertions read back.
        localization.GetString(Arg.Any<string>()).Returns(call =>
            call.Arg<string>() == "Str.Naming.Msg.NotAFileName" ? "{0} | {1} | {2}" : call.Arg<string>());
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(true);
    }

    public void Dispose()
    {
        // A regression would leave its file where the project pointed: never beside the temporary folder after the run.
        foreach (var escaped in new[] { Path.Join(Path.GetTempPath(), leaf + ".bas"), Path.Join(Path.GetTempPath(), leaf + ".frm") })
            if (File.Exists(escaped)) File.Delete(escaped);
        Directory.Delete(root, true);
    }

    private ProjectService Service()
    {
        var service = new ProjectService(() => throw new InvalidOperationException(), windows, Substitute.For<IEventBus>(), manager,
            Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(),
            Substitute.For<IUserSidecarService>(), new FileBaselineStore(), localization,
            Substitute.For<IHeaderRefresher>(), InBoxRegistry.Create()).WithMaker(root);
        var standalone = Directory.CreateDirectory(Path.Join(root, "standalone")).FullName;
        File.WriteAllText(Path.Join(standalone, Vb6ProjectMaker.StandaloneRunnerName()!), "runner");
        return service;
    }

    private static async Task<MakeOutcomes.Outcome> Ended(MakeOutcomes.Make make) =>
        await make.Ended.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    private static ProjectDefinition WithModule(string moduleName, string? absolutePath = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P") { AbsolutePath = absolutePath };
        var module = new ModuleDefinition(project, moduleName, ModuleKind.StandardModule);
        module.UpdateCode("Sub Main()\nEnd Sub");
        project.AddModule(module);
        return project;
    }

    // --- File > Make: a member's name is its file's in the package ---

    [Theory]
    [InlineData("..\\{0}")]
    [InlineData("../{0}")]
    [InlineData("sub\\..\\..\\{0}")]
    public async Task Make_refuses_a_module_whose_name_would_put_its_file_outside_the_package(string shape)
    {
        var name = string.Format(shape, leaf);
        var project = WithModule(name);
        var exe = Path.Join(Directory.CreateDirectory(Path.Join(root, "out")).FullName, "Made.exe");
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(exe);

        var make = () => Service().MakeProject(project);

        await make.Should().ThrowAsync<OperationCanceledException>();
        var outcome = await Ended(MakeOutcomes.Latest!);
        outcome.ProgramPath.Should().BeNull("a refused Make is never reported as made");
        outcome.WhyNot.Should().Be($"{name}'s file would be written outside the folder Make packages, so nothing was written");
        await windows.Received(1).MessageBox($"P | VB_Name | {name}", icon: MessageBoxIcon.Information);
        await windows.DidNotReceive().SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>());
        File.Exists(Path.Join(Path.GetTempPath(), leaf + ".bas")).Should().BeFalse();
        File.Exists(exe).Should().BeFalse();
    }

    [Fact]
    public async Task Make_refuses_a_form_whose_name_would_put_its_file_outside_the_package()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddForm(FormDefinition.NewForm(project, FormComponentClass.Instance, "..\\" + leaf));
        var exe = Path.Join(Directory.CreateDirectory(Path.Join(root, "out")).FullName, "Made.exe");
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(exe);

        var make = () => Service().MakeProject(project);

        await make.Should().ThrowAsync<OperationCanceledException>();
        (await Ended(MakeOutcomes.Latest!)).ProgramPath.Should().BeNull();
        await windows.Received(1).MessageBox($"P | VB_Name | ..\\{leaf}", icon: MessageBoxIcon.Information);
        File.Exists(Path.Join(Path.GetTempPath(), leaf + ".frm")).Should().BeFalse();
        File.Exists(exe).Should().BeFalse();
    }

    // The check before the picker is the one a user meets. The write loop confines each file as well, so a name changed
    // while the picker is open fails the Make rather than writing outside.
    [Fact]
    public async Task A_module_renamed_while_the_picker_is_open_fails_the_Make_and_writes_nothing_outside()
    {
        var project = WithModule("Module1");
        var module = project.Modules[0];
        var exe = Path.Join(Directory.CreateDirectory(Path.Join(root, "out")).FullName, "Made.exe");
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(_ =>
        {
            module.Name = "..\\" + leaf;
            return exe;
        });

        var make = () => Service().MakeProject(project);

        await make.Should().ThrowAsync<InvalidOperationException>();
        var outcome = await Ended(MakeOutcomes.Latest!);
        outcome.ProgramPath.Should().BeNull();
        outcome.WhyNot.Should().StartWith("Make failed: ");
        File.Exists(Path.Join(Path.GetTempPath(), leaf + ".bas")).Should().BeFalse();
        File.Exists(exe).Should().BeFalse();
        module.AbsolutePath.Should().BeNull("the paths Make repointed are put back");
    }

    // --- Make Project Group: ExeName32, or the project's Name, is the program's, with no picker to show where ---

    private ProjectDefinition SavedProject(string name = "P", string? exeName32 = null, string? path32 = null)
    {
        var folder = Directory.CreateDirectory(Path.Join(root, "proj")).FullName;
        var project = new ProjectDefinition(VBProjectType.EXE, name) { AbsolutePath = Path.Join(folder, "P.vbp") };
        File.WriteAllText(project.AbsolutePath, "original project");
        if (exeName32 is not null)
            project.UnknownPreSectionLines.Add((0, $"ExeName32=\"{exeName32}\""));
        if (path32 is not null)
            project.UnknownPreSectionLines.Add((1, $"Path32=\"{path32}\""));
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule)
            { AbsolutePath = Path.Join(folder, "Module1.bas") };
        module.UpdateCode("Sub Main()\nEnd Sub");
        project.AddModule(module);
        File.WriteAllText(module.AbsolutePath, "original module");
        manager.LoadedProjects.Returns([project]);
        return project;
    }

    [Fact]
    public async Task Make_Project_Group_refuses_an_ExeName32_that_climbs_out_of_the_projects_folder()
    {
        SavedProject(exeName32: "..\\" + leaf + ".exe");

        await Service().MakeProjectGroup();

        File.Exists(Path.Join(root, leaf + ".exe")).Should().BeFalse();
        File.Exists(Path.Join(root, leaf + ".dll")).Should().BeFalse();
        await windows.Received(1).MessageBox($"P | ExeName32 | ..\\{leaf}.exe", icon: MessageBoxIcon.Information);
        var outcome = await Ended(MakeOutcomes.Latest!);
        outcome.ProgramPath.Should().BeNull();
        outcome.WhyNot.Should().Be($"P's program would be written outside its folder: ExeName32 is '..\\{leaf}.exe', "
                                   + "which isn't a file name, so nothing was written");
    }

    [Fact]
    public async Task Make_Project_Group_refuses_a_rooted_ExeName32()
    {
        var rooted = Path.Join(root, leaf + ".exe");
        SavedProject(exeName32: rooted);

        await Service().MakeProjectGroup();

        File.Exists(rooted).Should().BeFalse();
        await windows.Received(1).MessageBox($"P | ExeName32 | {rooted}", icon: MessageBoxIcon.Information);
    }

    [Fact]
    public async Task Make_Project_Group_refuses_a_project_Name_that_would_name_its_program_outside_its_folder()
    {
        SavedProject(name: "..\\" + leaf);

        await Service().MakeProjectGroup();

        File.Exists(Path.Join(root, leaf + ".exe")).Should().BeFalse();
        await windows.Received(1).MessageBox($"..\\{leaf} | Name | ..\\{leaf}", icon: MessageBoxIcon.Information);
    }

    // Path32 is the folder the project names for its program, relative or absolute, as VB6 allows. Only the file name in
    // it is confined.
    [Fact]
    public async Task Make_Project_Group_still_builds_into_a_Path32_above_the_projects_folder()
    {
        Directory.CreateDirectory(Path.Join(root, "out"));
        SavedProject(exeName32: "Made.exe", path32: "..\\out");

        await Service().MakeProjectGroup();

        File.ReadAllText(Path.Join(root, "out", "Made.exe")).Should().Be("runner");
        File.Exists(Path.Join(root, "out", "Made.dll")).Should().BeTrue();
    }

    // --- Make with VB6 and Start with VB6: VB6 writes the program where a path there points, and Start with VB6 starts it ---

    // VB6's toolchain, the Basic provider's (#5621), with only the compiler run stubbed.
    private sealed class StubbedCompiler : Vb6Toolchain
    {
        public int Builds { get; private set; }

        internal override Task<ToolchainBuild?> RunVb6Async(string projectPath, string? outputDirectory)
        {
            Builds++;
            return Task.FromResult<ToolchainBuild?>(new ToolchainBuild(0, ""));
        }
    }

    private sealed class StubbedVb6 : Vb6ToolchainService
    {
        private readonly StubbedCompiler compiler;

        public StubbedVb6(IWindowManager windows, ILocalizationService localization, StubbedCompiler compiler)
            : base(Substitute.For<ILspClient>(), Substitute.For<IEventBus>(), windows, localization,
                new CodeTrust(windows, localization, _ => false), Substitute.For<IUserSidecarService>(),
                TestMakers.Vb6WithMaker(Path.GetTempPath(), compiler)) =>
            this.compiler = compiler;

        public int Builds => compiler.Builds;
        public int Starts { get; private set; }

        internal override void StartExe(string exePath) => Starts++;
    }

    // The toolchain's constructor reads VB6_EXE, so the path is supplied there and put back straight away. The test
    // assembly's own executable is a file that exists on every platform this runs on.
    private StubbedVb6 Vb6()
    {
        var previous = Environment.GetEnvironmentVariable("VB6_EXE");
        Environment.SetEnvironmentVariable("VB6_EXE", Environment.ProcessPath);
        try { return new StubbedVb6(windows, localization, new StubbedCompiler()); }
        finally { Environment.SetEnvironmentVariable("VB6_EXE", previous); }
    }

    [Fact]
    public async Task Make_with_VB6_refuses_an_ExeName32_that_climbs_out_before_VB6_starts()
    {
        var project = SavedProject(exeName32: "..\\" + leaf + ".exe");
        var vb6 = Vb6();

        (await vb6.MakeWithVb6Async(project)).Should().BeFalse();

        vb6.Builds.Should().Be(0);
        await windows.Received(1).MessageBox($"P | ExeName32 | ..\\{leaf}.exe", "Str.Vb6Toolchain.BuildFailedTitle",
            MessageBoxButtons.Ok, MessageBoxIcon.Warning);
    }

    [Fact]
    public async Task Start_with_VB6_refuses_an_ExeName32_that_climbs_out_and_starts_nothing()
    {
        var project = SavedProject(exeName32: "..\\" + leaf + ".exe");
        var vb6 = Vb6();

        await vb6.RunWithVb6Async(project);

        vb6.Builds.Should().Be(0);
        vb6.Starts.Should().Be(0);
    }

    // --- Writing a project to a folder, as --newproject does ---

    [Fact]
    public async Task Writing_a_project_to_a_folder_refuses_a_member_whose_file_would_land_outside_it()
    {
        var project = WithModule("..\\" + leaf);
        var folder = Directory.CreateDirectory(Path.Join(root, "write")).FullName;

        var write = () => Service().SaveProjectToDirectory(project, folder);

        (await write.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("..\\" + leaf);
        File.Exists(Path.Join(root, leaf + ".bas")).Should().BeFalse();
        Directory.GetFiles(folder).Should().BeEmpty();
    }

    // --- A picker's suggestion is only what the user confirms, but it shouldn't point outside either ---

    [Fact]
    public async Task Save_As_suggests_no_name_for_a_module_whose_name_isnt_a_file_name()
    {
        var project = WithModule("..\\" + leaf);
        FilePickerSaveOptions? asked = null;
        windows.SaveFilePickerAsync(Arg.Do<FilePickerSaveOptions>(options => asked = options), Arg.Any<string?>())
            .Returns((string?)null);

        try { await Service().SaveModule(project.Modules[0], saveAs: true); }
        catch (OperationCanceledException) { }

        asked.Should().NotBeNull();
        asked!.SuggestedFileName.Should().BeNull();
    }

    [Fact]
    public async Task Save_As_still_suggests_a_modules_own_name()
    {
        var project = WithModule("Module1");
        FilePickerSaveOptions? asked = null;
        windows.SaveFilePickerAsync(Arg.Do<FilePickerSaveOptions>(options => asked = options), Arg.Any<string?>())
            .Returns((string?)null);

        try { await Service().SaveModule(project.Modules[0], saveAs: true); }
        catch (OperationCanceledException) { }

        asked!.SuggestedFileName.Should().Be("Module1.bas");
    }

    // --- The rule ---

    [Theory]
    [InlineData("Module1.bas")]
    [InlineData("My App.exe")]
    [InlineData("...bas")]
    [InlineData("a..b.frm")]
    public void A_plain_file_name_joins_its_folder(string name)
    {
        ConfinedPath.IsFileName(name).Should().BeTrue();
        ConfinedPath.Join(root, name).Should().Be(Path.GetFullPath(Path.Join(root, name)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("..\\x.bas")]
    [InlineData("../x.bas")]
    [InlineData("sub\\x.bas")]
    [InlineData("sub/x.bas")]
    [InlineData("C:x.bas")]
    [InlineData("C:\\x.bas")]
    [InlineData("\\x.bas")]
    [InlineData("/x.bas")]
    [InlineData("\\\\server\\share\\x.bas")]
    [InlineData("x.bas:stream")]
    public void Anything_else_is_refused_on_every_host(string name)
    {
        ConfinedPath.IsFileName(name).Should().BeFalse();
        ConfinedPath.Join(root, name).Should().BeNull();
    }
}
