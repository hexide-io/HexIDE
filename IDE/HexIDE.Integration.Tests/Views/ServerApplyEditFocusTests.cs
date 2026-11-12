using HexIDE.Runtime.Serialization;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Dock.Avalonia.Controls;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public class ServerApplyEditFocusTests
{
    [AvaloniaFact]
    public async Task LensEditingAnotherOpenMemberKeepsKeyboardFocusAndCaret()
    {
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        var dock = composition.DocumentDockService;
        var host = new DockControl { Layout = composition.Root.Layout };
        host.DataTemplates.Add(new FuncDataTemplate<CodeEditorViewModel>((model, _) =>
            new CodeEditorView { DataContext = model }));
        var window = new Window { Content = host, Width = 800, Height = 600 };
        var client = Substitute.For<ILspClient>();
        client.IsRunning.Returns(true);
        var manager = Substitute.For<IProjectManager>();
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var first = new ModuleDefinition(project, "First", ModuleKind.StandardModule);
        var second = new ModuleDefinition(project, "Second", ModuleKind.StandardModule);
        foreach (var module in new[] { first, second })
        {
            module.UpdateCode("Sub Greet()\r\nEnd Sub\r\n");
            project.AddModule(module);
        }
        manager.LoadedProjects.Returns([project]);
        var localization = new LocalizationService();
        localization.Apply("en");
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        EditorService editors = null!;
        CodeEditorViewModel NewEditor() => new(Substitute.For<IWindowManager>(), editors,
            Substitute.For<IProjectService>(), new EventBus(), client, settings,
            Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(),
            Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(), localization, new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader());
        editors = new EditorService(dock, NewEditor, () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(), () => throw new InvalidOperationException(), () => manager, localization);
        try
        {
            window.Show();
            editors.EditCode(first);
            editors.EditCode(second);
            editors.EditCode(first);
            await Settle();
            var a = dock.OpenDocuments.OfType<CodeEditorViewModel>().Single(vm => vm.ModuleDefinition == first);
            var b = dock.OpenDocuments.OfType<CodeEditorViewModel>().Single(vm => vm.ModuleDefinition == second);
            var editor = host.GetVisualDescendants().OfType<CodeEditorView>().Single(v => v.DataContext == a)
                .FindControl<TextEditor>("TextEditor")!;
            editor.CaretOffset = editor.Document.Text.IndexOf("Greet", StringComparison.Ordinal) + 2;
            var caret = editor.CaretOffset;
            editor.TextArea.Focus().Should().BeTrue();
            editor.IsKeyboardFocusWithin.Should().BeTrue();
            var lostFocus = 0;
            editor.LostFocus += (_, _) => lostFocus++;
            var offset = b.Document.Text.IndexOf("Greet", StringComparison.Ordinal);
            var position = b.Document.GetLocation(offset);
            var edit = new WorkspaceEdit(new() { [b.GetDocumentUriPublic()] =
                [new TextEdit(new(new(position.Line - 1, position.Column - 1),
                    new(position.Line - 1, position.Column + 4)), "Welcome")] });
            client.ExecuteCommandAsync("test.edit", Arg.Any<System.Text.Json.JsonElement[]>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    editors.ApplyWorkspaceEdit(edit, _ => null).Should().BeNull();
                    return Task.FromResult<System.Text.Json.JsonElement?>(null);
                });
            await a.RunCodeLensAsync(new Command("Edit", "test.edit"));
            await Settle();
            b.Document.Text.Should().Contain("Sub Welcome()");
            dock.ActiveTab.Should().BeSameAs(a);
            editor.IsKeyboardFocusWithin.Should().BeTrue();
            editor.CaretOffset.Should().Be(caret);
            lostFocus.Should().Be(0, "applying the edit must not activate another tab");
        }
        finally
        {
            dock.CloseAll();
            window.Close();
        }
    }

    private static async Task Settle()
    {
        for (var i = 0; i < 3; i++)
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }
}
