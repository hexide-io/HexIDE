using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

// VB6 SP6 designer measurements for #1596: https://github.com/hexide-io/HexIDE/pull/5216.
public class FormBorderStyleTests
{
    private static PropertyClass Border(ComponentInstance form)
    {
        var property = form.BaseClass.Properties.SingleOrDefault(p => p.Name == "BorderStyle");
        property.Should().NotBeNull("a form offers BorderStyle even without a saved line");
        return property!;
    }

    private static FormEditViewModel Designer() => new FormEditViewModel(null!, new EventBus(), null!, null!, null!, null!,
        Substitute.For<ILocalizationService>()).Initialize(
        new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1"));

    private static void Set(FormEditViewModel designer, int style)
    {
        var form = designer.Form.Instance;
        var property = Border(form);
        var before = form.GetBoxedPropertyOrDefault(property);
        var after = Enum.ToObject(property.PropertyType, style);
        form.SetUntypedProperty(property, after);
        designer.PushSetPropertyCommand(form, property, before, after);
    }

    private static void Values(ComponentInstance form, bool min, bool max, bool taskbar)
    {
        form.GetPropertyOrDefault(VBProperties.MinButtonProperty).Should().Be(min);
        form.GetPropertyOrDefault(VBProperties.MaxButtonProperty).Should().Be(max);
        form.GetPropertyOrDefault(VBProperties.ShowInTaskbarProperty).Should().Be(taskbar);
    }

    private static void Saved(FormEditViewModel designer, int style, bool min, bool max, bool taskbar)
    {
        var saved = new FormSerializer().Serialize(designer.FormDefinition!, "Form1.frm").frmText;
        var lines = saved.ReplaceLineEndings("\n").Split('\n');
        foreach (var (name, value) in new[] { ("MinButton", min), ("MaxButton", max), ("ShowInTaskbar", taskbar) })
        {
            var line = lines.Where(l => l.TrimStart().StartsWith(name + " ")).ToArray();
            if (value) line.Should().BeEmpty(name + " is omitted when True");
            else line.Should().Equal("   " + name.PadRight(16) + "=   0   'False");
        }
        if (style == 2) saved.Should().NotContain("BorderStyle");
        else lines.Should().Contain(l => l.StartsWith("   BorderStyle     =   " + style + "  '"));
    }

    [AvaloniaTheory]
    [InlineData(0, true, true, false)]
    [InlineData(1, false, false, true)]
    [InlineData(3, false, false, false)]
    [InlineData(4, false, false, false)]
    [InlineData(5, false, false, false)]
    public void Sizable_to_each_style_and_back_is_one_undoable_change(int style, bool min, bool max, bool taskbar)
    {
        using var designer = Designer();
        var form = designer.Form.Instance;
        form.UnknownRawProperties.Add(new("ControlBox", ["   ControlBox      =   0   'False"]));
        Set(designer, style);
        Values(form, min, max, taskbar);
        Saved(designer, style, min, max, taskbar);
        form.UnknownRawPropertyLines.Should().Contain("   ControlBox      =   0   'False");
        designer.UndoStack.Undo();
        Convert.ToInt32(form.GetBoxedPropertyOrDefault(Border(form))).Should().Be(2);
        Values(form, true, true, true);
        Saved(designer, 2, true, true, true);
        designer.CanUndo.Should().BeFalse();
        designer.UndoStack.Redo();
        Values(form, min, max, taskbar);
        Saved(designer, style, min, max, taskbar);
        Set(designer, 2);
        Values(form, true, true, true);
        Saved(designer, 2, true, true, true);
        designer.UndoStack.Undo();
        Convert.ToInt32(form.GetBoxedPropertyOrDefault(Border(form))).Should().Be(style);
        Values(form, min, max, taskbar);
        Saved(designer, style, min, max, taskbar);
    }

    [AvaloniaFact]
    public void Hand_set_values_are_replaced_on_a_different_style()
    {
        using var designer = Designer();
        var form = designer.Form.Instance;
        form.SetProperty(VBProperties.MinButtonProperty, false);
        form.SetProperty(VBProperties.ShowInTaskbarProperty, false);
        form.UnknownRawProperties.Add(new("ControlBox", ["   ControlBox      =   0   'False"]));
        Set(designer, 1);
        Values(form, false, false, true);
        Saved(designer, 1, false, false, true);
        Set(designer, 2);
        Values(form, true, true, true);
        Saved(designer, 2, true, true, true);
        form.UnknownRawPropertyLines.Should().Contain("   ControlBox      =   0   'False");
        Set(designer, 3);
        form.SetProperty(VBProperties.MaxButtonProperty, true);
        Saved(designer, 3, false, true, false);
        Set(designer, 1);
        Values(form, false, false, true);
        Saved(designer, 1, false, false, true);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void None_keeps_hand_set_buttons(bool max)
    {
        using var designer = Designer();
        if (!max) Set(designer, 3);
        var form = designer.Form.Instance;
        form.SetProperty(VBProperties.MinButtonProperty, false);
        Set(designer, 0);
        Values(form, false, max, false);
        Saved(designer, 0, false, max, false);
    }

    [AvaloniaTheory]
    [InlineData(2)]
    [InlineData(1)]
    public void Same_style_keeps_hand_set_values_and_adds_no_undo(int style)
    {
        using var designer = Designer();
        if (style != 2) Set(designer, style);
        var form = designer.Form.Instance;
        form.SetProperty(VBProperties.MinButtonProperty, false);
        form.SetProperty(VBProperties.ShowInTaskbarProperty, false);
        Set(designer, style);
        Values(form, false, style == 2, false);
        Saved(designer, style, false, style == 2, false);
        if (style == 2) designer.CanUndo.Should().BeFalse();
        else
        {
            designer.UndoStack.Undo();
            designer.CanUndo.Should().BeFalse();
        }
    }

    [AvaloniaFact]
    public void Properties_window_offers_border_style_without_a_saved_line()
    {
        using var designer = Designer();
        var dock = Substitute.For<IDocumentDockService>();
        dock.ActiveDocument.Returns(designer);
        var grid = new PropertiesToolViewModel(dock, new EventBus(), Substitute.For<IWindowManager>(),
            Substitute.For<ILocalizationService>());
        var row = grid.Properties.OfType<PropertyViewModel>().SingleOrDefault(p => p.PropertyClass.Name == "BorderStyle");
        row.Should().NotBeNull();
        Convert.ToInt32(row!.Value).Should().Be(2);
        row.Value = Enum.ToObject(row.PropertyClass.PropertyType, 3);
        Values(designer.Form.Instance, false, false, false);
        foreach (var property in new[] { VBProperties.MinButtonProperty, VBProperties.MaxButtonProperty, VBProperties.ShowInTaskbarProperty })
            grid.Properties.OfType<PropertyViewModel>().Single(p => p.PropertyClass == property).Value.Should().Be(false);
        designer.UndoStack.Undo();
        Convert.ToInt32(row.Value).Should().Be(2);
        Values(designer.Form.Instance, true, true, true);
        designer.CanUndo.Should().BeFalse();
    }
}
