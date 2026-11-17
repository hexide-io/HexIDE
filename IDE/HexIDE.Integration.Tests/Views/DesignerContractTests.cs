using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

// #3482: the designer reaches a control class through IDesignerComponentClass and not through ComponentBaseClass. So
// these drive it with a class that is nothing but the interface.
public class DesignerContractTests
{
    internal sealed class InterfaceOnlyClass : IDesignerComponentClass
    {
        public int Drawn { get; private set; }

        public string Name => "Drawn";
        public string VBTypeName => "Test.Drawn";
        public string ClassName => "Drawn";
        public bool IsVisual => true;
        public IReadOnlyList<PropertyClass> Properties { get; } =
        [
            VBProperties.NameProperty, VBProperties.LeftProperty, VBProperties.TopProperty,
            VBProperties.WidthProperty, VBProperties.HeightProperty,
        ];
        public IReadOnlyDictionary<string, PropertyClass> PropertiesByName =>
            Properties.ToDictionary(p => p.Name, p => p, System.StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<EventClass> Events { get; } = [];
        public IReadOnlyList<PropertyChange> ApplyDesignerConsequences(ComponentInstance instance, PropertyClass property) => [];

        public bool IsWindowless => true;
        public (int Width, int Height) AddedSize => (600, 300);
        public Thickness ClientInset(ComponentInstance instance) => new(7, 9, 0, 0);
        public Control InstantiateForDesigner(ComponentInstance instance)
        {
            Drawn++;
            return new Border { Background = Brushes.Red };
        }
    }

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static FormEditViewModel Designer()
    {
        const string source = "VERSION 5.00\r\nBegin VB.Form Form1\r\n   ClientHeight = 4500\r\n   ClientWidth = 6000\r\nEnd\r\n";
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new NullSink())!;
        project.AddForm(form);
        return new FormEditViewModel(null!, new EventBus(), null!, null!, null!, null!, Substitute.For<ILocalizationService>())
            .Initialize(form);
    }

    private static ComponentInstance Placed(IComponentClass type)
    {
        var instance = new ComponentInstance(type, "Drawn1");
        instance.SetProperty(VBProperties.LeftProperty, 150);
        instance.SetProperty(VBProperties.TopProperty, 300);
        instance.SetProperty(VBProperties.WidthProperty, 1500);
        instance.SetProperty(VBProperties.HeightProperty, 450);
        return instance;
    }

    [AvaloniaFact]
    public void The_designer_draws_a_class_through_the_interface()
    {
        var type = new InterfaceOnlyClass();
        var component = new ComponentInstanceViewModel(Designer(), Placed(type));
        var renderer = new ControlRenderer { Width = 100, Height = 30 };
        var window = new Window { Width = 200, Height = 80, Content = renderer };
        window.Show();
        try
        {
            renderer.Component = component;
            Dispatcher.UIThread.RunJobs();

            component.PreviewFailure.Should().BeNull();
            type.Drawn.Should().BeGreaterThan(0);
            renderer.Child.Should().BeOfType<Image>();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_tool_for_a_class_that_is_only_the_interface_can_place_it() =>
        new ComponentToolViewModel(null!, (IComponentClass)new InterfaceOnlyClass(), null!).BaseClass.Should().NotBeNull();

    [AvaloniaFact]
    public void The_designer_measures_contained_controls_from_the_interface_s_inset()
    {
        var component = new ComponentInstanceViewModel(Designer(), Placed(new InterfaceOnlyClass()));

        component.ClientOrigin.Should().Be(new Point(component.Left + 7, component.Top + 9));
    }

    [AvaloniaFact]
    public void The_designer_stacks_a_class_by_the_interface_s_windowless_flag() =>
        FormEditViewModel.IsWindowless(new InterfaceOnlyClass()).Should().BeTrue();
}
