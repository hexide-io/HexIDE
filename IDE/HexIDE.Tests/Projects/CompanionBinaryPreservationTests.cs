using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// Guards against destroying a companion binary on save (issue #17).
///
/// A form whose blob-backed properties HexIDE does not model has those property lines dropped on load, so
/// the save produces either a truncated companion or none at all — and "none at all" was read by the write
/// path as *delete it*. Verified destruction: 2122 bytes of `Button ListBox.frx`.
///
/// The modelled set has since grown: <c>Form.Icon</c>, <c>PictureBox.Picture</c>, <c>CommandButton.Picture</c>
/// and <c>ListBox.DragIcon</c> are all held now, which is why <c>Button ListBox.frm</c> and
/// <c>Mover ListBox.frm</c> round-trip byte for byte, companion included. They stay in the theory below
/// anyway: this guard is about a save never DESTROYING a companion, and a form that reproduces its own is
/// still the strongest case that nothing was clobbered on the way through.
///
/// What is still unmodelled, and what the loss-flag test therefore uses: a blob on a <c>VB.Image</c>, a
/// control class HexIDE does not model at all.
///
/// The fixture is VB6's own shipped form, so it exercises the real shape rather than a contrived one. It is
/// skipped where VB6 is not installed (CI is Linux).
/// </summary>
public class CompanionBinaryPreservationTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-frx-" + Guid.NewGuid().ToString("N"));

    private static readonly string Vb6Template =
        Environment.GetEnvironmentVariable("VB6_TEMPLATES")
        ?? @"C:\Program Files (x86)\Microsoft Visual Studio\VB98\Template";

    public CompanionBinaryPreservationTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private readonly List<ProjectDefinition> loaded = new();

    private ProjectService MakeService()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
                      .Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));

        var windowManager = Substitute.For<IWindowManager>();
        windowManager.ShowDialog(Arg.Any<IDialog>()).Returns(ci =>
        {
            if (ci.Arg<IDialog>() is SaveChangesViewModel vm) vm.Yes();
            return Task.FromResult(true);
        });

        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);

        return new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            windowManager,
            Substitute.For<IEventBus>(),
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            Substitute.For<HexIDE.Localization.ILocalizationService>());
    }

    /// <summary>Copies a VB6-shipped form + its .frx into the scratch dir with a .vbp. Null if VB6 absent.</summary>
    private string? StageVb6Form(string relativeFormPath)
    {
        var srcFrm = Path.Join(Vb6Template, relativeFormPath);
        var srcFrx = Path.ChangeExtension(srcFrm, ".frx");
        if (!File.Exists(srcFrm) || !File.Exists(srcFrx)) return null;

        var name = Path.GetFileNameWithoutExtension(srcFrm).Replace(" ", "");
        var dstFrm = Path.Join(dir, name + ".frm");
        File.Copy(srcFrm, dstFrm);
        File.Copy(srcFrx, Path.ChangeExtension(dstFrm, ".frx"));

        // The .frm references its companion by the ORIGINAL name, so rewrite those references to match.
        var text = File.ReadAllText(dstFrm)
            .Replace(Path.GetFileName(srcFrx), name + ".frx", StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(dstFrm, text);

        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, $"Type=Exe\r\nForm={name}.frm\r\nName=\"Test\"\r\n");
        return vbp;
    }

    [Theory]
    [InlineData(@"Controls\Button ListBox.frm")]   // DragIcon + 4 CommandButton.Picture — 2122 bytes
    [InlineData(@"Controls\Mover ListBox.frm")]    // 516 bytes
    [InlineData(@"Forms\Splash Screen.frm")]       // was truncated 790 -> 12
    public async Task Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce(string relativeFormPath)
    {
        var vbp = StageVb6Form(relativeFormPath);
        if (vbp is null) return; // VB6 not installed (CI)

        var frxPath = Directory.EnumerateFiles(dir, "*.frx").Single();
        var before = await File.ReadAllBytesAsync(frxPath);
        before.Length.Should().BeGreaterThan(12, "the fixture must actually carry blobs");

        var svc = MakeService();
        await svc.OpenProject(vbp);
        await svc.SaveProject(loaded.Single(), saveAs: false);

        File.Exists(frxPath).Should().BeTrue("a save must never delete a companion it cannot reproduce");
        var after = await File.ReadAllBytesAsync(frxPath);
        after.Should().Equal(before, "the companion holds the only copy of those images");
    }

    [Fact]
    public async Task A_second_save_still_leaves_the_companion_intact()
    {
        var vbp = StageVb6Form(@"Controls\Button ListBox.frm");
        if (vbp is null) return;

        var frxPath = Directory.EnumerateFiles(dir, "*.frx").Single();
        var before = await File.ReadAllBytesAsync(frxPath);

        var svc = MakeService();
        await svc.OpenProject(vbp);
        var project = loaded.Single();
        await svc.SaveProject(project, saveAs: false);
        await svc.SaveProject(project, saveAs: false);

        (await File.ReadAllBytesAsync(frxPath)).Should().Equal(before);
    }

    [Fact]
    public async Task The_form_records_that_it_lost_binary_fidelity()
    {
        // Web Browser, and this is the third fixture this test has had. Button ListBox stopped exercising
        // it when CommandButton.Picture and ListBox.DragIcon were modelled; Splash Screen stopped when
        // VB.Image was. Each time the form began reproducing its own companion, the flag correctly stopped
        // firing, and the test failed for the best possible reason.
        //
        // Web Browser is the durable choice: its six Pictures sit on an MSComctlLib.ImageList, and hosting
        // third-party ActiveX controls is out of scope. If this one ever stops losing binary content, that
        // is a change worth noticing rather than a fixture to swap out.
        var vbp = StageVb6Form(@"Forms\Web Browser.frm");
        if (vbp is null) return;

        var svc = MakeService();
        await svc.OpenProject(vbp);

        // The CONCLUSION, not one of the two mechanisms that can reach it. ProjectService.WouldLoseBlobs
        // has two arms: the explicit HasUnmodelledBinaryProperties flag, and a comparison of blobs written
        // against blobs read — the safety net for properties that are named but whose CLR type is unmapped
        // and which vanish with no diagnostic at all. Splash Screen trips the second: its Form.Icon is
        // captured, the VB.Image's Picture is not, so one blob out of two reaches the model.
        //
        // Asserting the flag alone made this test claim a regression when the arm simply changed.
        loaded.Single().Forms.Single().UnfaithfulSaveCauses
            .Should().HaveFlag(UnfaithfulSaveCause.UnreproducibleBinaryContent,
                "this is what holds the form read-only and stops the save touching the companion");
    }

    [Fact]
    public async Task AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag()
    {
        // The other half, and the one that keeps the flag honest. A signal that fires for everything
        // protects nothing: it would hold every form read-only forever and the burndown could never move.
        var vbp = StageVb6Form(@"Controls\Button ListBox.frm");
        if (vbp is null) return;

        var svc = MakeService();
        await svc.OpenProject(vbp);

        var form = loaded.Single().Forms.Single();
        form.HasUnmodelledBinaryProperties.Should().BeFalse(
            "every blob this form cites is on a modelled property now — Form.Icon, CommandButton.Picture "
          + "and ListBox.DragIcon — so there is nothing it cannot reproduce");
        form.CanSaveFaithfully.Should().BeTrue("and so it must no longer be held read-only");
    }
}
