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
/// HexIDE flattens nested Begin blocks, so a menu hierarchy is destroyed and a container's children are
/// re-parented to the form. VB6 then rejects the file outright whenever a menu carries a shortcut or a
/// separator — which is nearly every real menu. Writing it would move the defect to outcome 3 (works here,
/// fails in VB6), the worst kind because it is silent.
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

    private const string MenuForm =
        "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n" +
        "   Begin VB.Menu mnuFile \r\n      Caption         =   \"&File\"\r\n" +
        "      Begin VB.Menu mnuFileNew \r\n         Caption         =   \"&New\"\r\n" +
        "         Shortcut        =   ^N\r\n      End\r\n   End\r\n" +
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
        await svc.OpenProject(Stage(MenuForm));
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
        await svc.OpenProject(Stage(MenuForm));

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
        await svc.OpenProject(Stage(MenuForm));

        loaded.Single().Forms.Single().UnfaithfulSaveReason
            .Should().Contain("nested").And.Contain("flatten");
    }

    [Fact]
    public async Task The_message_is_localized_and_agrees_in_number()
    {
        // The first version injected an English reason string into an English frame:
        // "These forms were not saved, because it contains…" — plural frame, singular reason, and the
        // reason itself was a hardcoded literal that appeared untranslated in every language.
        var svc = MakeService();
        await svc.OpenProject(Stage(MenuForm));

        await svc.SaveProject(loaded.Single(), saveAs: false);

        requestedKeys.Should().Contain("Str.Dialog.UnfaithfulSave.Body.One",
            "one refused form must use the singular message");
        requestedKeys.Should().NotContain("Str.Dialog.UnfaithfulSave.Body.Many");
    }

    [Fact]
    public async Task Two_refused_forms_use_the_plural_message()
    {
        File.WriteAllText(Path.Join(dir, "Form1.frm"), MenuForm);
        File.WriteAllText(Path.Join(dir, "Form2.frm"), MenuForm.Replace("Form1", "Form2"));
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
        File.WriteAllText(Path.Join(dir, "Form1.frm"), MenuForm);
        File.WriteAllText(Path.Join(dir, "Form2.frm"), MenuForm.Replace("Form1", "Form2"));
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nForm=Form1.frm\r\nForm=Form2.frm\r\nName=\"Test\"\r\n");

        var svc = MakeService();
        await svc.OpenProject(vbp);
        await svc.SaveProject(loaded.Single(), saveAs: false);

        warningsShown.Should().Be(1);
    }
}
