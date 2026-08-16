using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// Covers the closed-form overwrite (issue #23).
///
/// A <c>.frm</c> that is not open in a tab used to be skipped by the file watcher
/// (<c>DirtyDetector.ClassifyForm</c> returned <c>Indeterminate</c> for <c>!IsOpen</c>), so its cached
/// model stayed at the pre-change content. Because <see cref="ProjectService.SaveProject"/> writes every
/// form unconditionally — <c>IsDirty</c> gates only the close-time prompt — the next save serialised that
/// stale model back over the file, silently discarding whatever had landed on disk.
///
/// These tests exercise the two halves at the service level: adopting an external change must leave the
/// form reported as clean (so the watcher classifies the *next* change correctly rather than raising a
/// false conflict), and a save afterwards must not revert the adopted content.
/// </summary>
public class ExternalFormChangeTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-extchange-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = new();

    public ExternalFormChangeTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private const string OneTextBox =
        "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n" +
        "   Begin VB.TextBox Text1 \r\n      Left            =   120\r\n   End\r\n" +
        "End\r\nAttribute VB_Name = \"Form1\"\r\n" +
        "Private Sub Form_Load()\r\nEnd Sub\r\n";

    /// <summary>What a `git pull` might land: a second control and different code.</summary>
    private const string TwoControlsPulled =
        "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n" +
        "   Begin VB.TextBox Text1 \r\n      Left            =   120\r\n   End\r\n" +
        "   Begin VB.CommandButton Command1 \r\n      Left            =   240\r\n   End\r\n" +
        "End\r\nAttribute VB_Name = \"Form1\"\r\n" +
        "Private Sub Form_Load()\r\n    Text1.Text = \"pulled\"\r\nEnd Sub\r\n";

    private ProjectService MakeService()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
                      .Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));

        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);

        return new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            Substitute.For<IWindowManager>(),
            Substitute.For<IEventBus>(),
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            Substitute.For<HexIDE.Localization.ILocalizationService>());
    }

    /// <summary>
    /// Opens the project and saves once, so every baseline matches this build's serializer output — a
    /// hand-authored fixture can differ cosmetically from what a save writes and would otherwise register
    /// as dirty for reasons unrelated to the test.
    /// </summary>
    private async Task<(ProjectService svc, FormDefinition form, string frmPath)> OpenNormalised()
    {
        var frmPath = Path.Join(dir, "Form1.frm");
        File.WriteAllText(frmPath, OneTextBox);
        File.WriteAllText(Path.Join(dir, "Test.vbp"), "Type=Exe\r\nForm=Form1.frm\r\nName=\"Test\"\r\n");

        var svc = MakeService();
        await svc.OpenProject(Path.Join(dir, "Test.vbp"));
        var project = loaded.Single();
        await svc.SaveProject(project, saveAs: false);
        return (svc, project.Forms.Single(), frmPath);
    }

    [Fact]
    public async Task UntouchedForm_ReportsNoUnsavedChanges()
    {
        var (svc, form, _) = await OpenNormalised();

        svc.HasUnsavedChanges(form).Should().BeFalse();
    }

    [Fact]
    public async Task EditedForm_ReportsUnsavedChanges()
    {
        var (svc, form, _) = await OpenNormalised();

        form.UpdateCode("Private Sub Form_Load()\r\n    ' edited in the IDE\r\nEnd Sub\r\n");

        svc.HasUnsavedChanges(form).Should().BeTrue();
    }

    [Fact]
    public async Task ReloadFromDisk_AdoptsExternalChange_AndLeavesFormClean()
    {
        var (svc, form, frmPath) = await OpenNormalised();

        File.WriteAllText(frmPath, TwoControlsPulled);
        (await svc.ReloadFormFromDisk(form)).Should().BeTrue();

        form.Code.Should().Contain("pulled");
        form.Components.Select(c => c.GetPropertyOrDefault(VBProperties.NameProperty))
            .Should().Contain("Command1");

        // The render baseline must be re-established by the reload; otherwise the untouched form reports
        // itself as edited — a spurious save prompt on close, and a false Conflict on the next external
        // change (which is what the watcher now asks HasUnsavedChanges to decide).
        svc.HasUnsavedChanges(form).Should().BeFalse();
    }

    [Fact]
    public async Task SaveAfterReload_DoesNotRevertTheExternalChange()
    {
        var (svc, form, frmPath) = await OpenNormalised();
        var project = loaded.Single();

        File.WriteAllText(frmPath, TwoControlsPulled);
        await svc.ReloadFormFromDisk(form);

        await svc.SaveProject(project, saveAs: false);

        // End-to-end guard rather than a regression test: this passes with or without the classifier fix,
        // because it calls ReloadFormFromDisk directly and so bypasses the skip that caused #23. What it
        // pins is the other half — that once the external change *is* adopted, the unconditional
        // save-every-form loop writes it back out rather than reverting it. The classifier itself is
        // covered by DirtyDetectorTests.NotOpenForm_*.
        var onDisk = File.ReadAllText(frmPath);
        onDisk.Should().Contain("pulled");
        onDisk.Should().Contain("Command1");
    }
}
