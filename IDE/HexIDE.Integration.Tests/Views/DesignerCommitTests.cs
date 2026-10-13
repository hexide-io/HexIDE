using System.IO;
using Avalonia.Headless.XUnit;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using NSubstitute;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A committed change in the designer announces itself once, with the model already flushed
/// (hexide-io/HexIDE#273 task 3.3).
/// </summary>
/// <remarks>
/// The header the code window shows in front of a form's code is rendered from
/// <c>FormDefinition.Components</c>, and the designer's working collections run ahead of that list until
/// something flushes them. So the two halves of this are inseparable: an announcement without the flush
/// renders the form as it was one control ago, and a flush without the announcement leaves the window
/// showing a header for a form that no longer exists.
/// </remarks>
public class DesignerCommitTests
{
    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "P");

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private static FormDefinition Deserialize(string source) =>
        new FormDeserializer().Deserialize(Project, source, NullSink.Instance)!;

    private const string OneLabel = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.Label Label1
              Caption         =   "Hi"
           End
        End
        Attribute VB_Name = "Form1"
        """;

    /// <summary>
    /// A command that does nothing on either side.
    /// </summary>
    /// <remarks>
    /// The subject here is the stack, not any command: a real one (LockControlsCommand) saves the form from
    /// its Undo, which would drag the whole project service into a test about when a notification is raised.
    /// </remarks>
    private sealed class NoopCommand : IDesignerCommand
    {
        public string Description => "noop";
        public void Execute(FormEditViewModel vm) { }
        public void Undo(FormEditViewModel vm) { }
    }

    private static FormEditViewModel NewDesignerVm(IEventBus bus) => new(
        null!, bus, null!, null!, null!, null!, Substitute.For<ILocalizationService>());

    [AvaloniaFact]
    public void ACommitAnnouncesItselfWithTheModelAlreadyFlushed()
    {
        // The ordering IS the test. Both facts are read inside the handler, because checking them after the
        // publish would pass just as happily if the flush ran second -- and second is useless: whatever
        // re-renders the form does so while handling this event.
        var bus = new EventBus();
        var form = Deserialize(OneLabel);
        var vm = NewDesignerVm(bus).Initialize(form);

        var spawned = new ComponentInstance(CommandButtonComponentClass.Instance, "Command1");
        var spawnedVm = new ComponentInstanceViewModel(vm, spawned);
        vm.AllComponents.Add(spawnedVm);
        vm.Components.Add(spawnedVm);

        var announcements = 0;
        var modelHadItWhenAnnounced = false;
        bus.Subscribe<FormLayoutChangedEvent>(e =>
        {
            announcements++;
            modelHadItWhenAnnounced = e.Form.Components.Contains(spawned);
        });

        vm.UndoStack.Push(new NoopCommand());

        announcements.Should().Be(1);
        modelHadItWhenAnnounced.Should().BeTrue(
            "a render taken on this event would otherwise miss the control just added");
    }

    [AvaloniaFact]
    public void UndoAndRedoAreCommitsToo()
    {
        // They change the model exactly as the original gesture did, and nothing else would announce it.
        var bus = new EventBus();
        var vm = NewDesignerVm(bus).Initialize(Deserialize(OneLabel));
        vm.UndoStack.Push(new NoopCommand());

        var announcements = 0;
        bus.Subscribe<FormLayoutChangedEvent>(_ => announcements++);

        vm.UndoStack.Undo();
        vm.UndoStack.Redo();

        announcements.Should().Be(2);
    }

    [AvaloniaFact]
    public void ClearingTheUndoStackIsNotACommit()
    {
        // Clear runs when the designer is rebuilt from a freshly-reloaded model. Treating it as a commit
        // would re-render the form and replace the text just read from disk with a reproduction of it --
        // which is precisely what the invariant forbids outside a save.
        var bus = new EventBus();
        var vm = NewDesignerVm(bus).Initialize(Deserialize(OneLabel));
        vm.UndoStack.Push(new NoopCommand());

        var announcements = 0;
        bus.Subscribe<FormLayoutChangedEvent>(_ => announcements++);

        vm.UndoStack.Clear();

        announcements.Should().Be(0);
    }

    [AvaloniaFact]
    public void ADragAnnouncesOnceWhenItEndsRatherThanPerPointerMove()
    {
        // Every pointer move writes the model through a two-way Canvas.Left binding. Announcing per write
        // would mean a full re-render of the designer text and a whole-document didChange to every attached
        // language server for each pixel of a drag; the undo stack's existing mid-drag discard is what makes
        // once-per-gesture free.
        var bus = new EventBus();
        var form = Deserialize(OneLabel);
        var vm = NewDesignerVm(bus).Initialize(form);
        var target = vm.Components[0];

        var announcements = 0;
        bus.Subscribe<FormLayoutChangedEvent>(_ => announcements++);

        vm.BeginDrag([target]);
        for (var x = 1; x <= 20; x++)
        {
            target.Left = x * 10;
            vm.UndoStack.Push(new NoopCommand());   // as a gesture mid-drag would
        }
        announcements.Should().Be(0, "nothing is committed until the gesture ends");

        vm.EndDrag();

        announcements.Should().Be(1);
    }

    private const string LabelAt120 = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.Label Label1
              Caption         =   "Hi"
              Height          =   240
              Left            =   120
              Top             =   120
              Width           =   1000
           End
        End
        Attribute VB_Name = "Form1"
        Option Explicit

        """;

    private static CodeEditorViewModel NewCodeEditor()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        return new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), Substitute.For<IProjectService>(),
            eventBus, Substitute.For<ILspClient>(), Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(), Substitute.For<IBreakpointService>(), Substitute.For<IDebugController>(),
            Substitute.For<IRunScope>(), Substitute.For<ILocalizationService>());
    }

    [AvaloniaFact]
    public void MovingAControlWithTheCodeWindowOpen()
    {
        // The code-editor delta's scenario, from the designer's own gesture to the open code window: the drag ends,
        // the designer's undo stack takes the move as one commit and announces it on the bus, and the header
        // refresher re-renders the form and re-heads the window. The code typed before the move is the developer's
        // last code edit, and the code window's undo has to take it and leave the move.
        var bus = new EventBus();
        var form = Deserialize(LabelAt120);
        form.AbsolutePath = Path.Combine(Path.GetTempPath(), "hexide-designer-commit", "Form1.frm");
        var open = new List<BaseEditorWindowViewModel>();
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(_ => open);
        using var refresher = new HeaderRefresher(bus, dock, new BreakpointService(), new BookmarkService());

        // Settled on a render before the window opens, so what changes below is the move and nothing else.
        refresher.LayoutChanged(form);
        using var editor = NewCodeEditor().Initialize(form);
        open.Add(editor);
        var asOpened = editor.BufferBody;
        editor.Document.Insert(editor.Document.TextLength, "Dim x As Long\r\n");
        var typed = editor.BufferBody;
        var headerBefore = form.DesignerText!;

        var designer = NewDesignerVm(bus).Initialize(form);
        var label = designer.Components.Single();
        designer.BeginDrag([label]);
        label.Left += 30;
        label.Top += 15;
        designer.EndDrag();

        var headerAfter = form.DesignerText!;
        var before = headerBefore.Split("\r\n");
        var after = headerAfter.Split("\r\n");
        after.Should().HaveSameCount(before);
        Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).Select(i => after[i].Trim()).ToList()
            .Should().HaveCount(2).And.OnlyContain(line => line.StartsWith("Left ") || line.StartsWith("Top "),
                "the header shows the control's new position, and nothing else of it changed");
        editor.Document.Text.Should().Be(headerAfter + typed, "the code window's header is the new one");
        editor.BufferBody.Should().Be(typed, "and its code is untouched");

        editor.UndoRequested(() => editor.Document.UndoStack.Undo());

        editor.BufferBody.Should().Be(asOpened, "undo in the code window still undoes the developer's last code edit");
        editor.Document.Text.Should().Be(headerAfter + asOpened, "and does not undo the move");
        form.DesignerText.Should().Be(headerAfter);
    }

    [AvaloniaFact]
    public void RenamingTheFormBecomesObservableOnTheCommit()
    {
        // The half of task 2.4a deferred to 3.3. FormDefinition.Name is derived from the root component's
        // (Name) property and has no setter, so it only raises PropertyChanged when UpdateComponents runs --
        // which is what the flush does. Before this, renaming a form in the property grid changed nothing
        // the tab title or the language layer could see until a save happened to flush.
        var bus = new EventBus();
        var form = Deserialize(OneLabel);
        var vm = NewDesignerVm(bus).Initialize(form);

        var renames = 0;
        form.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FormDefinition.Name)) renames++; };

        vm.Form!.Instance.SetProperty(VBProperties.NameProperty, "Renamed");
        renames.Should().Be(0, "a property written straight onto the root component announces nothing");

        vm.UndoStack.Push(new NoopCommand());

        renames.Should().Be(1);
        form.Name.Should().Be("Renamed");
    }
}
