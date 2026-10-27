using System.IO;
using Avalonia;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

public class PictureBoxAutoSizeDesignerTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static (FormEditViewModel Vm, FormDefinition Form, ComponentInstance Box) Drawn()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project,
            "VERSION 5.00\r\nBegin VB.Form Form1\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n", new NullSink())!;
        project.AddForm(form);
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IComponentRegistry>(), Substitute.For<ILocalizationService>());
        var vm = new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        vm.SpawnControlAt(PictureBoxComponentClass.Instance, new Rect(10, 10, 80, 25));
        return (vm, form, vm.SelectedComponent!.Instance);
    }

    // As the Properties window and set_control_property both do it: set the value, then record it.
    private static void Set<T>(FormEditViewModel vm, ComponentInstance box, PropertyClass<T> property, T value)
    {
        var before = box.GetBoxedPropertyOrDefault(property);
        box.SetProperty(property, value);
        vm.PushSetPropertyCommand(box, property, before, value);
    }

    private static byte[] Picture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        const int pixels = 40 * 30 * 3;
        writer.Write((ushort)0x4d42);
        writer.Write(54 + pixels);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(40);
        writer.Write(30);
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write(0);
        writer.Write(pixels);
        writer.Write(new byte[16 + pixels]);
        return stream.ToArray();
    }

    // #922: 40 x 30 pixels becomes 660 x 510 twips bordered, 600 x 450 without a border.
    [AvaloniaTheory]
    [InlineData(VBBorder.FixedSingle, 44, 34)]
    [InlineData(VBBorder.None, 40, 30)]
    public void Picture_assignment_fits_and_undo_restores_picture_and_size(VBBorder border, double width, double height)
    {
        var (vm, _, box) = Drawn();
        Set(vm, box, VBProperties.BorderStyleProperty, border);
        Set(vm, box, VBProperties.AutoSizeProperty, true);
        var picture = Picture();

        Set(vm, box, VBProperties.PictureProperty, picture);

        box.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(width);
        box.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(height);
        vm.UndoStack.Undo();
        box.GetPropertyOrDefault(VBProperties.PictureProperty).Should().BeNull();
        box.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(80);
        box.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(25);
        vm.UndoStack.Redo();
        box.GetPropertyOrDefault(VBProperties.PictureProperty).Should().BeSameAs(picture);
        box.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(width);
        box.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(height);
    }

    [AvaloniaFact]
    public void Explicit_width_holds_until_next_picture_and_clearing_keeps_size()
    {
        var (vm, _, box) = Drawn();
        Set(vm, box, VBProperties.AutoSizeProperty, true);
        Set(vm, box, VBProperties.PictureProperty, Picture());
        Set(vm, box, VBProperties.WidthProperty, 100d);
        box.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(100);
        Set(vm, box, VBProperties.PictureProperty, Picture());
        box.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(44);
        Set<byte[]?>(vm, box, VBProperties.PictureProperty, null);
        box.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(44);
        box.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(34);
    }

    [AvaloniaFact]
    public void Instantiation_keeps_saved_dimensions_and_AutoSize_false_keeps_picture_dimensions()
    {
        var (vm, _, box) = Drawn();
        Set(vm, box, VBProperties.PictureProperty, Picture());
        box.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(80);
        box.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(25);
        box.SetProperty(VBProperties.AutoSizeProperty, true);
        var control = PictureBoxComponentClass.Instance.Instantiate(box);
        box.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(80);
        box.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(25);
        control.Should().NotBeNull();
    }
}
