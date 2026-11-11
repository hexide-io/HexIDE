using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.ViewModels;

public class WorkspaceRenameTests : IDisposable
{
    private readonly ILspClient client = Substitute.For<ILspClient>();
    private readonly IProjectService projects = Substitute.For<IProjectService>();
    private readonly ILocalizationService localization = Substitute.For<ILocalizationService>();
    private readonly IStatusBarService status = Substitute.For<IStatusBarService>();
    private readonly IDocumentDockService dock = Substitute.For<IDocumentDockService>();
    private readonly List<BaseEditorWindowViewModel> windows = [];
    private readonly EditorService editors;
    private BaseEditorWindowViewModel? active;
    private readonly ModuleDefinition first;
    private readonly ModuleDefinition second;

    public WorkspaceRenameTests()
    {
        var project = TestHelpers.CreateProject();
        first = TestHelpers.CreateModule(project, "Module1");
        second = TestHelpers.CreateModule(project, "Module2");
        foreach (var module in new[] { first, second })
        {
            module.AbsolutePath = Path.GetFullPath(module.Name + ".bas");
            module.UpdateCode("Public Sub Greet()\r\n    Greet\r\nEnd Sub\r\n");
            project.AddModule(module);
        }
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns([project]);
        dock.OpenDocuments.Returns(windows);
        dock.When(d => d.OpenDocument(Arg.Any<BaseEditorWindowViewModel>()))
            .Do(c => { active = c.Arg<BaseEditorWindowViewModel>(); windows.Add(active); });
        dock.TryActivate(Arg.Any<Func<CodeEditorViewModel, bool>>())
            .Returns(c =>
            {
                var found = windows.OfType<CodeEditorViewModel>().FirstOrDefault(c.Arg<Func<CodeEditorViewModel, bool>>());
                if (found is null) return false;
                active = found;
                return true;
            });
        editors = new EditorService(dock, NewEditor, () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(), () => throw new InvalidOperationException(), () => manager, localization);
        localization.GetString("Str.CodeEditor.Msg.RenameDocumentRefused").Returns("Rename refused: {0}");
        localization.GetString("Str.CodeEditor.Msg.RenameStaleVersion").Returns("Cannot rename {0}: stale version. Try again.");
        localization.GetString("Str.CodeEditor.Msg.RenameUnsavedChanges").Returns("Cannot rename {0}: unsaved changes. Save and try again.");
        localization.GetString("Str.CodeEditor.Msg.RenamedInModules").Returns("Renamed in {0} modules");
    }

    private CodeEditorViewModel NewEditor() => new(
        Substitute.For<IWindowManager>(), editors, projects, Substitute.For<IEventBus>(), client,
        Substitute.For<ISettingsService>(), status, Substitute.For<IBookmarkService>(),
        Substitute.For<HexIDE.Debugging.IBreakpointService>(),
        Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(), Substitute.For<HexIDE.Debugging.IRunScope>(), localization,
        regionReader: new HexIDE.Runtime.Serialization.Vb6ReadOnlyRegionReader(),
        regionRules: new HexIDE.Runtime.Serialization.Vb6ReadOnlyRegionRules());

    private CodeEditorViewModel Open(ModuleDefinition module)
    {
        editors.EditCode(module);
        return windows.OfType<CodeEditorViewModel>().Single(w => w.ModuleDefinition == module);
    }

    private static string UriOf(ModuleDefinition module) => DocumentWireName.For(DocumentIdentity.For(module));

    private static TextEdit Rename(CodeEditorViewModel editor)
    {
        var offset = editor.Document.Text.IndexOf("Greet", StringComparison.Ordinal);
        var location = editor.Document.GetLocation(offset);
        return new(new(new(location.Line - 1, location.Column - 1), new(location.Line - 1, location.Column + 4)), "Welcome");
    }

    private static TextEdit RenameClosed()
    {
        // A standard module's file begins with Attribute VB_Name, then its code.
        return new(new(new(1, 11), new(1, 16)), "Welcome");
    }

    private async Task<string?> Answer(CodeEditorViewModel source, WorkspaceEdit edit, Func<string, RenameDocumentSnapshot?>? documentFor = null)
    {
        client.RequestRenameAsync(source.GetDocumentUriPublic(), Arg.Any<Position>(), "Welcome", Arg.Any<CancellationToken>())
            .Returns(new RenameResult(edit, null, documentFor));
        var answer = await source.RequestRenameAsync(new(1, 11), "Welcome");
        return source.ApplyRename(answer!.Edit!, "Greet", answer.DocumentFor);
    }

    [AvaloniaFact]
    public async Task StaleRefusalNamesTheMemberReasonAndAction()
    {
        var a = Open(first);
        var b = Open(second);
        (await Answer(a, new(DocumentChanges: [new(new(UriOf(second), 99), [Rename(b)])])))
            .Should().Be("Cannot rename Module2: stale version. Try again.");
    }

    [AvaloniaFact]
    public async Task RefusedRenameOpensNoWindow()
    {
        var a = Open(first);
        dock.ClearReceivedCalls();
        (await Answer(a, new(DocumentChanges:
            [new(new(UriOf(second), null), [RenameClosed()]), new(new(UriOf(first), 99), [Rename(a)])])))
            .Should().NotBeNull();
        dock.DidNotReceive().OpenDocument(Arg.Any<BaseEditorWindowViewModel>());
        windows.Should().ContainSingle().Which.Should().BeSameAs(a);
        active.Should().BeSameAs(a);
    }

    [AvaloniaFact]
    public async Task SuccessfulRenameReturnsToItsSourceAtTheRenamedName()
    {
        var a = Open(first);
        a.CaretOffset = a.Document.Text.IndexOf("Greet", StringComparison.Ordinal);
        (await Answer(a, new(new() { [UriOf(first)] = [Rename(a)], [UriOf(second)] = [RenameClosed()] })))
            .Should().BeNull();
        active.Should().BeSameAs(a);
        a.Document.GetText(a.CaretOffset, "Welcome".Length).Should().Be("Welcome");
    }

    [AvaloniaFact]
    public async Task TwoOpenModulesAreRenamedWithIndependentUndo()
    {
        var a = Open(first);
        var b = Open(second);
        var beforeA = a.Document.Text;
        var beforeB = b.Document.Text;
        (await Answer(a, new(new() { [UriOf(first)] = [Rename(a)], [UriOf(second)] = [Rename(b)] })))
            .Should().BeNull();
        a.Document.Text.Should().Contain("Sub Welcome()");
        b.Document.Text.Should().Contain("Sub Welcome()");
        status.Received().SetTemporaryMessage("Renamed in 2 modules", Arg.Any<TimeSpan>());
        a.Document.UndoStack.Undo();
        a.Document.Text.Should().Be(beforeA);
        b.Document.Text.Should().Contain("Sub Welcome()");
        b.Document.UndoStack.Undo();
        b.Document.Text.Should().Be(beforeB);
    }

    [AvaloniaFact]
    public async Task AnUnchangedClosedMemberOpensAndCanUndoItsRename()
    {
        var a = Open(first);
        var edit = new WorkspaceEdit(new() { [UriOf(first)] = [Rename(a)], [UriOf(second)] = [RenameClosed()] });
        (await Answer(a, edit)).Should().BeNull();
        var b = windows.OfType<CodeEditorViewModel>().Single(w => w.ModuleDefinition == second);
        b.Document.Text.Should().Contain("Sub Welcome()");
        b.Document.UndoStack.Undo();
        b.Document.Text.Should().Contain("Sub Greet()");
    }

    [AvaloniaFact]
    public async Task NullVersionAgainstUnsavedMemberRefusesEveryBuffer()
    {
        var a = Open(first);
        var b = Open(second);
        b.Document.Insert(b.Document.TextLength, "' unsaved\r\n");
        var beforeA = a.Document.Text;
        var beforeB = b.Document.Text;
        var edit = new WorkspaceEdit(DocumentChanges:
            [new(new(UriOf(first), 1), [Rename(a)]), new(new(UriOf(second), null), [Rename(b)])]);
        (await Answer(a, edit)).Should().Be("Cannot rename Module2: unsaved changes. Save and try again.");
        a.Document.Text.Should().Be(beforeA);
        b.Document.Text.Should().Be(beforeB);
    }

    [AvaloniaFact]
    public async Task StaleVersionRefusesEveryBuffer()
    {
        var a = Open(first);
        var b = Open(second);
        var beforeA = a.Document.Text;
        var beforeB = b.Document.Text;
        (await Answer(a, new(DocumentChanges:
            [new(new(UriOf(first), 1), [Rename(a)]), new(new(UriOf(second), 99), [Rename(b)])])))
            .Should().Be("Cannot rename Module2: stale version. Try again.");
        a.Document.Text.Should().Be(beforeA);
        b.Document.Text.Should().Be(beforeB);
    }

    [AvaloniaFact]
    public async Task SecondMembersHeaderRefusesEveryBufferAndNamesTheMember()
    {
        var a = Open(first);
        var b = Open(second);
        var beforeA = a.Document.Text;
        var beforeB = b.Document.Text;
        (await Answer(a, new(new() { [UriOf(first)] = [Rename(a)],
            [UriOf(second)] = [new(new(new(0, 0), new(0, 9)), "Changed")] })))
            .Should().Be("Rename refused: Module2");
        a.Document.Text.Should().Be(beforeA);
        b.Document.Text.Should().Be(beforeB);
    }

    [AvaloniaFact]
    public async Task CurrentVersionAppliesEvenWithUnsavedChangesSentToServer()
    {
        var a = Open(first);
        var b = Open(second);
        b.Document.Insert(b.Document.TextLength, "' unsaved\r\n");
        await b.FlushDocumentAsync();
        await client.Received().ChangeDocumentAsync(UriOf(second), 2, b.Document.Text, Arg.Any<CancellationToken>());
        (await Answer(a, new(DocumentChanges:
            [new(new(UriOf(first), 1), [Rename(a)]), new(new(UriOf(second), 2), [Rename(b)])])))
            .Should().BeNull();
        a.Document.Text.Should().Contain("Sub Welcome()");
        b.Document.Text.Should().Contain("Sub Welcome()");
    }

    [AvaloniaFact]
    public async Task VersionForNeverOpenedMemberStillRequiresCleanDiskState()
    {
        var a = Open(first);
        projects.HasUnsavedChanges(second).Returns(true);
        var before = a.Document.Text;
        (await Answer(a, new(DocumentChanges:
            [new(new(UriOf(first), 1), [Rename(a)]), new(new(UriOf(second), 1), [RenameClosed()])])))
            .Should().Be("Cannot rename Module2: unsaved changes. Save and try again.");
        a.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public async Task ResourceOperationRefusesBeforeAnyTextEdit()
    {
        var a = Open(first);
        var before = a.Document.Text;
        (await Answer(a, new(DocumentChanges:
            [new(new(UriOf(first), 1), [Rename(a)]), new(Kind: "delete", Uri: UriOf(second))])))
            .Should().Be("Rename refused: Module2");
        a.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public async Task VersionForNeverOpenedCleanMemberUsesDiskAndOpensItsWindow()
    {
        var a = Open(first);
        (await Answer(a, new(DocumentChanges:
            [new(new(UriOf(first), 1), [Rename(a)]), new(new(UriOf(second), 99), [RenameClosed()])])))
            .Should().BeNull();
        windows.OfType<CodeEditorViewModel>().Single(w => w.ModuleDefinition == second)
            .Document.Text.Should().Contain("Sub Welcome()");
    }

    [AvaloniaFact]
    public async Task AnAnswerEditingOnlyAnotherModuleIsAppliedThere()
    {
        var a = Open(first);
        var b = Open(second);
        var before = a.Document.Text;
        (await Answer(a, new(new() { [UriOf(second)] = [Rename(b)] }))).Should().BeNull();
        a.Document.Text.Should().Be(before);
        b.Document.Text.Should().Contain("Sub Welcome()");
    }

    [AvaloniaFact]
    public async Task UnsavedChangeNotYetSentRefusesEvenTheLastSentVersion()
    {
        var a = Open(first);
        var b = Open(second);
        b.Document.Insert(b.Document.TextLength, "' pending\r\n");
        var before = a.Document.Text;
        (await Answer(a, new(DocumentChanges:
            [new(new(UriOf(first), 1), [Rename(a)]), new(new(UriOf(second), 1), [Rename(b)])])))
            .Should().Be("Cannot rename Module2: stale version. Try again.");
        a.Document.Text.Should().Be(before);
        b.Document.Text.Should().Contain("Sub Greet()");
    }

    [AvaloniaFact]
    public async Task ReadOnlyFormRefusesEveryBuffer()
    {
        var a = Open(first);
        var form = TestHelpers.CreateForm(first.Owner, "Form1");
        form.UpdateCode("Public Sub Greet()\r\nEnd Sub\r\n");
        form.MarkUnfaithfulToSave(UnfaithfulSaveCause.UnreproducibleBinaryContent, "test");
        first.Owner.AddForm(form);
        editors.EditCode(form);
        var b = windows.OfType<CodeEditorViewModel>().Single(w => w.FormDefinition == form);
        b.IsReadOnly.Should().BeTrue();
        var before = a.Document.Text;
        (await Answer(a, new(DocumentChanges:
            [new(new(UriOf(first), 1), [Rename(a)]), new(new(b.GetDocumentUriPublic(), 1), [Rename(b)])])))
            .Should().Be("Rename refused: Form1");
        a.Document.Text.Should().Be(before);
        b.Document.Text.Should().Contain("Sub Greet()");
    }

    [AvaloniaTheory]
    [InlineData("range")]
    [InlineData("overlap")]
    [InlineData("duplicate")]
    public async Task MalformedEditsCannotPartlyWriteAnotherBuffer(string fault)
    {
        var a = Open(first);
        var b = Open(second);
        var beforeA = a.Document.Text;
        var beforeB = b.Document.Text;
        TextEdit[] edits = fault == "range"
            ? [new(new(new(999, 0), new(999, 1)), "bad")]
            : [Rename(b), Rename(b)];
        TextDocumentEdit[] changes = fault == "duplicate"
            ? [new(new(UriOf(first), 1), [Rename(a)]),
                new(new(UriOf(second), 1), [Rename(b)]), new(new(UriOf(second), 1), [Rename(b)])]
            : [new(new(UriOf(first), 1), [Rename(a)]), new(new(UriOf(second), 1), edits)];
        (await Answer(a, new(DocumentChanges: changes))).Should().Be("Rename refused: Module2");
        a.Document.Text.Should().Be(beforeA);
        b.Document.Text.Should().Be(beforeB);
    }

    [AvaloniaFact]
    public async Task TargetOpenedOnlyToAnotherBackendStillRequiresCleanDiskState()
    {
        var a = Open(first);
        var b = Open(second);
        b.Document.Insert(b.Document.TextLength, "' unsaved\r\n");
        await b.FlushDocumentAsync();
        var before = a.Document.Text;
        (await Answer(a, new(DocumentChanges:
            [new(new(UriOf(first), 1), [Rename(a)]), new(new(UriOf(second), 2), [Rename(b)])]),
            uri => uri == UriOf(first) ? new(1, a.Document.Text) : null)).Should().Be("Cannot rename Module2: unsaved changes. Save and try again.");
        a.Document.Text.Should().Be(before);
        b.Document.Text.Should().Contain("Sub Greet()");
    }

    [AvaloniaFact]
    public async Task VersionBelongsToTheAnsweringConnectionAfterAReplay()
    {
        var a = Open(first);
        var b = Open(second);
        b.Document.Insert(b.Document.TextLength, "' unsaved\r\n");
        await b.FlushDocumentAsync();
        (await Answer(a, new(DocumentChanges:
            [new(new(UriOf(first), 1), [Rename(a)]), new(new(UriOf(second), 1), [Rename(b)])]),
            uri => new(1, uri == UriOf(first) ? a.Document.Text : b.Document.Text))).Should().BeNull();
        a.Document.Text.Should().Contain("Sub Welcome()");
        b.Document.Text.Should().Contain("Sub Welcome()");

        // A backend that has not received the next edit still reports the same version, but its
        // snapshot describes older text. The registry's current version cannot make that safe.
        var serverText = b.Document.Text;
        b.Document.Insert(b.Document.TextLength, "' newer\r\n");
        await b.FlushDocumentAsync();
        var before = b.Document.Text;
        (await Answer(a, new(DocumentChanges: [new(new(UriOf(second), 1), [Rename(b)])]),
            _ => new(1, serverText))).Should().Be("Cannot rename Module2: stale version. Try again.");
        b.Document.Text.Should().Be(before);
    }

    public void Dispose()
    {
        foreach (var window in windows) window.Dispose();
    }
}
