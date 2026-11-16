using System.Linq;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

public class LoadedFormMenuTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new System.InvalidOperationException(error);
    }

    // The loaded form reported in #4140, including properties the Menu Editor cannot edit.
    private const string MenuForm = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.Menu mnuFile
              Caption = "&File"
              Begin VB.Menu mnuOpen
                 Caption = "&Open"
                 Shortcut = ^O
              End
              Begin VB.Menu mnuMRU
                 Caption = "First"
                 Index = 0
              End
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static FormEditViewModel Open()
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            MenuForm, new Sink())!;
        return new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(),
            null!, null!, Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>(),
            new Vb6ProjectFileFormat()).Initialize(form);
    }

    [AvaloniaFact]
    public void LoadedMenus_PopulateTheDesignerTree()
    {
        using var vm = Open();

        vm.TopLevelMenu.Select(c => c.Name).Should().Equal("mnuFile");
        var children = vm.TopLevelMenu.Single().Instance.GetPropertyOrDefault(MenuComponentClass.SubItemsProperty)!;
        children.Select(c => c.GetPropertyOrDefault(VBProperties.NameProperty)).Should().Equal("mnuOpen", "mnuMRU");
        children.Should().Equal(vm.AllComponents.Single(c => c.Name == "mnuOpen").Instance,
            vm.AllComponents.Single(c => c.Name == "mnuMRU").Instance);
    }

    [AvaloniaFact]
    public void LoadedMenus_AppearInTheMenuEditorWithTheirIndentation()
    {
        using var vm = Open();

        var editor = new MenuEditorViewModel(vm);

        editor.FlatMenu.Select(c => c.Name).Should().Equal("mnuFile", "mnuOpen", "mnuMRU");
        editor.FlatMenu.Select(c => c.Indent).Should().Equal(0, 1, 1);
    }

    [AvaloniaFact]
    public void LoadingMenus_LeavesTheDocumentUnmodified()
    {
        using var vm = Open();

        vm.IsModified.Should().BeFalse();
        vm.UndoStack.CanUndo.Should().BeFalse();
        vm.HasComponentsNotInModel.Should().BeFalse();
    }
}
