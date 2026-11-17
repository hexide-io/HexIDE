using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.Components;
using HexIDE.Tools.ObjectBrowser;

namespace HexIDE.Tests.ViewModels;

/// <summary>Declaration-list contracts from hexide-io/HexIDE#4401, matching the bundled server.</summary>
public class UnopenedModuleMembersTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BrowserUsesTextWhenNoSymbolsAreAvailable(bool running)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        var module = new ModuleDefinition(project, "Module2", ModuleKind.StandardModule);
        module.UpdateCode("Public counter As Long\nSub Bump()\nEnd Sub\nFunction Twice&()\nEnd Function\nProperty Get Value() As Long\nEnd Property\nEnum Choices\nOne\nEnd Enum\nType Record\nvalue As Long\nEnd Type");
        module.RecordOriginalHeader("Attribute VB_Name = \"Module2\"\r\n");
        project.AddModule(module);
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        var client = Substitute.For<ILspClient>();
        client.IsRunning.Returns(running);
        client.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
        var editor = Substitute.For<IEditorService>();
        var browser = new ObjectBrowserToolViewModel(projects, client, editor,
            Substitute.For<IComponentRegistry>(), Substitute.For<ITypeLibraryService>(),
            Substitute.For<IFocusedProjectUtil>(), Substitute.For<ILocalizationService>(), Substitute.For<ILanguageConnectionRegistry>());
        browser.SelectedLibrary = browser.Libraries.Single(l => l.Name == "Project1");
        browser.SelectedClass = browser.SelectedLibrary.Classes.Single(c => c.Name == "Module2");
        browser.FilteredMembers.Select(m => m.Name).Should().Equal("Bump", "Twice", "Value (Get)", "Choices", "Record");
        browser.FilteredMembers.Select(m => m.Kind).Should().Equal(OBMemberKind.Method, OBMemberKind.Method,
            OBMemberKind.Property, OBMemberKind.Constant, OBMemberKind.Constant);
        browser.SelectedMember = browser.FilteredMembers[0];
        browser.GoToDefinitionCommand.Execute(null);
        editor.Received(1).NavigateTo(DocumentWireName.For(module), 3, 5);
        if (running) _ = client.Received(1).RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
