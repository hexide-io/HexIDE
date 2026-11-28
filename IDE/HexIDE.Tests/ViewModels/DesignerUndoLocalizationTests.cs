using Avalonia;
using AwesomeAssertions.Execution;
using HexIDE.Desktop.Server;
using HexIDE.Events;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Commands;

namespace HexIDE.Tests.ViewModels;

public class DesignerUndoLocalizationTests
{
    private const string KeyPrefix = "Str.Designer.Undo.";

    private static ILocalizationService ReadPack(string id)
    {
        var pack = new LocalizationService();
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => pack.GetStringFrom(id, call.Arg<string>()));
        return localization;
    }

    private static FormEditViewModel Open(ILocalizationService? localization = null)
    {
        localization ??= Substitute.For<ILocalizationService>();
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(), InBoxRegistry.Create(), localization);
        var settings = Substitute.For<ISettingsService>();
        settings.GridWidth.Returns(8);
        settings.GridHeight.Returns(8);
        var root = new ComponentInstance(FormComponentClass.Instance, "Form1");
        var line = new ComponentInstance(LineComponentClass.Instance, "Line1")
            .SetProperty(VBProperties.X1Property, 100d).SetProperty(VBProperties.Y1Property, 100d)
            .SetProperty(VBProperties.X2Property, 200d).SetProperty(VBProperties.Y2Property, 200d);
        var form = new FormDefinition(TestHelpers.CreateProject(),
            [root, Button("Command1", 270, 330, 435, 390), Button("Command2", 1350, 1500, 570, 510),
                Button("Command3", 3000, 3300, 720, 660), line], "");
        return new FormEditViewModel(toolbox, new EventBus(), Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), Substitute.For<IWindowManager>(), settings, localization).Initialize(form);
    }

    private static ComponentInstance Button(string name, int left, int top, int width, int height) =>
        new ComponentInstance(CommandButtonComponentClass.Instance, name)
            .SetProperty(VBProperties.LeftProperty, left).SetProperty(VBProperties.TopProperty, top)
            .SetProperty(VBProperties.WidthProperty, width).SetProperty(VBProperties.HeightProperty, height);

    private static ComponentInstanceViewModel Find(FormEditViewModel designer, string name) =>
        designer.AllComponents.Single(c => c.Name == name);

    private static void Drag(FormEditViewModel designer, string target, double move, double resize)
    {
        var controls = target == "Buttons"
            ? new[] { Find(designer, "Command1"), Find(designer, "Command2") }
            : [Find(designer, target)];
        designer.BeginDrag(controls);
        foreach (var control in controls)
        {
            if (move != 0) control.Left += move;
            if (resize != 0) control.Width += resize;
        }
        designer.EndDrag();
    }

    [AvaloniaTheory]
    [InlineData("Line1", "MoveControl", "Line1")]
    [InlineData("Buttons", "MoveControls", 2)]
    public void German_undo_and_redo_use_the_same_key_and_arguments(string target, string key, object argument)
    {
        var german = ReadPack("de");
        using var designer = Open();
        var stack = designer.UndoStack;

        Drag(designer, target, 3, 0);

        var expected = string.Format(new LocalizationService().GetStringFrom("de", KeyPrefix + key), argument);
        expected.Should().NotBe(stack.UndoDescription);
        using var assertions = new AssertionScope();
        stack.GetUndoDisplayName(german).Should().Be(expected);
        stack.Undo();
        stack.GetRedoDisplayName(german).Should().Be(expected);
    }

    [AvaloniaTheory]
    [InlineData(nameof(FormEditViewModel.CenterHorizontally), "CenterHorizontally", 1)]
    [InlineData(nameof(FormEditViewModel.CenterVertically), "CenterVertically", 1)]
    [InlineData(nameof(FormEditViewModel.AlignLefts), "AlignLefts", 3)]
    [InlineData(nameof(FormEditViewModel.AlignRights), "AlignRights", 3)]
    [InlineData(nameof(FormEditViewModel.AlignTops), "AlignTops", 3)]
    [InlineData(nameof(FormEditViewModel.AlignBottoms), "AlignBottoms", 3)]
    [InlineData(nameof(FormEditViewModel.AlignCentersH), "AlignCentersHorizontally", 3)]
    [InlineData(nameof(FormEditViewModel.AlignCentersV), "AlignCentersVertically", 3)]
    [InlineData(nameof(FormEditViewModel.MakeSameWidth), "MakeSameWidth", 3)]
    [InlineData(nameof(FormEditViewModel.MakeSameHeight), "MakeSameHeight", 3)]
    [InlineData(nameof(FormEditViewModel.MakeSameSize), "MakeSameSize", 3)]
    [InlineData(nameof(FormEditViewModel.MakeHorizontalSpacingEqual), "EqualHorizontalSpacing", 3)]
    [InlineData(nameof(FormEditViewModel.IncreaseHorizontalSpacing), "IncreaseHorizontalSpacing", 3)]
    [InlineData(nameof(FormEditViewModel.DecreaseHorizontalSpacing), "DecreaseHorizontalSpacing", 3)]
    [InlineData(nameof(FormEditViewModel.RemoveHorizontalSpacing), "RemoveHorizontalSpacing", 3)]
    [InlineData(nameof(FormEditViewModel.MakeVerticalSpacingEqual), "EqualVerticalSpacing", 3)]
    [InlineData(nameof(FormEditViewModel.IncreaseVerticalSpacing), "IncreaseVerticalSpacing", 3)]
    [InlineData(nameof(FormEditViewModel.DecreaseVerticalSpacing), "DecreaseVerticalSpacing", 3)]
    [InlineData(nameof(FormEditViewModel.RemoveVerticalSpacing), "RemoveVerticalSpacing", 3)]
    [InlineData(nameof(FormEditViewModel.SizeToGrid), "SizeToGrid", 1)]
    [InlineData(nameof(FormEditViewModel.DeleteSelected), "Delete", 1)]
    [InlineData(nameof(FormEditViewModel.DeleteSelected), "DeleteControls", 3)]
    [InlineData(nameof(FormEditViewModel.CutSelectedControls), "Cut", 1)]
    [InlineData(nameof(FormEditViewModel.CutSelectedControls), "CutControls", 3)]
    [InlineData(nameof(FormEditViewModel.PasteControls), "Paste", 1)]
    [InlineData(nameof(FormEditViewModel.PasteControls), "PasteControls", 3)]
    public void English_selection_step_names_match_the_description(string operation, string key, int selectionCount)
    {
        using var designer = Open();
        var target = Find(designer, "Command1");
        designer.SelectedComponent = target;
        designer.SetSelectedComponents(designer.Components.Where(c => !c.IsLine).Take(selectionCount));

        try
        {
            if (operation == nameof(FormEditViewModel.PasteControls))
                designer.CopySelectedControls();
            typeof(FormEditViewModel).GetMethod(operation)!.Invoke(designer, null);
            AssertEnglishName(designer, key);
        }
        finally
        {
            DesignerClipboard.Clear();
        }
    }

    [AvaloniaTheory]
    [InlineData("Add")]
    [InlineData("BringToFront")]
    [InlineData("SendToBack")]
    [InlineData("LockControls")]
    [InlineData("UnlockControls")]
    public void English_control_step_names_match_the_description(string key)
    {
        using var designer = Open();
        switch (key)
        {
            case "Add":
                designer.SpawnControlAt(CommandButtonComponentClass.Instance, new Rect(40, 40, 60, 25));
                break;
            case "BringToFront":
                designer.BringToFront(designer.Components.First(c => !c.IsLine));
                break;
            case "SendToBack":
                designer.SendToBack(designer.Components.Last(c => !c.IsLine));
                break;
            case "LockControls":
                designer.ToggleLockControls();
                break;
            case "UnlockControls":
                designer.ToggleLockControls();
                designer.ToggleLockControls();
                break;
        }
        AssertEnglishName(designer, key);
    }

    [AvaloniaTheory]
    [InlineData("Line1", 3, 0, "MoveControl")]
    [InlineData("Command1", 0, 3, "ResizeControl")]
    [InlineData("Command1", 3, 3, "MoveResizeControl")]
    [InlineData("Form1", 0, 3, "ResizeForm")]
    [InlineData("Buttons", 3, 0, "MoveControls")]
    [InlineData("Buttons", 3, 3, "MoveResizeControls")]
    public void English_drag_step_names_match_the_description(string target, double move, double resize, string key)
    {
        using var designer = Open();
        Drag(designer, target, move, resize);
        AssertEnglishName(designer, key);
    }

    [AvaloniaFact]
    public void English_name_for_empty_geometry_matches_the_description()
    {
        using var designer = Open();
        designer.UndoStack.Push(new MoveResizeCommand(Array.Empty<MoveResizeCommand.Entry>()));
        AssertEnglishName(designer, "Move");
    }

    private static void AssertEnglishName(FormEditViewModel designer, string key)
    {
        var stack = designer.UndoStack;
        stack.CanUndo.Should().BeTrue();
        var english = ReadPack("en");
        stack.GetUndoDisplayName(english).Should().Be(stack.UndoDescription);
        english.Received(1).GetString(KeyPrefix + key);
    }

    [AvaloniaTheory]
    [InlineData(false, "Caption: Command1")]
    [InlineData(true, "(Name): Command1")]
    public void Property_names_are_verbatim_in_German(bool batch, string expected)
    {
        var german = ReadPack("de");
        using var designer = Open();
        var target = Find(designer, "Command1").Instance;

        if (batch)
        {
            designer.CreateControlArray(Find(designer, "Command2").Instance, target, "Command2");
        }
        else
        {
            var before = target.GetBoxedPropertyOrDefault(VBProperties.CaptionProperty);
            target.SetProperty(VBProperties.CaptionProperty, "New caption");
            designer.PushSetPropertyCommand(target, VBProperties.CaptionProperty, before, "New caption");
        }

        var stack = designer.UndoStack;
        stack.GetUndoDisplayName(german).Should().Be(expected).And.Be(stack.UndoDescription);
        german.DidNotReceive().GetString(Arg.Any<string>());
    }

    [AvaloniaFact]
    public async Task Designer_automation_reports_the_English_step_in_a_German_IDE()
    {
        var german = ReadPack("de");
        using var designer = Open(german);
        var dock = Substitute.For<IDocumentDockService>();
        dock.ActiveTab.Returns(designer);
        dock.ActiveDocument.Returns(designer);
        using var context = new IdeContext(Substitute.For<IProjectManager>(), dock, Substitute.For<ILspClient>(),
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            localization: german, settings: null!);
        var tools = new HexIdeTools(context);
        var ct = TestContext.Current.CancellationToken;
        Drag(designer, "Line1", 3, 0);

        (await tools.GetUndoStateAsync(ct)).UndoDescription.Should().Be("Move: Line1");
        (await tools.InvokeDesignerUndoAsync(ct)).Note.Should().Be("Undid 'Move: Line1'. Nothing left to undo; next redo: 'Move: Line1'.");
        (await tools.GetUndoStateAsync(ct)).RedoDescription.Should().Be("Move: Line1");
        (await tools.InvokeDesignerRedoAsync(ct)).Note.Should().Be("Redid 'Move: Line1'. Next undo: 'Move: Line1'; nothing to redo.");
    }
}
