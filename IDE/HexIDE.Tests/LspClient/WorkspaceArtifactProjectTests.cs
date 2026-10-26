using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.LspClient;

public sealed class WorkspaceArtifactProjectTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FormsAndModulesInvalidateOnAddRenameAndRemove(bool form)
    {
        var root = Path.Combine(Path.GetTempPath(), "artifact-membership");
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1")
        {
            AbsolutePath = Path.Combine(root, "Project1.vbp")
        };
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns(new[] { project });
        var sut = new ProjectLspWorkspace(() => manager);
        sut.Projects.Should().ContainSingle();
        var changes = 0;
        sut.ProjectChanged += () => changes++;
        if (form)
        {
            var item = new FormDefinition(project, [], "") { AbsolutePath = Path.Combine(root, "Form1.frm") };
            project.AddForm(item);
            sut.Projects[0].Project.Files.Should().ContainSingle(f => f.HostPath == item.AbsolutePath);
            item.AbsolutePath = Path.Combine(root, "Renamed.frm");
            sut.Projects[0].Project.Files[0].ProjectPath.Should().Be("Renamed.frm");
            project.DeleteForm(item);
        }
        else
        {
            var item = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule)
            {
                AbsolutePath = Path.Combine(root, "Module1.bas")
            };
            project.AddModule(item);
            sut.Projects[0].Project.Files.Should().ContainSingle(f => f.HostPath == item.AbsolutePath);
            item.AbsolutePath = Path.Combine(root, "Renamed.bas");
            sut.Projects[0].Project.Files[0].ProjectPath.Should().Be("Renamed.bas");
            project.DeleteModule(item);
        }
        changes.Should().Be(3);
        sut.Projects[0].Project.Files.Should().BeEmpty();
        sut.Dispose();
    }

    [Fact]
    public void MembershipRenamesAndReferencesInvalidateTheProjection()
    {
        var root = Path.Combine(Path.GetTempPath(), "artifact-project");
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1")
        {
            AbsolutePath = Path.Combine(root, "Project1.vbp")
        };
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns(new[] { project });
        var sut = new ProjectLspWorkspace(() => manager);
        sut.Projects.Should().ContainSingle();
        var changes = 0;
        sut.ProjectChanged += () => changes++;
        var carried = new RelatedDocumentDefinition(project, "probe.c", Path.Combine(root, "probe.c"));
        project.AddRelatedDocument(carried);
        sut.Projects[0].Project.Files.Should().ContainSingle(f => f.HostPath == carried.AbsolutePath);
        carried.AbsolutePath = Path.Combine(root, "renamed.c");
        sut.Projects[0].Project.Files[0].ProjectPath.Should().Be("renamed.c");
        project.DeleteRelatedDocument(carried);
        sut.Projects[0].Project.Files.Should().BeEmpty();
        var reference = new VbReference(Guid.NewGuid().ToString(), "1.2", 0, null, "Library");
        project.AddReference(reference);
        sut.Projects[0].Project.References.Should().ContainSingle(r => r.Name == "Library" && r.Major == 1 && r.Minor == 2);
        project.RemoveReference(reference);
        sut.Projects[0].Project.References.Should().BeEmpty();
        project.SetReferences([reference]);
        sut.Projects[0].Project.References.Should().ContainSingle();
        changes.Should().Be(6);
        sut.Dispose();
    }

    [Fact]
    public void TheProjectionLoadsNoLibraryAndKeepsTheNameTheProjectRecords()
    {
        // Opening a project mustn't load what it references: a reference names a path, and a path on another machine is
        // contacted as it loads, which hands that machine the user's credentials. On Windows, a projection that read the
        // registered OLE Automation library would name it stdole here (#2071's security review).
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1")
        {
            AbsolutePath = Path.Combine(Path.GetTempPath(), "artifact-library", "Project1.vbp")
        };
        project.AddReference(new VbReference("{00020430-0000-0000-C000-000000000046}", "2.0", 0, null, "OLE Automation"));
        project.AddReference(new VbReference(Guid.NewGuid().ToString(), "1.0", 0, @"\\host.invalid\share\remote.tlb", "Remote"));
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns(new[] { project });
        var sut = new ProjectLspWorkspace(() => manager);
        sut.Projects[0].Project.References.Select(r => r.Name).Should().Equal("OLE Automation", "Remote");
        sut.Dispose();
    }

    public static bool OnWindows => OperatingSystem.IsWindows();

    [Fact(Skip = "Type libraries are read only on Windows.", SkipUnless = nameof(OnWindows))]
    public void TheTypeLibraryNamesOleAutomationStdole()
    {
        HexIDE.Runtime.TypeLibrary.TypeLibReader.ReadName("{00020430-0000-0000-C000-000000000046}", "2.0", null)
            .Should().Be("stdole");
    }

    [Fact(Skip = "Type libraries are read only on Windows.", SkipUnless = nameof(OnWindows))]
    public void ALocalTypeLibraryIsNamedFromItsFile()
    {
        HexIDE.Runtime.TypeLibrary.TypeLibReader.ReadName("", "0.0", Path.Combine(Environment.SystemDirectory, "stdole2.tlb"))
            .Should().Be("stdole");
    }

    [Theory]
    [InlineData("CondComp=\"DEBUG = 1 : Trace = -2\"", 2)]
    [InlineData("CondComp=\"DEBUG = 70000\"", 0)]
    [InlineData(null, 0)]
    public void CompilationArgumentsAreProjectedAndARefusedLineGivesNone(string? line, int count)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1")
        {
            AbsolutePath = Path.Combine(Path.GetTempPath(), "artifact-arguments", "Project1.vbp")
        };
        if (line is not null) project.UnknownPreSectionLines.Add((0, line));
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns(new[] { project });
        var sut = new ProjectLspWorkspace(() => manager);
        var arguments = sut.Projects[0].Project.CompilationArguments!;
        arguments.Should().HaveCount(count);
        if (count > 0)
        {
            arguments["debug"].Should().Be(1);
            arguments["TRACE"].Should().Be(-2);
        }
        sut.Dispose();
    }
}
