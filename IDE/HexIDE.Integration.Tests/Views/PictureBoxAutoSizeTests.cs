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
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Interpreter;
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
            var row = grid.Properties.Single(p => p.PropertyClass == VBProperties.PictureProperty);
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

    [AvaloniaTheory]
    [InlineData(VBBorder.FixedSingle, 660, 510)]
    [InlineData(VBBorder.None, 600, 450)]
    public async Task Runtime_picture_assignment_fits_bitmap_and_keeps_explicit_width(VBBorder border, double width, double height)
    {
        var path = Path.Join(Path.GetTempPath(), Guid.NewGuid() + ".bmp");
        await File.WriteAllBytesAsync(path, Bitmap());
        try
        {
            var (_, canvas, ctx, env) = ContainerRuntimeHarness.Spawn($"""
                VERSION 5.00
                Begin VB.Form Form1
                   Begin VB.PictureBox Picture1
                      AutoSize = -1
                      BorderStyle = {(int)border}
                      Width = 3000
                      Height = 1000
                   End
                End
                Attribute VB_Name = "Form1"
                """);
            var box = (VBPictureBox)ContainerRuntimeHarness.Child(canvas, "Picture1");
            box.Width.Should().Be(200);
            await ContainerRuntimeHarness.Run(ctx, env, $"Set Picture1.Picture = LoadPicture(\"{path}\")\r\n");
            box.Width.Should().Be(width / 15);
            box.Height.Should().Be(height / 15);
            AvaloniaInteroperability.TryGet(box, VBProperties.WidthProperty, out var read).Should().BeTrue();
            read.Value.Should().Be((float)width);
            await ContainerRuntimeHarness.Run(ctx, env, "Picture1.Width = 3000\r\n");
            box.Width.Should().Be(200);
            await ContainerRuntimeHarness.Run(ctx, env, $"Set Picture1.Picture = LoadPicture(\"{path}\")\r\n");
            box.Width.Should().Be(width / 15);
            // The measured no-picture transition must not undo a size explicitly assigned by code.
            await ContainerRuntimeHarness.Run(ctx, env, "Picture1.Width = 3000\r\nSet Picture1.Picture = LoadPicture()\r\n");
            box.Width.Should().Be(200);
            box.Height.Should().Be(height / 15);
            box.Picture.Should().BeNull();
            await ContainerRuntimeHarness.Run(ctx, env, "Picture1.AutoSize = False\r\nPicture1.AutoSize = True\r\n");
            box.Width.Should().Be(200);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Runtime_unmeasured_autosize_refuses_before_replacing_picture(bool flatBorder)
    {
        var instance = new ComponentInstance(PictureBoxComponentClass.Instance, "Picture1");
        instance.SetProperty(VBProperties.AutoSizeProperty, true);
        if (flatBorder) instance.SetProperty(VBProperties.AppearanceProperty, VBAppearance.Flat);
        var box = (VBPictureBox)PictureBoxComponentClass.Instance.Instantiate(instance);
        var old = box.PictureData;
        Action assign = () => box.PictureData = flatBorder ? Bitmap() : new byte[] { 0, 0, 1, 0 };
        assign.Should().Throw<NotImplementedException>();
        box.PictureData.Should().BeSameAs(old);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Runtime_unmeasured_picture_conversions_refuse(int kind)
    {
        var box = (VBPictureBox)PictureBoxComponentClass.Instance.Instantiate(
            new ComponentInstance(PictureBoxComponentClass.Instance, "Picture1"));
        var value = kind switch { 0 => Vb6Value.Nothing, 1 => new Vb6Value(1), _ => new Vb6Value("picture") };
        Action assign = () => AvaloniaInteroperability.TrySet(box, VBProperties.PictureProperty, value);
        assign.Should().Throw<NotImplementedException>();
        box.PictureData.Should().BeNull();
    }

    [AvaloniaFact]
    public void Runtime_enabling_autosize_with_a_picture_refuses_unmeasured_sizing()
    {
        var instance = new ComponentInstance(PictureBoxComponentClass.Instance, "Picture1");
        instance.SetProperty(VBProperties.PictureProperty, Bitmap());
        var box = (VBPictureBox)PictureBoxComponentClass.Instance.Instantiate(instance);
        Action enable = () => AvaloniaInteroperability.TrySet(box, VBProperties.AutoSizeProperty, new Vb6Value(true));
        enable.Should().Throw<NotImplementedException>();
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
