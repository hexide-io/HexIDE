using System.Linq;
using Avalonia.Headless.XUnit;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Language;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Addins;

/// <summary>
/// The IDE's own composition hands its code windows and its Object Browser the registry the provider registers its editing
/// helpers in (#5403), so they colour, grey and list declarations as before the helpers were registered.
/// </summary>
public class LanguageEditingCompositionTests
{
    [AvaloniaFact]
    public async Task The_composed_code_window_and_Object_Browser_ask_the_helpers_the_provider_registered()
    {
        await using var real = new RealComposition();
        var composition = real.Composition;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Sub A()\r\nEnd Sub\r\n");
        project.AddModule(module);
        // The main view lays out the dock a code window opens in.
        var root = composition.Root;

        composition.EditorService.EditCode(module);
        var editor = composition.EditorService.FindOpenCode(DocumentIdentity.For(module));

        editor.Should().NotBeNull();
        try
        {
            editor!.Brackets!.Brackets("(x)").Should().HaveCount(2);
            editor.ConditionalCode!.InactiveLines("#If 0 Then\nx\n#End If\n", new(ConditionalConstants.NoArguments))
                .Should().NotBeEmpty();
            root.ObjectBrowser.LanguageEditing!.DeclarationsFor("vb6")
                .Declarations("Sub A()\nEnd Sub").Select(s => s.Name).Should().Equal("A");
        }
        finally
        {
            // Unloaded first, so the window's close leaves no project holder open on the language layer: the module is
            // new and unsaved, and a modified project member's close keeps its document open there otherwise.
            composition.EventBus.Publish(new ProjectUnloadedEvent(project));
            editor?.Dispose();
        }
    }
}
