using System;
using System.IO;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// Guards the real <see cref="ProjectService"/> load → save cycle for UserControls and PropertyPages.
///
/// The serializer was never at fault here and is pinned by its own unit tests, which hand it code-only
/// input. The defect (issue #12) lived entirely in the wiring: the loader stored the *whole* .ctl file in
/// <c>ModuleDefinition.Code</c>, and <see cref="FormSerializer"/> regenerates the Begin..End header before
/// appending Code verbatim — so a plain Ctrl+S emitted the header twice and grew the file on every save.
/// Nothing exercised the two ends together, which is why it survived. These tests do exactly that.
/// </summary>
public class UserControlSaveRoundTripTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-uc-" + Guid.NewGuid().ToString("N"));

    public UserControlSaveRoundTripTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private IFileBaselineStore baselineStore = null!;

    private ProjectService MakeService(out Func<ProjectDefinition> loadedProject)
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(Array.Empty<ProjectDefinition>());

        ProjectDefinition? captured = null;
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
                      .Do(ci => captured = ci.Arg<ProjectDefinition>());

        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);

        baselineStore = new FileBaselineStore();

        loadedProject = () => captured ?? throw new InvalidOperationException("project was never loaded");

        return new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            Substitute.For<IWindowManager>(),
            Substitute.For<IEventBus>(),
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            baselineStore,
            Substitute.For<HexIDE.Localization.ILocalizationService>(),
            Substitute.For<IHeaderRefresher>());
    }

    /// <summary>Writes a canonical .ctl (via the serializer, so the on-disk format is real VB6) plus a .vbp.</summary>
    private string WriteProject(string code, byte[][]? companionBlobs = null)
    {
        var scratch = new ProjectDefinition(VBProjectType.EXE, "Test");
        var formPart = new FormDefinition(scratch, FormComponentClass.Instance, "UserControl1");
        formPart.UpdateRootTypeName("VB.UserControl");

        var (ctl, _) = new FormSerializer().Serialize(formPart, code, "UserControl1.ctl");
        File.WriteAllText(Path.Join(dir, "UserControl1.ctl"), ctl);

        if (companionBlobs is not null)
        {
            var (ctx, _) = FrxSerializer.Write(companionBlobs);
            File.WriteAllBytes(Path.Join(dir, "UserControl1.ctx"), ctx);
        }

        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nUserControl=UserControl1.ctl\r\nName=\"Test\"\r\n");
        return vbp;
    }

    private static int Occurrences(string haystack, string needle)
    {
        var n = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    [Theory]
    [InlineData("Module=Gone; Gone.bas", "Gone.bas")]
    [InlineData("Class=Gone; Gone.cls", "Gone.cls")]
    [InlineData("Form=Gone.frm", "Gone.frm")]
    [InlineData("UserControl=Gone.ctl", "Gone.ctl")]
    [InlineData("PropertyPage=Gone.pag", "Gone.pag")]
    public async Task A_missing_member_is_marked_and_save_never_creates_its_file(string line, string file)
    {
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, $"Type=Exe\r\n{line}\r\nName=\"Test\"\r\n");
        var svc = MakeService(out var loaded);

        await svc.OpenProject(vbp);
        var project = loaded();
        project.Forms.Should().BeEmpty();
        project.Modules.Should().BeEmpty();
        project.PreservedItemLines.Should().Equal(line);
        var tool = new HexIDE.Tools.ProjectToolViewModel(Substitute.For<IProjectManager>(),
            Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), Substitute.For<IEditorService>(),
            Substitute.For<HexIDE.Localization.ILocalizationService>());
        using var tree = new HexIDE.Tools.ProjectViewModel(tool, project);
        tree.Elements.OfType<HexIDE.Tools.PreservedItemViewModel>().Should().ContainSingle()
            .Which.IsMissing.Should().BeTrue();

        await svc.SaveProject(project, saveAs: false);

        File.Exists(Path.Join(dir, file)).Should().BeFalse();
        File.ReadAllText(vbp).ReplaceLineEndings("\n").Should().Contain(line + "\n");
    }

    [Fact]
    public async Task A_UserControl_after_a_module_keeps_its_project_line_in_place()
    {
        var vbp = WriteProject("");
        File.WriteAllText(Path.Join(dir, "Module1.bas"), "Attribute VB_Name = \"Module1\"\r\n");
        var original = "Type=Exe\r\nModule=Module1; Module1.bas\r\nUserControl=UserControl1.ctl\r\nName=\"Test\"\r\n";
        File.WriteAllText(vbp, original);
        var svc = MakeService(out var project);

        await svc.OpenProject(vbp);
        await svc.SaveProject(project(), saveAs: false);

        (await File.ReadAllBytesAsync(vbp, TestContext.Current.CancellationToken)).Should().Equal(System.Text.Encoding.ASCII.GetBytes(original));
    }

    [Fact]
    public async Task A_missing_form_before_a_loaded_form_keeps_its_slot_when_rebased()
    {
        var vbp = Path.Join(dir, "Test.vbp");
        var scratch = new ProjectDefinition(VBProjectType.EXE, "Test");
        var form = new FormDefinition(scratch, FormComponentClass.Instance, "Loaded");
        var (frm, _) = new FormSerializer().Serialize(form, "Loaded.frm");
        File.WriteAllText(Path.Join(dir, "Loaded.frm"), frm);
        var original = "Type=Exe\r\nForm=Missing.frm\r\nObject=kept\r\nForm=Loaded.frm\r\nStartup=\"Loaded\"\r\nName=\"Test\"\r\n";
        File.WriteAllText(vbp, original);
        var svc = MakeService(out var project);

        await svc.OpenProject(vbp);
        await svc.SaveProject(project(), saveAs: false);
        (await File.ReadAllBytesAsync(vbp, TestContext.Current.CancellationToken)).Should().Equal(System.Text.Encoding.ASCII.GetBytes(original));

        var target = Path.Join(dir, "sub", "Test.vbp");
        var rebased = original.Replace("Form=Missing.frm", @"Form=..\Missing.frm")
            .Replace("Form=Loaded.frm", @"Form=..\Loaded.frm");
        new ProjectSerializer().Serialize(project(), target).Should().Be(rebased);
    }

    [Fact]
    public async Task Saving_a_loaded_UserControl_writes_the_visual_header_exactly_once()
    {
        var vbp = WriteProject("Public Sub Hello()\r\nEnd Sub\r\n");
        var ctlPath = Path.Join(dir, "UserControl1.ctl");
        var svc = MakeService(out var project);

        await svc.OpenProject(vbp);
        await svc.SaveProject(project(), saveAs: false);

        var saved = await File.ReadAllTextAsync(ctlPath, TestContext.Current.CancellationToken);
        Occurrences(saved, "Begin VB.UserControl").Should().Be(1);
        saved.Should().Contain("Public Sub Hello()");
    }

    [Fact]
    public async Task Repeated_saves_do_not_grow_the_file()
    {
        var vbp = WriteProject("Public Sub Hello()\r\nEnd Sub\r\n");
        var ctlPath = Path.Join(dir, "UserControl1.ctl");
        var svc = MakeService(out var project);

        await svc.OpenProject(vbp);
        await svc.SaveProject(project(), saveAs: false);
        var afterFirst = await File.ReadAllTextAsync(ctlPath, TestContext.Current.CancellationToken);
        await svc.SaveProject(project(), saveAs: false);
        var afterSecond = await File.ReadAllTextAsync(ctlPath, TestContext.Current.CancellationToken);

        afterSecond.Should().Be(afterFirst, "a save must be idempotent, not additive");
    }

    [Fact]
    public async Task Opening_a_UserControl_reads_its_ctx_companion_not_a_frx()
    {
        // The load path used to hardcode ".frx", so a UserControl's .ctx was never read — which left the
        // save path with no blobs and made it delete the companion it had never opened.
        var vbp = WriteProject("Public Sub Hello()\r\nEnd Sub\r\n",
                               companionBlobs: [[1, 2, 3, 4], [9, 9, 9]]);
        var ctxPath = Path.Join(dir, "UserControl1.ctx");
        var svc = MakeService(out _);

        await svc.OpenProject(vbp);

        // A baseline is recorded for every file the loader reads, so its presence proves the .ctx was read.
        baselineStore.TryGet(ctxPath).Should().NotBeNull();
    }

    [Fact]
    public async Task An_unparseable_ctl_round_trips_verbatim_rather_than_being_mangled()
    {
        var junk = "this is not a VB6 user control\r\n";
        var ctlPath = Path.Join(dir, "UserControl1.ctl");
        File.WriteAllText(ctlPath, junk);
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nUserControl=UserControl1.ctl\r\nName=\"Test\"\r\n");
        var svc = MakeService(out var project);

        await svc.OpenProject(vbp);
        await svc.SaveProject(project(), saveAs: false);

        (await File.ReadAllTextAsync(ctlPath, TestContext.Current.CancellationToken)).Should().Be(junk);
    }
}
