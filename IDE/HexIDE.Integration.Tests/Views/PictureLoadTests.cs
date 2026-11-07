using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using HexIDE.VisualDesigner.Views;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

// #2653's supervisor decision; record framing is measured in
// docs/vb6-fidelity-oracle/serialization--file-format-fidelity.md.
public class PictureLoadTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new InvalidOperationException(message);
    }

    [AvaloniaFact]
    public void Both_property_tabs_offer_load_buttons_only_on_picture_rows()
    {
        var grid = new PropertiesToolViewModel(Substitute.For<IDocumentDockService>(), new EventBus(),
            Substitute.For<IWindowManager>(), Substitute.For<ILocalizationService>());
        foreach (var property in new PropertyClass[] { VBProperties.PictureProperty, VBProperties.IconProperty,
                     VBProperties.DragIconProperty, VBProperties.MouseIconProperty, VBProperties.CaptionProperty })
        {
            var row = new PropertyViewModel(grid, property, null);
            grid.Properties.Add(row);
            grid.CategorizedProperties.Add(row);
        }
        var view = new PropertiesToolView { DataContext = grid };
        var window = new Window { Content = view, Width = 400, Height = 400 };
        try
        {
            window.Show();
            foreach (var tab in new[] { "AlphabeticPropertiesTab", "CategorizedPropertiesTab" })
            {
                view.FindControl<TabItem>(tab)!.IsSelected = true;
                window.UpdateLayout();
                foreach (var row in grid.Properties)
                {
                    var buttons = view.GetVisualDescendants().OfType<Button>()
                        .Where(b => b.IsEffectivelyVisible && ReferenceEquals(b.DataContext, row)
                            && ReferenceEquals(b.Command, row.LoadPictureCommand)).ToArray();
                    buttons.Should().HaveCount(row.CanLoadPicture ? 1 : 0);
                }
            }
        }
        finally { window.Close(); }
    }

    private static byte[] Bitmap()
    {
        var bytes = new byte[58];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M'; bytes[2] = 58; bytes[10] = 54;
        bytes[14] = 40; bytes[18] = 1; bytes[22] = 1; bytes[26] = 1; bytes[28] = 24;
        return bytes;
    }

    [AvaloniaTheory]
    [InlineData("Picture")]
    [InlineData("Icon")]
    [InlineData("DragIcon")]
    [InlineData("MouseIcon")]
    public async Task Load_replaces_one_picture_as_one_undo_step_and_round_trips(string name)
    {
        await Exercise(name, "load");
    }

    [AvaloniaFact]
    public async Task A_framed_picture_the_reader_accepts_is_not_wrapped_twice()
    {
        await Exercise("Picture", "framed");
    }

    [AvaloniaTheory]
    [InlineData("cancel")]
    [InlineData("invalid")]
    [InlineData("missing")]
    public async Task Cancel_or_unreadable_file_keeps_the_old_picture_and_undo_stack(string outcome)
    {
        await Exercise("Picture", outcome);
    }

    private static async Task Exercise(string name, string outcome)
    {
        var path = Path.Join(Path.GetTempPath(), Guid.NewGuid() + ".bmp");
        var payload = Bitmap();
        var input = payload;
        if (outcome == "framed")
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(payload.Length + 8);
            writer.Write(0x746c);
            writer.Write(payload.Length);
            writer.Write(payload);
            input = stream.ToArray();
        }
        if (outcome != "missing") await File.WriteAllBytesAsync(path, outcome == "invalid" ? [1, 2, 3] : input);
        try
        {
            var project = new ProjectDefinition(VBProjectType.EXE, "P");
            var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
            var instance = name == "Icon" ? form.Components[0]
                : new ComponentInstance(ImageComponentClass.Instance, "Image1");
            if (name != "Icon") form.UpdateComponents([..form.Components, instance]);
            var property = instance.BaseClass.Properties.Single(p => p.Name == name);
            byte[] old = [42];
            instance.SetUntypedProperty(property, old);
            var bus = new EventBus();
            var loc = Substitute.For<ILocalizationService>();
            loc.GetString("Str.Tool.Properties.LoadPictureFailed").Returns("Cannot load picture '{0}'.");
            using var designer = new FormEditViewModel(null!, bus, null!, null!, null!, null!, loc).Initialize(form);
            var dock = Substitute.For<IDocumentDockService>();
            dock.ActiveDocument.Returns(designer);
            var windows = Substitute.For<IWindowManager>();
            windows.OpenFilePickerAsync(Arg.Any<FilePickerOpenOptions>())
                .Returns(outcome == "cancel" ? null : new[] { path });
            var grid = new PropertiesToolViewModel(dock, bus, windows, loc);
            designer.SelectedComponent = designer.AllComponents.Single(c => c.Instance == instance);
            var row = grid.Properties.Single(p => p.PropertyClass == property);
            row.CanLoadPicture.Should().BeTrue();
            await row.LoadPictureCommand.ExecuteAsync(null);
            await windows.Received(1).OpenFilePickerAsync(Arg.Is<FilePickerOpenOptions>(o => !o.AllowMultiple));
            if (outcome is not ("load" or "framed"))
            {
                instance.GetBoxedPropertyOrDefault(property).Should().BeSameAs(old);
                row.Value.Should().BeSameAs(old);
                designer.CanUndo.Should().BeFalse();
                if (outcome != "cancel")
                    await windows.Received(1).MessageBox(Arg.Is<string>(s => s.Contains(path)), icon: MessageBoxIcon.Error);
                else
                    await windows.DidNotReceiveWithAnyArgs().MessageBox(default!);
                return;
            }
            var record = (byte[])instance.GetBoxedPropertyOrDefault(property)!;
            FrxImageHelper.PicturePayload(record).Should().Equal(payload);
            row.Value.Should().BeSameAs(record);
            designer.CanUndo.Should().BeTrue();
            designer.UndoStack.Undo();
            instance.GetBoxedPropertyOrDefault(property).Should().BeSameAs(old);
            row.Value.Should().BeSameAs(old);
            designer.CanUndo.Should().BeFalse();
            designer.UndoStack.Redo();
            form.AbsolutePath = Path.ChangeExtension(path, ".frm");
            var service = new ProjectService(null!, windows, bus, Substitute.For<IProjectManager>(),
                Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(),
                Substitute.For<IUserSidecarService>(), new FileBaselineStore(), loc, Substitute.For<IHeaderRefresher>());
            (await service.SaveForm(form, saveAs: false, interactive: false)).Should().BeTrue();
            var source = await File.ReadAllTextAsync(form.AbsolutePath);
            var companion = await File.ReadAllBytesAsync(Path.ChangeExtension(path, ".frx"));
            companion.Should().Equal(record);
            var reopened = new FormDeserializer().Deserialize(project, source, new Sink(),
                FrxDeserializer.Read(companion!, source))!;
            var reopenedInstance = reopened.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty)
                == instance.GetPropertyOrDefault(VBProperties.NameProperty));
            ((byte[])reopenedInstance.GetBoxedPropertyOrDefault(property)!).Should().Equal(record);
        }
        finally
        {
            File.Delete(path);
            File.Delete(Path.ChangeExtension(path, ".frm"));
            File.Delete(Path.ChangeExtension(path, ".frx"));
        }
    }
}
