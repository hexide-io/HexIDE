using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
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

    // HEXIDE_ROUNDTRIP_CORPUS FIRST, because that is the variable the corpus is actually configured under
    // (SerializationCorpusTests reads it, CLAUDE.md documents it, and it is what a developer sets). This
    // file read only VB6_TEMPLATES, so on a machine where the corpus was present and correctly configured
    // every test in it returned at the `vbp is null` guard and passed in 25 ms without asserting anything.
    // A guard that skips is indistinguishable from a guard that passes, which is precisely how verification
    // tooling fails open.
    private static readonly string Vb6Template =
        FirstExistingDirectory(
            Environment.GetEnvironmentVariable("HEXIDE_ROUNDTRIP_CORPUS")?.Split(';', StringSplitOptions.RemoveEmptyEntries),
            Environment.GetEnvironmentVariable("VB6_TEMPLATES"),
            @"C:\Program Files (x86)\Microsoft Visual Studio\VB98\Template");

    private static string FirstExistingDirectory(string[]? candidates, params string?[] fallbacks)
    {
        foreach (var c in (candidates ?? []).Concat(fallbacks))
            if (!string.IsNullOrWhiteSpace(c) && Directory.Exists(c))
                return c;
        return fallbacks[^1]!;
    }

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

    // ------------------------------------------------------------------------------------------------
    // #148 — a save writes BOTH halves or NEITHER.
    //
    // FrxSerializer gives each record the offset of wherever it lands, so the .frm's citations are only
    // meaningful against the exact companion produced alongside them. Writing the text while refusing the
    // companion leaves citations addressing a partition that is no longer there — and because a form's
    // faithfulness is re-derived from its own citations, the damaged pair reopens as FAITHFUL.
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The invariant, stated directly: the two files on disk must be the two halves of ONE Serialize
    /// result. Saving is deterministic over an unchanged model, so re-serializing yields exactly what the
    /// save should have written — and any half that was skipped or written from a different run shows up.
    /// </summary>
    private static void AssertDiskHoldsOneSerializeResult(FormDefinition form, string frmPath)
    {
        var (expectedText, expectedCompanion) = new FormSerializer().Serialize(form, Path.GetFileName(frmPath));
        var frxPath = Path.ChangeExtension(frmPath, ".frx");

        Vb6TextFile.ReadAllText(frmPath).Should().Be(expectedText,
            "the designer text on disk must be the half the serializer produced");

        if (expectedCompanion is { Length: > 0 })
        {
            File.Exists(frxPath).Should().BeTrue("the form cites companion content, so it must be beside it");
            File.ReadAllBytes(frxPath).Should().Equal(expectedCompanion,
                "the companion must be the OTHER half of that same result — one written without the other "
              + "leaves every citation addressing a partition that does not exist");
        }
    }

    [Fact]
    public async Task Dropping_a_blob_bearing_control_keeps_both_halves_in_step()
    {
        // Button ListBox carries five blobs across five controls, so removing one moves every offset after
        // it. That is the mutation the old gate could not survive: it compared a walk of the produced
        // companion (4) against a load-time constant (5), refused the companion write, and wrote the .frm
        // anyway — with four freshly renumbered citations pointing into the untouched five-record file.
        var vbp = StageVb6Form(@"Controls\Button ListBox.frm");
        if (vbp is null) return;

        var svc = MakeService();
        await svc.OpenProject(vbp);
        var form = loaded.Single().Forms.Single();
        var frmPath = form.AbsolutePath!;

        var blobBearing = form.Components.First(c =>
            c.BaseClass.Properties.Any(p => p.PropertyType == typeof(byte[])
                                            && c.TryGetBoxedProperty(p, out var v) && v is byte[]));
        form.UpdateComponents(form.Components.Where(c => c != blobBearing).ToList());

        await svc.SaveProject(loaded.Single(), saveAs: false);

        AssertDiskHoldsOneSerializeResult(form, frmPath);
    }

    [Fact]
    public async Task A_form_that_reproduces_its_companion_is_never_denied_the_write()
    {
        // ODBC Log In was refused its companion on EVERY save while reproducing it byte for byte. The two
        // sides of the old comparison were counted by different readers: the load partitioned by cited
        // offset (3), the save walked flat length-prefixed records (2) — a reader whose own documentation
        // says it is wrong for exactly the List/ItemData records this form carries.
        //
        // A plain save hid it, because the bytes it declined to write happened to equal the bytes already
        // there. Perturbing the file on disk makes the refusal observable.
        var vbp = StageVb6Form(@"Forms\ODBC Log In.frm");
        if (vbp is null) return;

        var frxPath = Directory.EnumerateFiles(dir, "*.frx").Single();

        var svc = MakeService();
        await svc.OpenProject(vbp);
        var form = loaded.Single().Forms.Single();

        // AFTER the load, so the model does not absorb it. The last cited record runs to end-of-file, so a
        // byte appended BEFORE loading simply becomes part of that record and is faithfully written back —
        // which says nothing about whether the save wrote the companion at all.
        await File.WriteAllBytesAsync(frxPath, (await File.ReadAllBytesAsync(frxPath)).Append((byte)0xEE).ToArray());

        await svc.SaveProject(loaded.Single(), saveAs: false);

        File.ReadAllBytes(frxPath).Should().NotContain((byte)0xEE,
            "the form reproduces its companion exactly, so the save must write it rather than decline");
        AssertDiskHoldsOneSerializeResult(form, form.AbsolutePath!);
    }

    [Fact]
    public async Task A_companion_nothing_cites_is_left_alone_rather_than_deleted()
    {
        // A form citing nothing produces no blobs, every save, forever. Reading that as "the developer
        // cleared the last picture" deletes a file this form never referenced and never modelled — its
        // bytes are read by falling back to a flat walk and are not held anywhere else.
        //
        // The old blob-count comparison blocked this by accident. Removing it made the deletion reachable,
        // so the guard is now stated in terms of what the designer file actually cites.
        var frm = Path.Join(dir, "Bare.frm");
        await File.WriteAllTextAsync(frm,
            "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption = \"x\"\r\nEnd\r\nAttribute VB_Name = \"Bare\"\r\n");
        var orphan = Path.ChangeExtension(frm, ".frx");
        var orphanBytes = new byte[] { 4, 0, 0, 0, 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(orphan, orphanBytes);

        var vbp = Path.Join(dir, "Test.vbp");
        await File.WriteAllTextAsync(vbp, "Type=Exe\r\nForm=Bare.frm\r\nName=\"Test\"\r\n");

        var svc = MakeService();
        await svc.OpenProject(vbp);
        await svc.SaveProject(loaded.Single(), saveAs: false);

        File.Exists(orphan).Should().BeTrue("a companion this form never cited is not the save's to delete");
        File.ReadAllBytes(orphan).Should().Equal(orphanBytes);
    }
}
