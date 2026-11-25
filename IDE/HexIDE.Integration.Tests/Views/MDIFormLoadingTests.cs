using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Views;

// Saved root and Explorer label measured in https://github.com/hexide-io/HexIDE/issues/5313.
public class MDIFormLoadingTests
{
    private static string Corpus()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Join(root.FullName, "corpus", "designer"))) root = root.Parent;
        return Path.Join(root!.FullName, "corpus", "designer", "mdi");
    }

    [AvaloniaFact]
    public async Task The_Explorer_lists_the_MDI_form_beside_the_child_and_its_code_window_edits()
    {
        var owned = new RealComposition();
        var composition = owned.Composition;
        _ = composition.Root;
        var directory = Path.Join(Path.GetTempPath(), "hexide-mdi-editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.EnumerateFiles(Corpus())) File.Copy(file, Path.Join(directory, Path.GetFileName(file)));
        try
        {
            await composition.ProjectService.OpenProject(Path.Join(directory, "MDIProbe.vbp"));
            var project = composition.ProjectManager.LoadedProjects.Single();
            var mdi = project.Forms.Single(form => form.Name == "MDIForm1");
            var tree = new ProjectToolViewModel(composition.ProjectManager, new EventBus(), composition.ProjectService,
                composition.EditorService, composition.LocalizationService);
            using var node = new ProjectViewModel(tree, project);
            var formNode = node.Elements.OfType<FormViewModel>().Single(form => form.FormDefinition == mdi);
            formNode.AccessibleName.Should().Be("MDIForm1 (MDIForm1.frm)");
            project.Forms.Select(form => form.Name).Should().Equal("MDIForm1", "Form1");
            composition.EditorService.EditCode(mdi);
            var editor = composition.DocumentDockService.OpenDocuments.OfType<CodeEditorViewModel>().Single();
            editor.IsReadOnly.Should().BeFalse();
            var before = File.ReadAllText(mdi.AbsolutePath!);
            var addition = "\r\nPublic Sub Hello()\r\nEnd Sub\r\n";
            editor.Document.Insert(editor.Document.TextLength, addition);
            await editor.FlushDocumentAsync();
            await composition.ProjectService.SaveForm(mdi, saveAs: false, interactive: false);
            File.ReadAllBytes(mdi.AbsolutePath!).Should().Equal(System.Text.Encoding.ASCII.GetBytes(before + addition));
        }
        finally
        {
            composition.DocumentDockService.CloseAll();
            foreach (var project in composition.ProjectManager.LoadedProjects.ToArray()) composition.ProjectManager.UnloadProject(project);
            // The code window started the bundled language server in the project's folder, its working directory when
            // nothing else is set (LspLaunchDirectory.For). Windows won't delete a folder a live process is in, so stop
            // the server before deleting it.
            await owned.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void View_Object_and_Explorer_open_refuse_the_designer(bool doubleClick)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, File.ReadAllText(Path.Join(Corpus(), "MDIForm1.frm")), new Sink());
        form.Should().NotBeNull();
        project.AddForm(form!);
        var windows = Substitute.For<IWindowManager>();
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.MDIForm.DesignerNotImplemented").Returns("The MDI form designer is not implemented (#5313).");
        var dock = Substitute.For<IDocumentDockService>();
        var editors = new EditorService(windows, dock, null!, null!, null!, () => throw new InvalidOperationException("No designer should open"),
            () => Substitute.For<IProjectManager>(), localization);
        var tool = new ProjectToolViewModel(Substitute.For<IProjectManager>(), new EventBus(),
            Substitute.For<HexIDE.Projects.IProjectService>(), editors, localization);
        using var node = new ProjectViewModel(tool, project);
        using var child = new FormViewModel(node, form!);
        tool.SelectedForm = child;
        if (doubleClick) tool.OpenSelected(); else tool.ViewObjectCommand.Execute(null);
        windows.Received(1).MessageBox(Arg.Is<string>(text => text.Contains("#5313")),
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<MessageBoxLayout>());
        dock.DidNotReceiveWithAnyArgs().OpenDocument(null!);
    }

    private sealed class Sink : IDeserializeErrorSink { public void LogError(string message) { } }
}
