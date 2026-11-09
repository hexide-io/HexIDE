using System.IO;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using HexIDE.Controls;
using HexIDE.Runtime.Serialization;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;

namespace HexIDE.Integration.Tests.Views;

// Measured: docs/vb6-fidelity-oracle/autosize-on-a-label-and-a-picturebox.md (#922).
public class PictureBoxAutoSizeTests
{
    [AvaloniaTheory]
    [InlineData(VBBorder.FixedSingle, 44, 34)]
    [InlineData(VBBorder.None, 40, 30)]
    public void Designer_picture_assignment_fits_picture_and_border(VBBorder border, double width, double height)
    {
        var instance = new ComponentInstance(PictureBoxComponentClass.Instance, "Picture1");
        instance.SetProperty(VBProperties.AutoSizeProperty, true);
        instance.SetProperty(VBProperties.BorderStyleProperty, border);
        instance.SetProperty(VBProperties.WidthProperty, 3000.0);
        instance.SetProperty(VBProperties.HeightProperty, 1000.0);
        instance.SetProperty(VBProperties.PictureProperty, Bitmap());
        // Instantiation preserves saved extents, even with a picture and AutoSize in the file.
        _ = (VBPictureBox)PictureBoxComponentClass.Instance.Instantiate(instance);
        instance.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(3000);
        instance.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(1000);

        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
        form.UpdateComponents([..form.Components, instance]);
        using var designer = new FormEditViewModel(null!, new EventBus(), null!, null!, null!, null!,
            Substitute.For<ILocalizationService>()).Initialize(form);
        var changes = designer.PushSetPropertyCommand(instance, VBProperties.PictureProperty, null,
            instance.GetPropertyOrDefault(VBProperties.PictureProperty));
        changes.Should().HaveCount(2);
        instance.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(width);
        instance.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(height);
        designer.UndoStack.Undo();
        instance.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(3000);
        instance.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(1000);
        instance.GetPropertyOrDefault(VBProperties.PictureProperty).Should().BeNull();
        designer.CanUndo.Should().BeFalse();
        designer.UndoStack.Redo();
        instance.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(width);
        instance.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(height);
        instance.SetProperty(VBProperties.WidthProperty, 3000.0);
        PictureBoxComponentClass.Instance.ApplyDesignerConsequences(instance, VBProperties.WidthProperty).Should().BeEmpty();
        instance.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(3000);
        instance.SetProperty<byte[]?>(VBProperties.PictureProperty, null);
        PictureBoxComponentClass.Instance.ApplyDesignerConsequences(instance, VBProperties.PictureProperty).Should().BeEmpty();
        instance.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(3000);
        instance.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(height);
        PictureBoxComponentClass.Instance.ApplyDesignerConsequences(instance, VBProperties.AutoSizeProperty).Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(VBBorder.FixedSingle, "660", "510")]
    [InlineData(VBBorder.None, "600", "450")]
    public void Designer_autosize_saves_measured_twips(VBBorder border, string width, string height)
    {
        var instance = new ComponentInstance(PictureBoxComponentClass.Instance, "Picture1");
        instance.SetProperty(VBProperties.AutoSizeProperty, true);
        instance.SetProperty(VBProperties.BorderStyleProperty, border);
        instance.SetProperty(VBProperties.PictureProperty, Bitmap());
        PictureBoxComponentClass.Instance.ApplyDesignerConsequences(instance, VBProperties.PictureProperty);
        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
        form.UpdateComponents([..form.Components, instance]);
        var saved = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        saved.ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim())
            .Should().Contain($"Width           =   {width}")
            .And.Contain($"Height          =   {height}");
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Unmeasured_picture_or_flat_border_refuses_autosizing(bool flatBorder)
    {
        var instance = new ComponentInstance(PictureBoxComponentClass.Instance, "Picture1");
        instance.SetProperty(VBProperties.AutoSizeProperty, true);
        instance.SetProperty(VBProperties.PictureProperty, flatBorder ? Bitmap() : new byte[] { 0, 0, 1, 0 });
        if (flatBorder) instance.SetProperty(VBProperties.AppearanceProperty, VBAppearance.Flat);
        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
        form.UpdateComponents([..form.Components, instance]);
        using var designer = new FormEditViewModel(null!, new EventBus(), null!, null!, null!, null!,
            Substitute.For<ILocalizationService>()).Initialize(form);
        Action resize = () => designer.PushSetPropertyCommand(instance, VBProperties.PictureProperty, null,
            instance.GetPropertyOrDefault(VBProperties.PictureProperty));
        resize.Should().Throw<NotImplementedException>();
        instance.GetPropertyOrDefault(VBProperties.PictureProperty).Should().BeNull();
        designer.CanUndo.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Flat_border_picture_picker_reports_one_not_implemented_refusal()
    {
        var path = Path.Join(Path.GetTempPath(), Guid.NewGuid() + ".bmp");
        await File.WriteAllBytesAsync(path, Bitmap());
        try
        {
            var instance = new ComponentInstance(PictureBoxComponentClass.Instance, "Picture1");
            instance.SetProperty(VBProperties.AutoSizeProperty, true);
            instance.SetProperty(VBProperties.AppearanceProperty, VBAppearance.Flat);
            instance.SetProperty(VBProperties.WidthProperty, 200.0);
            instance.SetProperty(VBProperties.HeightProperty, 100.0);
            var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
            form.UpdateComponents([..form.Components, instance]);
            var bus = new EventBus();
            var loc = Substitute.For<ILocalizationService>();
            loc.GetString("Str.ProjectRunner.NotImplementedYet").Returns("Not implemented: {0}");
            loc.GetString("Str.Tool.Properties.LoadPictureFailed").Returns("Cannot load picture: {0}");
            using var designer = new FormEditViewModel(null!, bus, null!, null!, null!, null!, loc).Initialize(form);
            var dock = Substitute.For<IDocumentDockService>();
            dock.ActiveDocument.Returns(designer);
            var windows = Substitute.For<IWindowManager>();
            windows.OpenFilePickerAsync(Arg.Any<FilePickerOpenOptions>()).Returns(new[] { path });
            var grid = new PropertiesToolViewModel(dock, bus, windows, loc);
            designer.SelectedComponent = designer.AllComponents.Single(c => c.Instance == instance);
            var row = grid.Properties.OfType<PropertyViewModel>().Single(p => p.PropertyClass == VBProperties.PictureProperty);
            await row.LoadPictureCommand.ExecuteAsync(null);
            await windows.Received(1).MessageBox(Arg.Is<string>(s => s.StartsWith("Not implemented:")
                && s.Contains("flat border") && s.Contains("not measured")), icon: MessageBoxIcon.Error);
            windows.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "MessageBox").Should().Be(1);
            row.Value.Should().BeNull();
            instance.GetPropertyOrDefault(VBProperties.PictureProperty).Should().BeNull();
            instance.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(200);
            instance.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(100);
            designer.CanUndo.Should().BeFalse();
        }
        finally { File.Delete(path); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Picture_display_cannot_commit_text_on_focus_loss(bool hasPicture)
    {
        var editor = new TextBox();
        var box = new PropertyBox
        {
            PropertyClass = VBProperties.PictureProperty,
            Object = hasPicture ? Bitmap() : null,
            Template = new FuncControlTemplate<PropertyBox>((_, scope) =>
            {
                var panel = new StackPanel { Name = "PART_Panel" };
                scope.Register(panel.Name, panel);
                return panel;
            }),
            GenericTemplate = new FuncDataTemplate<object>((_, _) => editor),
        };
        editor.Bind(TextBox.TextProperty, new Binding(nameof(PropertyBox.Object))
        {
            Source = box, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
        });
        var next = new Button();
        var window = new Window { Content = new StackPanel { Children = { box, next } } };
        try
        {
            window.Show();
            window.UpdateLayout();
            var before = box.Object;
            editor.IsReadOnly.Should().BeTrue("pictures are assigned by the picker, not as text");
            editor.Focus();
            next.Focus();
            box.Object.Should().BeSameAs(before, "leaving a picture row must not commit its display text");
        }
        finally { window.Close(); }
    }

    private static byte[] Bitmap()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'B'); writer.Write((byte)'M'); writer.Write(3654);
        writer.Write(0); writer.Write(54); writer.Write(40);
        writer.Write(40); writer.Write(30); writer.Write((short)1); writer.Write((short)24);
        writer.Write(new byte[24 + 3600]);
        return stream.ToArray();
    }
}
