using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Views;

namespace HexIDE.Integration.Tests.Views;

public class PropertiesObjectNamesTests
{
    // VB6's measured names are recorded in #1520; file order here is deliberately reversed.
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.OptionButton optSize
              Index = 1
           End
           Begin VB.OptionButton optSize
              Index = 0
           End
           Begin VB.CommandButton Command1
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // Measured on VB6 SP6, #4541 / oracle PR #4539. TabIndex deliberately differs from file order.
    private const string SortedForm = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.Label zLabel
              TabIndex = 4
           End
           Begin VB.OptionButton optSize
              Index = 10
              TabIndex = 3
           End
           Begin VB.CommandButton Command1
              TabIndex = 5
           End
           Begin VB.OptionButton optSize
              Index = 2
              TabIndex = 0
           End
           Begin VB.CommandButton aButton
              TabIndex = 2
           End
           Begin VB.CommandButton Bravo
              TabIndex = 1
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new InvalidOperationException(message);
    }

    private static (FormEditViewModel designer, PropertiesToolViewModel grid, ComboBox combo, Window window) Open(string source = Form)
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;
        var bus = new EventBus();
        var loc = Substitute.For<ILocalizationService>();
        var designer = new FormEditViewModel(null!, bus, Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), Substitute.For<IWindowManager>(), Substitute.For<ISettingsService>(), loc).Initialize(form);
        var dock = Substitute.For<IDocumentDockService>();
        dock.ActiveDocument.Returns(designer);
        var grid = new PropertiesToolViewModel(dock, bus, Substitute.For<IWindowManager>(), loc);
        var view = new PropertiesToolView { DataContext = grid };
        var window = new Window { Content = view, Width = 400, Height = 600 };
        window.Show();
        var combo = view.FindControl<ComboBox>("ObjectSelector")!;
        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (designer, grid, combo, window);
    }

    private static string? Row(ComboBox combo, int index) => combo.ContainerFromIndex(index)!
        .GetVisualDescendants().OfType<TextBlock>().First().Text;

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Object_rows_follow_the_measured_name_order(bool punctuation)
    {
        var source = SortedForm.ReplaceLineEndings("\n");
        if (punctuation)
            source = source.Replace("End\nAttribute", "   Begin VB.CommandButton aZ\n   End\n   Begin VB.CommandButton a_x\n   End\n   Begin VB.CommandButton A1\n   End\nEnd\nAttribute");
        var (designer, _, combo, window) = Open(source);
        try
        {
            var expected = punctuation
                ? new[] { "a_x", "A1", "aButton", "aZ", "Bravo", "Command1", "Form1", "optSize(10)", "optSize(2)", "zLabel" }
                : new[] { "aButton", "Bravo", "Command1", "Form1", "optSize(10)", "optSize(2)", "zLabel" };
            Enumerable.Range(0, combo.ItemCount).Select(i => Row(combo, i)).Should().Equal(expected);
        }
        finally { window.Close(); designer.Dispose(); }
    }

    [AvaloniaFact]
    public void Renaming_moves_the_row_and_keeps_it_selected()
    {
        var (designer, grid, combo, window) = Open(Form.Replace("Command1", "Bravo"));
        try
        {
            var selected = designer.AllComponents.Single(c => c.Name == "Bravo");
            combo.SelectedItem = selected;
            Dispatcher.UIThread.RunJobs();
            var name = grid.Properties.OfType<PropertyViewModel>().Single(p => p.PropertyClass == VBProperties.NameProperty);
            name.Value = "Zed";
            Dispatcher.UIThread.RunJobs();
            Enumerable.Range(0, combo.ItemCount).Select(i => Row(combo, i)).Should().Equal(
                "Form1", "optSize(0)", "optSize(1)", "Zed");
            combo.SelectedItem.Should().BeSameAs(selected);
            designer.SelectedComponent.Should().BeSameAs(selected);
        }
        finally { window.Close(); designer.Dispose(); }
    }

    [AvaloniaFact]
    public void Array_elements_and_plain_controls_show_their_names()
    {
        var (designer, _, combo, window) = Open();
        try
        {
            Row(combo, 0).Should().Be("Command1");
            Row(combo, 1).Should().Be("Form1");
            Row(combo, 2).Should().Be("optSize(0)");
            Row(combo, 3).Should().Be("optSize(1)");
        }
        finally { window.Close(); designer.Dispose(); }
    }

    [AvaloniaFact]
    public void Editing_Index_updates_the_rendered_name()
    {
        var (designer, grid, combo, window) = Open();
        try
        {
            combo.SelectedItem = designer.AllComponents.Single(c => c.AddressedName == "optSize(0)");
            Dispatcher.UIThread.RunJobs();
            var index = grid.Properties.OfType<PropertyViewModel>().Single(p => p.PropertyClass == VBProperties.DesignIndexProperty);
            index.Value = "2";
            Dispatcher.UIThread.RunJobs();
            Row(combo, 3).Should().Be("optSize(2)");
            combo.SelectedItem.Should().Be(designer.AllComponents.Single(c => c.AddressedName == "optSize(2)"));
        }
        finally { window.Close(); designer.Dispose(); }
    }
}
