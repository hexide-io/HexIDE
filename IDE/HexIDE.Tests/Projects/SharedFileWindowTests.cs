using System.IO;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;
using HexIDE.Tests.ViewModels;

namespace HexIDE.Tests.Projects;

// VB6 SP6: docs/vb6-fidelity-oracle/a-file-shared-by-two-projects-of-a-group.md (#5201).
public sealed class SharedFileWindowTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("hexide-shared-windows-").FullName;
    private readonly EventBus events = new();
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private readonly List<string> questions = [];
    private readonly List<MessageBoxButtons> questionButtons = [];
    private readonly Queue<MessageBoxResult> answers = new();
    private readonly ProjectManager manager;
    private readonly ProjectService service;
    private readonly DocumentDockService documents;
    private readonly EditorService editors;
    private readonly MainViewViewModel.DockFactory factory;
    private object[] copies = [];
    private string path = "";

    public SharedFileWindowTests()
    {
        var strings = Substitute.For<ILocalizationService>();
        strings.GetString(Arg.Any<string>()).Returns(c => c.Arg<string>() + " {0} {1}");
        windows.MessageBox(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(),
            Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>(), Arg.Any<MessageBoxLayout>()).Returns(c =>
        {
            questions.Add(c.ArgAt<string>(0));
            questionButtons.Add(c.Arg<MessageBoxButtons>());
            return answers.Count > 0 ? answers.Dequeue() : MessageBoxResult.No;
        });
        (factory, _) = DockFactoryTests.CreateFactory();
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);
        documents = new DocumentDockService(new DockFactoryReference { Value = factory }, events);
        manager = new ProjectManager(Substitute.For<IEditorService>(), events);
        service = new ProjectService(() => throw new InvalidOperationException(), windows, events, manager,
            Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(),
            Substitute.For<IUserSidecarService>(), new FileBaselineStore(), strings,
            Substitute.For<IHeaderRefresher>(), InBoxRegistry.Create(), documents: documents);
        editors = new EditorService(windows, documents, () => ExternalFormChangeTests.Editor(service, events),
            () => throw new InvalidOperationException(), () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(), () => manager, strings, events, () => service);
    }

    public void Dispose()
    {
        documents.CloseAll();
        foreach (var project in manager.LoadedProjects)
            events.Publish(new HexIDE.Events.ProjectUnloadedEvent(project));
        documents.Dispose();
        Directory.Delete(root, true);
    }

    private async Task Open(string extension)
    {
        path = Path.Join(root, "Shared." + extension);
        var header = extension switch
        {
            "frm" => "VERSION 5.00\r\nBegin VB.Form Shared\r\n   Caption = \"Shared\"\r\n   ClientHeight = 1500\r\n   ClientWidth = 3000\r\nEnd\r\n",
            "cls" => "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1\r\nEND\r\n",
            _ => ""
        };
        File.WriteAllText(path, header + "Attribute VB_Name = \"Shared\"\r\n'original\r\n");
        var line = extension == "frm" ? "Form=..\\Shared.frm"
            : $"{(extension == "cls" ? "Class" : "Module")}=Shared; ..\\Shared.{extension}";
        foreach (var name in new[] { "P1", "P2" })
        {
            Directory.CreateDirectory(Path.Join(root, name));
            File.WriteAllText(Path.Join(root, name, name + ".vbp"), $"Type=Exe\r\n{line}\r\nName=\"{name}\"\r\n");
        }
        File.WriteAllText(Path.Join(root, "Together.vbg"), "VBGROUP 5.0\r\nProject=P1\\P1.vbp\r\nStartupProject=P2\\P2.vbp\r\n");
        await service.OpenProject(Path.Join(root, "Together.vbg"));
        copies = manager.LoadedProjects.Select(p => extension == "frm" ? (object)p.Forms.Single() : p.Modules.Single()).ToArray();
        questions.Clear();
        questionButtons.Clear();
    }

    private void EditCode(int copy)
    {
        if (copies[copy] is FormDefinition form) editors.EditCode(form);
        else editors.EditCode((ModuleDefinition)copies[copy]);
    }
    private CodeEditorViewModel Editor => documents.OpenDocuments.OfType<CodeEditorViewModel>().Single();
    private void Dirty() => Editor.Document.Text += "'edited\r\n";

    [AvaloniaTheory]
    [InlineData("bas", false), InlineData("bas", true), InlineData("cls", false), InlineData("cls", true), InlineData("frm", false), InlineData("frm", true)]
    public async Task Open_copy_warns_and_activates_existing_code_window(string extension, bool dirty)
    {
        await Open(extension);
        EditCode(0);
        var first = Editor;
        if (dirty) Dirty();
        // A different active tab proves activation, rather than merely leaving the old window open.
        var other = new ProjectDocumentViewModel(Substitute.For<ILocalizationService>()).Initialize(manager.LoadedProjects[0]);
        documents.OpenDocument(other);
        EditCode(1);
        questions.Should().ContainSingle(q => q.StartsWith("Str.Dialog.SharedFile.AlreadyOpen", StringComparison.Ordinal));
        documents.OpenDocuments.OfType<CodeEditorViewModel>().Should().ContainSingle().Which.Should().BeSameAs(first);
        documents.ActiveDocument.Should().BeSameAs(first);
        questionButtons.Should().Equal(MessageBoxButtons.Ok);
    }

    [AvaloniaTheory]
    [InlineData("bas", false), InlineData("bas", true), InlineData("cls", false), InlineData("cls", true), InlineData("frm", false), InlineData("frm", true)]
    public async Task Person_close_asks_and_saves_only_on_yes(string extension, bool save)
    {
        await Open(extension);
        EditCode(0);
        Dirty();
        answers.Enqueue(save ? MessageBoxResult.Yes : MessageBoxResult.No);
        answers.Enqueue(MessageBoxResult.No); // Round 1 update question.
        factory.CloseDockable(Editor); // The tab close command's real route.
        documents.OpenDocuments.Should().BeEmpty();
        questions.Should().HaveCount(save ? 2 : 1);
        questions[0].Should().Be("Str.Dialog.SharedFile.SaveOnClose Shared P1");
        questionButtons.Should().OnlyContain(buttons => buttons == MessageBoxButtons.YesNo);
        if (save) questions[1].Should().StartWith("Str.Dialog.SharedFile.Update");
        File.ReadAllText(path).Contains("'edited", StringComparison.Ordinal).Should().Be(save);
    }

    [AvaloniaTheory]
    [InlineData("bas"), InlineData("cls"), InlineData("frm")]
    public async Task Automation_tab_close_uses_the_person_close_route(string extension)
    {
        await Open(extension);
        EditCode(0);
        Dirty();
        var tab = Editor;
        documents.TryCloseAny(d => ReferenceEquals(d, tab)).Should().BeTrue();
        questions.Should().ContainSingle(q => q.StartsWith("Str.Dialog.SharedFile.SaveOnClose", StringComparison.Ordinal));
        documents.OpenDocuments.Should().BeEmpty();
        File.ReadAllText(path).Should().NotContain("'edited");
    }

    [AvaloniaTheory]
    [InlineData("bas"), InlineData("cls"), InlineData("frm")]
    public async Task Closed_dirty_copy_does_not_block_opening_another(string extension)
    {
        await Open(extension);
        EditCode(0);
        Dirty();
        factory.CloseDockable(Editor);
        questions.Clear();
        EditCode(1);
        questions.Should().BeEmpty();
        ((object?)Editor.FormDefinition ?? Editor.ModuleDefinition).Should().BeSameAs(copies[1]);
    }

    [AvaloniaTheory]
    [InlineData("bas"), InlineData("cls"), InlineData("frm")]
    public async Task Clean_person_close_asks_nothing(string extension)
    {
        await Open(extension);
        EditCode(0);
        factory.CloseDockable(Editor);
        questions.Should().BeEmpty();
        documents.OpenDocuments.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData("bas", false), InlineData("bas", true), InlineData("cls", false), InlineData("cls", true), InlineData("frm", false), InlineData("frm", true)]
    public async Task Ide_close_asks_no_shared_window_question(string extension, bool unload)
    {
        await Open(extension);
        EditCode(0);
        Dirty();
        if (unload) events.Publish(new HexIDE.Events.ProjectUnloadedEvent(manager.LoadedProjects[0]));
        else documents.CloseAll();
        questions.Should().BeEmpty();
        documents.OpenDocuments.Should().BeEmpty();
    }
}
