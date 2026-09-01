using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// Guards the refuse-to-save gate for forms HexIDE cannot reproduce (issues #21, #22).
///
/// HexIDE flattens nested Begin blocks, so a container's children are re-parented to the form. Writing
/// that would move the defect to outcome 3 (works here, fails in VB6), the worst kind because it is
/// silent.
///
/// Menu hierarchies used to be gated for the same reason and no longer are — they survive a round-trip
/// as of #83, so the fixtures here use container nesting, which is still flattened (#84).
///
/// Refusing moves it to outcome 0 instead: the operation fails, the file is untouched, and the developer
/// finds out immediately. See docs/serialization-outcomes.md.
/// </summary>
public class UnfaithfulSaveGateTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-gate-" + Guid.NewGuid().ToString("N"));

    public UnfaithfulSaveGateTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private readonly List<ProjectDefinition> loaded = new();
    private int warningsShown;
    private readonly List<string> requestedKeys = new();
    private IWindowManager? lastWindowManager;

    private ProjectService MakeService()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
                      .Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));

        var windowManager = Substitute.For<IWindowManager>();
        lastWindowManager = windowManager;
        windowManager.ShowDialog(Arg.Any<IDialog>()).Returns(ci =>
        {
            if (ci.Arg<IDialog>() is SaveChangesViewModel vm) vm.Yes();
            return Task.FromResult(true);
        });
        windowManager.MessageBox(Arg.Any<string>(), Arg.Any<string>(),
                                 Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>())
                     .Returns(_ => { warningsShown++; return Task.FromResult(MessageBoxResult.Ok); });

        var localization = Substitute.For<HexIDE.Localization.ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(ci => { requestedKeys.Add(ci.Arg<string>()); return "{0}"; });

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
            localization);
    }

    private const string FlatForm =
        "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n" +
        "   Begin VB.TextBox Text1 \r\n      Left            =   120\r\n   End\r\n" +
        "End\r\nAttribute VB_Name = \"Form1\"\r\n";

    /// <summary>
    /// A control inside a container — still flattened on save, so still gated.
    ///
    /// This fixture used to be a nested menu, which is no longer gated: menu hierarchies survive a
    /// round-trip as of #83, so a menu form would exercise nothing here. Container nesting is #84.
    /// </summary>
    /// <summary>
    /// A form the gate still refuses. It used to be a CommandButton inside a Frame, which was the canonical
    /// unreproducible shape; containers now round-trip, so the fixture moved to what remains — a control
    /// nested under a class that is not a container.
    ///
    /// The format permits writing this and VB6 loads it without complaint, so it is corrupt input rather than
    /// an exotic container: HexIDE has nowhere to host the button, records no containment link for it, and a
    /// save would re-parent it onto the form still carrying its container-relative coordinates.
    /// </summary>
    private const string NestedContainerForm =
        "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n" +
        "   Begin VB.ListBox List1 \r\n" +
        "      Begin VB.CommandButton Command1 \r\n         Caption         =   \"Command1\"\r\n" +
        "      End\r\n   End\r\n" +
        "End\r\nAttribute VB_Name = \"Form1\"\r\n";

    private string Stage(string formText)
    {
        File.WriteAllText(Path.Join(dir, "Form1.frm"), formText);
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nForm=Form1.frm\r\nName=\"Test\"\r\n");
        return vbp;
    }

    [Fact]
    public async Task A_nested_form_is_left_untouched_by_a_save()
    {
        var svc = MakeService();
        await svc.OpenProject(Stage(NestedContainerForm));
        var formPath = Path.Join(dir, "Form1.frm");
        var before = await File.ReadAllTextAsync(formPath);

        await svc.SaveProject(loaded.Single(), saveAs: false);

        (await File.ReadAllTextAsync(formPath)).Should().Be(before,
            "a form HexIDE cannot reproduce must not be rewritten");
    }

    [Fact]
    public async Task The_developer_is_told_the_form_was_not_saved()
    {
        // Silence would be the worst outcome: the file is protected but the user believes it was written.
        var svc = MakeService();
        await svc.OpenProject(Stage(NestedContainerForm));

        await svc.SaveProject(loaded.Single(), saveAs: false);

        warningsShown.Should().Be(1);
    }

    [Fact]
    public async Task A_flat_form_still_saves_normally()
    {
        // The gate must be narrow. A form with no nesting past root->child is reproducible and unaffected.
        var svc = MakeService();
        await svc.OpenProject(Stage(FlatForm));
        var form = loaded.Single().Forms.Single();

        form.CanSaveFaithfully.Should().BeTrue();
        await svc.SaveProject(loaded.Single(), saveAs: false);

        warningsShown.Should().Be(0);
        (await File.ReadAllTextAsync(Path.Join(dir, "Form1.frm"))).Should().Contain("Begin VB.TextBox Text1");
    }

    [Fact]
    public async Task The_reason_names_what_is_wrong()
    {
        var svc = MakeService();
        await svc.OpenProject(Stage(NestedContainerForm));

        // The wording moved with the gate: "flatten onto the form" described what a save did to a container's
        // children, which no longer happens. What is left is re-parenting a control out of a class that cannot
        // host it.
        loaded.Single().Forms.Single().UnfaithfulSaveReason
            .Should().Contain("nests").And.Contain("not a container").And.Contain("re-parent");
    }

    [Fact]
    public async Task The_message_is_localized_and_agrees_in_number()
    {
        // The first version injected an English reason string into an English frame:
        // "These forms were not saved, because it contains…" — plural frame, singular reason, and the
        // reason itself was a hardcoded literal that appeared untranslated in every language.
        var svc = MakeService();
        await svc.OpenProject(Stage(NestedContainerForm));

        await svc.SaveProject(loaded.Single(), saveAs: false);

        requestedKeys.Should().Contain("Str.Dialog.UnfaithfulSave.Body.One",
            "one refused form must use the singular message");
        requestedKeys.Should().NotContain("Str.Dialog.UnfaithfulSave.Body.Many");
    }

    [Fact]
    public async Task Two_refused_forms_use_the_plural_message()
    {
        File.WriteAllText(Path.Join(dir, "Form1.frm"), NestedContainerForm);
        File.WriteAllText(Path.Join(dir, "Form2.frm"), NestedContainerForm.Replace("Form1", "Form2"));
        File.WriteAllText(Path.Join(dir, "Test.vbp"),
            "Type=Exe\r\nForm=Form1.frm\r\nForm=Form2.frm\r\nName=\"Test\"\r\n");

        var svc = MakeService();
        await svc.OpenProject(Path.Join(dir, "Test.vbp"));
        await svc.SaveProject(loaded.Single(), saveAs: false);

        requestedKeys.Should().Contain("Str.Dialog.UnfaithfulSave.Body.Many");
        requestedKeys.Should().NotContain("Str.Dialog.UnfaithfulSave.Body.One");
    }

    [Fact]
    public async Task One_message_covers_a_whole_batch()
    {
        // Two unfaithful forms must not produce two dialogs.
        File.WriteAllText(Path.Join(dir, "Form1.frm"), NestedContainerForm);
        File.WriteAllText(Path.Join(dir, "Form2.frm"), NestedContainerForm.Replace("Form1", "Form2"));
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nForm=Form1.frm\r\nForm=Form2.frm\r\nName=\"Test\"\r\n");

        var svc = MakeService();
        await svc.OpenProject(vbp);
        await svc.SaveProject(loaded.Single(), saveAs: false);

        warningsShown.Should().Be(1);
    }

    // ── Save As is gated too (#143) ──────────────────────────────────────────────────────────────────
    //
    // These are the cases that were missing. The suite drove `saveAs: false` exclusively, so the bypass
    // — `!form.CanSaveFaithfully && !saveAs` — was invisible to it.
    //
    // The bypass was not merely a hole in the gate. A copy written at a new path loses its blobs, because
    // WriteCompanionBinary can only protect a companion that already exists; and it then REOPENS AS
    // FAITHFUL, because the citations that flagged it are exactly what went missing. A recoverable
    // refusal turned into a file that looks clean and is not.

    [Fact]
    public async Task Save_as_refuses_a_form_it_cannot_reproduce()
    {
        var svc = MakeService();
        await svc.OpenProject(Stage(NestedContainerForm));
        var form = loaded.Single().Forms.Single();
        var target = Path.Join(dir, "Copy.frm");

        await svc.SaveForm(form, saveAs: true);

        File.Exists(target).Should().BeFalse("a copy HexIDE cannot reproduce must not be written either");
        Directory.GetFiles(dir, "*.frm").Should().ContainSingle(
            "Save As must not have produced a second, degraded form");
    }

    [Fact]
    public async Task Save_as_leaves_the_original_untouched()
    {
        var svc = MakeService();
        await svc.OpenProject(Stage(NestedContainerForm));
        var formPath = Path.Join(dir, "Form1.frm");
        var before = await File.ReadAllTextAsync(formPath);

        await svc.SaveForm(loaded.Single().Forms.Single(), saveAs: true);

        (await File.ReadAllTextAsync(formPath)).Should().Be(before);
    }

    [Fact]
    public async Task Save_as_tells_the_developer_at_the_moment_it_refuses()
    {
        // The lone-save path used not to report at all: the refusal sat in the pending list and surfaced
        // during the NEXT unrelated Save Project, naming a file that was not in that batch.
        var svc = MakeService();
        await svc.OpenProject(Stage(NestedContainerForm));

        await svc.SaveForm(loaded.Single().Forms.Single(), saveAs: true);

        warningsShown.Should().Be(1, "a refusal must be reported when it happens, not banked for a later batch");
    }

    [Fact]
    public async Task A_lone_refusal_is_not_replayed_into_the_next_save()
    {
        // The consequence of banking it: a second, unrelated save reported a file it had not been asked
        // to write. Draining at the point of refusal is what stops that.
        var svc = MakeService();
        await svc.OpenProject(Stage(NestedContainerForm));
        var project = loaded.Single();

        await svc.SaveForm(project.Forms.Single(), saveAs: false);
        warningsShown.Should().Be(1);

        await svc.SaveProject(project, saveAs: false);

        warningsShown.Should().Be(2, "the batch reports its own refusal once — not that one plus the earlier one twice");
    }

    [Fact]
    public async Task A_faithful_form_still_saves_as_normally()
    {
        // The gate must not have swallowed the ordinary path: a reproducible form still writes.
        var svc = MakeService();
        await svc.OpenProject(Stage(FlatForm));
        var form = loaded.Single().Forms.Single();

        await svc.SaveForm(form, saveAs: false);

        warningsShown.Should().Be(0, "nothing was refused");
        File.Exists(Path.Join(dir, "Form1.frm")).Should().BeTrue();
    }

    // ── #147: the gate applies to a UserControl, and refuses ABOVE its picker ────────────────
    //
    // A .ctl has a designer half and a companion beside it, so everything above applies to it — and until
    // now none of it was tested here. The repo carries no .ctl or .ctx fixture anywhere, so this path had
    // never been exercised at all.

    private const string NestedContainerUserControl =
        "VERSION 5.00\r\nBegin VB.UserControl UserControl1 \r\n   ClientHeight    =   3000\r\n" +
        "   Begin VB.ListBox List1 \r\n" +
        "      Begin VB.CommandButton Command1 \r\n         Caption         =   \"Command1\"\r\n" +
        "      End\r\n   End\r\n" +
        "End\r\nAttribute VB_Name = \"UserControl1\"\r\n";

    private const string FlatUserControl =
        "VERSION 5.00\r\nBegin VB.UserControl UserControl1 \r\n   ClientHeight    =   3000\r\n" +
        "   Begin VB.TextBox Text1 \r\n      Left            =   120\r\n   End\r\n" +
        "End\r\nAttribute VB_Name = \"UserControl1\"\r\n";

    private string StageUserControl(string ctlText)
    {
        File.WriteAllText(Path.Join(dir, "UserControl1.ctl"), ctlText);
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nUserControl=UserControl1.ctl\r\nName=\"Test\"\r\n");
        return vbp;
    }

    [Fact]
    public async Task A_UserControl_the_gate_refuses_is_left_untouched_by_a_save()
    {
        var svc = MakeService();
        await svc.OpenProject(StageUserControl(NestedContainerUserControl));
        var ctlPath = Path.Join(dir, "UserControl1.ctl");
        var before = await File.ReadAllTextAsync(ctlPath);

        await svc.SaveProject(loaded.Single(), saveAs: false);

        (await File.ReadAllTextAsync(ctlPath)).Should().Be(before,
            "a .ctl HexIDE cannot reproduce is left alone, exactly as a .frm is");
    }

    [Fact]
    public async Task Save_as_on_a_UserControl_refuses_before_asking_for_a_destination()
    {
        // The refusal used to sit BELOW the picker, so the developer was asked where to put a file that
        // was never going to be written. The merged requirement rules that out in terms — "no destination
        // is asked for" — and it was honoured for forms and not for modules.
        var svc = MakeService();
        await svc.OpenProject(StageUserControl(NestedContainerUserControl));

        var written = await svc.SaveModule(loaded.Single().Modules.Single(), saveAs: true);

        written.Should().BeFalse();
        await lastWindowManager!.DidNotReceive().SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>());
    }

    [Fact]
    public async Task SaveModule_reports_a_refusal_to_its_caller()
    {
        // Not cosmetic: the MCP write tools answered MutateResult(true, null) after any save that did not
        // throw, so a refused write was reported to an agent as success. An agent has no dialog to read
        // and will build on the answer.
        var svc = MakeService();
        await svc.OpenProject(StageUserControl(NestedContainerUserControl));

        var written = await svc.SaveModule(loaded.Single().Modules.Single(), saveAs: false);

        written.Should().BeFalse("the file was not written, and the caller has no other way to tell");
    }

    [Fact]
    public async Task A_lone_UserControl_refusal_is_reported_when_it_happens()
    {
        var svc = MakeService();
        await svc.OpenProject(StageUserControl(NestedContainerUserControl));

        await svc.SaveModule(loaded.Single().Modules.Single(), saveAs: false);

        warningsShown.Should().Be(1,
            "a lone module refusal is reported at the moment it happens, like a lone form refusal — "
          + "otherwise it survives to surface during the next unrelated save");
    }

    [Fact]
    public async Task A_faithful_UserControl_still_saves()
    {
        // The over-reach guard: hoisting the gate above the picker must not refuse an ordinary .ctl.
        var svc = MakeService();
        await svc.OpenProject(StageUserControl(FlatUserControl));

        var written = await svc.SaveModule(loaded.Single().Modules.Single(), saveAs: false);

        written.Should().BeTrue();
        warningsShown.Should().Be(0, "nothing was refused");
        File.Exists(Path.Join(dir, "UserControl1.ctl")).Should().BeTrue();
    }
}
