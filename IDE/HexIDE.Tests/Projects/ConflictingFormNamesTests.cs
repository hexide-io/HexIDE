using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// A form whose <c>Begin</c> line and <c>Attribute VB_Name</c> name different forms is opened as VB6 opens it: under the
/// <c>Begin</c> line's name, with a warning, and with the attribute rewritten to match so the next save writes it so
/// (#1407). HexIDE took the right name but said nothing, and saved the contradiction back.
/// </summary>
/// <remarks>
/// Measured on the oracle (the oracle's "A form whose two names disagree"). vb6.exe builds such a form under the
/// <c>Begin</c> line's name and logs "Conflicting names were found in '…\Form1.frm'.  The name 'frmRenamed' will be
/// used." The VB6 IDE shows that message as the project opens, and its next save writes
/// <c>Attribute VB_Name = "frmRenamed"</c>.
/// </remarks>
public class ConflictingFormNamesTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-conflicting-names-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = new();
    private readonly IWindowManager windowManager = Substitute.For<IWindowManager>();

    public ConflictingFormNamesTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private ProjectService MakeService()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
                      .Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));

        windowManager.ShowDialog(Arg.Any<IDialog>()).Returns(ci =>
        {
            if (ci.Arg<IDialog>() is SaveChangesViewModel vm) vm.Yes();
            return Task.FromResult(true);
        });

        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);

        var localization = Substitute.For<HexIDE.Localization.ILocalizationService>();
        localization.GetString("Str.Dialog.ConflictingNames")
            .Returns("Conflicting names were found in '{0}'. The name '{1}' will be used.");

        return new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            windowManager,
            Substitute.For<IEventBus>(),
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            localization,
            Substitute.For<IHeaderRefresher>());
    }

    private async Task<string> Stage(string beginName, string vbName)
    {
        var token = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Join(dir, "Form1.frm"),
            "VERSION 5.00\r\n" +
            $"Begin VB.Form {beginName} \r\n" +
            "   Caption         =   \"Orders\"\r\n" +
            "   ClientHeight    =   1200\r\n" +
            "   ClientLeft      =   60\r\n" +
            "   ClientTop       =   450\r\n" +
            "   ClientWidth     =   3000\r\n" +
            "   LinkTopic       =   \"Form1\"\r\n" +
            "   ScaleHeight     =   1200\r\n" +
            "   ScaleWidth      =   3000\r\n" +
            "End\r\n" +
            $"Attribute VB_Name = \"{vbName}\"\r\n" +
            "Attribute VB_GlobalNameSpace = False\r\n" +
            "Attribute VB_Creatable = False\r\n" +
            "Attribute VB_PredeclaredId = True\r\n" +
            "Attribute VB_Exposed = False\r\n" +
            "Option Explicit\r\n", token);
        var vbp = Path.Join(dir, "P.vbp");
        await File.WriteAllTextAsync(vbp,
            $"Type=Exe\r\nForm=Form1.frm\r\nStartup=\"{beginName}\"\r\nName=\"P\"\r\n", token);
        return vbp;
    }

    [Fact]
    public async Task A_form_whose_names_disagree_takes_its_Begin_name_and_says_so()
    {
        var vbp = await Stage("frmRenamed", "Form1");
        var svc = MakeService();

        await svc.OpenProject(vbp);

        var form = loaded.Single().Forms.Single();
        form.Name.Should().Be("frmRenamed");
        await windowManager.Received(1).MessageBox(
            $"Conflicting names were found in '{Path.Join(dir, "Form1.frm")}'. The name 'frmRenamed' will be used.",
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), MessageBoxIcon.Warning, Arg.Any<Avalonia.Controls.Window?>());
    }

    [Fact]
    public async Task Its_VB_Name_is_made_to_agree_and_the_form_comes_up_edited()
    {
        var vbp = await Stage("frmRenamed", "Form1");
        var svc = MakeService();

        await svc.OpenProject(vbp);

        var form = loaded.Single().Forms.Single();
        form.Code.Should().StartWith("Attribute VB_Name = \"frmRenamed\"\r\nAttribute VB_GlobalNameSpace = False\r\n");
        svc.HasUnsavedChanges(form).Should().BeTrue("the file on disk still says Form1, and only a save makes it agree");

        await svc.SaveProject(loaded.Single(), saveAs: false);
        (await File.ReadAllTextAsync(Path.Join(dir, "Form1.frm"), TestContext.Current.CancellationToken))
            .Should().Contain("Begin VB.Form frmRenamed \r\n")
            .And.Contain("Attribute VB_Name = \"frmRenamed\"\r\n")
            .And.Contain("LinkTopic       =   \"Form1\"\r\n", "VB6's save left LinkTopic as it was");
    }

    [Fact]
    public async Task Names_that_differ_only_in_case_disagree_too()
    {
        // Measured: vb6.exe logs the same warning for VB_Name "FRMRENAMED" beside Begin frmRenamed, and uses frmRenamed.
        var vbp = await Stage("frmRenamed", "FRMRENAMED");
        var svc = MakeService();

        await svc.OpenProject(vbp);

        loaded.Single().Forms.Single().Code.Should().StartWith("Attribute VB_Name = \"frmRenamed\"\r\n");
        await windowManager.Received(1).MessageBox(
            $"Conflicting names were found in '{Path.Join(dir, "Form1.frm")}'. The name 'frmRenamed' will be used.",
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), MessageBoxIcon.Warning, Arg.Any<Avalonia.Controls.Window?>());
    }

    [Fact]
    public async Task A_form_whose_names_agree_opens_unedited_and_unremarked()
    {
        var vbp = await Stage("frmOrders", "frmOrders");
        var svc = MakeService();

        await svc.OpenProject(vbp);

        svc.HasUnsavedChanges(loaded.Single().Forms.Single()).Should().BeFalse();
        await windowManager.DidNotReceiveWithAnyArgs().MessageBox(default!);
    }
}
