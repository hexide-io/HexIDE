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

    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new InvalidOperationException(message);
    }

    private static (FormEditViewModel designer, PropertiesToolViewModel grid, ComboBox combo, Window window) Open()
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), Form, new Sink())!;
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

    [AvaloniaFact]
    public void Array_elements_and_plain_controls_show_their_names()
    {
        var (designer, _, combo, window) = Open();
        try
        {
            Row(combo, 1).Should().Be("optSize(1)");
            Row(combo, 2).Should().Be("optSize(0)");
            Row(combo, 3).Should().Be("Command1");
        }
        finally { window.Close(); designer.Dispose(); }
    }

    [AvaloniaFact]
    public void Editing_Index_updates_the_rendered_name()
    {
        var (designer, grid, combo, window) = Open();
        try
        {
            combo.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            var index = grid.Properties.OfType<PropertyViewModel>().Single(p => p.PropertyClass == VBProperties.DesignIndexProperty);
            index.Value = "2";
            Dispatcher.UIThread.RunJobs();
            Row(combo, 1).Should().Be("optSize(2)");
        }
        finally { window.Close(); designer.Dispose(); }
    }
}
