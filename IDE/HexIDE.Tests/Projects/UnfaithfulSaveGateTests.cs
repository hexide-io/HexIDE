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
}
