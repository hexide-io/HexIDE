using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia.Controls;
using System.Linq;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A UserControl's or PropertyPage's Properties window lists only properties VB6's lists for that kind (#1585). Each VB6
/// list is the Properties window's Alphabetic tab for a new one, read on vb6.exe (the oracle's "What VB6's Properties
/// window lists for a UserControl and a PropertyPage"). HexIDE models only some of them; it must show no property VB6
/// doesn't, as a form's Caption on a UserControl was.
/// </summary>
public class DesignerRootPropertiesTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    // VB6's Properties window, Alphabetic tab, for a new UserControl: 51 rows. Three names were cut at the column's edge
    // in the capture, and are VB6's own: DataBindingBehavior, DataSourceBehavior and InvisibleAtRuntime.
    private static readonly string[] Vb6UserControl =
    [
        "(Name)", "AccessKeys", "Alignable", "Appearance", "AutoRedraw", "BackColor", "BackStyle", "BorderStyle",
        "CanGetFocus", "ClipBehavior", "ClipControls", "ControlContainer", "DataBindingBehavior", "DataSourceBehavior",
        "DefaultCancel", "DrawMode", "DrawStyle", "DrawWidth", "EditAtDesignTime", "Enabled", "FillColor", "FillStyle",
        "Font", "FontTransparent", "ForeColor", "ForwardFocus", "HasDC", "Height", "HitBehavior", "InvisibleAtRuntime",
        "KeyPreview", "MaskColor", "MaskPicture", "MouseIcon", "MousePointer", "OLEDropMode", "Palette", "PaletteMode",
        "Picture", "PropertyPages", "Public", "RightToLeft", "ScaleHeight", "ScaleLeft", "ScaleMode", "ScaleTop",
        "ScaleWidth", "Tag", "ToolboxBitmap", "Width", "Windowless",
    ];

    // The same for a new PropertyPage: 33 rows.
    private static readonly string[] Vb6PropertyPage =
    [
        "(Name)", "Appearance", "AutoRedraw", "BackColor", "Caption", "ClipControls", "DrawMode", "DrawStyle", "DrawWidth",
        "FillColor", "FillStyle", "Font", "FontTransparent", "ForeColor", "HasDC", "Height", "HelpContextID", "KeyPreview",
        "MouseIcon", "MousePointer", "OLEDropMode", "Palette", "PaletteMode", "Picture", "RightToLeft", "ScaleHeight",
        "ScaleLeft", "ScaleMode", "ScaleTop", "ScaleWidth", "StandardSize", "Tag", "Width",
    ];

    private static string File(string kind, string name) =>
        $"VERSION 5.00\r\nBegin VB.{kind} {name} \r\n   ClientHeight    =   3600\r\n   ClientLeft      =   0\r\n"
      + $"   ClientTop       =   0\r\n   ClientWidth     =   4800\r\nEnd\r\nAttribute VB_Name = \"{name}\"\r\n";

    private static (FormDefinition File, FormEditViewModel Designer) Open(string kind, string name, ProjectDefinition? project = null, string? text = null)
    {
        project ??= new ProjectDefinition(VBProjectType.EXE, "P");
        var file = new FormDeserializer().Deserialize(project, text ?? File(kind, name), new NullSink())!;
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        return (file, new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<HexIDE.Projects.IProjectService>(),
            null!, null!, Substitute.For<ISettingsService>(), localization).Initialize(file));
    }

    private static string[] Listed(FormEditViewModel designer) =>
        [.. PropertyText.InWindowOrder(designer.Form.Instance.BaseClass).Select(p => p.Name)];

    [AvaloniaFact]
    public void A_UserControl_lists_only_properties_VB6s_UserControl_has()
    {
        var (_, designer) = Open("UserControl", "UserControl1");

        Listed(designer).Should().Equal("(Name)", "BackColor", "Enabled", "Font", "ForeColor", "Height", "KeyPreview", "Tag", "Width");
        Listed(designer).Should().BeSubsetOf(Vb6UserControl);
    }

    // #1631, issuecomment-6426450412: HelpContextID belongs in the PropertyPage Properties window.
    [AvaloniaFact]
    public void A_PropertyPage_lists_only_properties_VB6s_PropertyPage_has()
    {
        var (_, designer) = Open("PropertyPage", "PropertyPage1");

        Listed(designer).Should().Equal("(Name)", "BackColor", "Caption", "Font", "ForeColor", "Height", "HelpContextID", "KeyPreview", "Tag", "Width");
        Listed(designer).Should().BeSubsetOf(Vb6PropertyPage);
    }

    // #1970 and #1596: the form lists its measured MousePointer and BorderStyle properties.
    [AvaloniaFact]
    public void A_form_keeps_its_list()
    {
        var (_, designer) = Open("Form", "Form1");

        Listed(designer).Should().Equal("(Name)", "BackColor", "BorderStyle", "Caption", "Enabled", "Font", "ForeColor", "Height", "Icon",
            "KeyPreview", "Left", "MaxButton", "MinButton", "MousePointer", "ShowInTaskbar", "StartUpPosition", "Tag", "Top", "Visible", "Width");
    }

    // The designer finds the root of each kind, and its class is the kind the file names.
    [AvaloniaTheory]
    [InlineData("Form", "Form1")]
    [InlineData("UserControl", "UserControl1")]
    [InlineData("PropertyPage", "PropertyPage1")]
    public void The_root_is_found_and_its_class_is_the_files_kind(string kind, string name)
    {
        var (file, designer) = Open(kind, name);

        designer.Form.Should().NotBeNull();
        designer.Form.Name.Should().Be(name);
        file.Name.Should().Be(name);
        designer.Form.Instance.BaseClass.VBTypeName.Should().Be(file.RootVBTypeName).And.Be($"VB.{kind}");
        designer.Form.Instance.BaseClass.Should().BeAssignableTo<FormComponentClass>();
        VBLoader.FormComponentOf(file).Should().BeSameAs(designer.Form.Instance);
    }

    // Renaming the root renames the document, so it takes the project's rule: a UserControl can't take a module's name.
    [AvaloniaFact]
    public void A_UserControls_root_cannot_take_a_modules_name()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddModule(new ModuleDefinition(project, "Module1", ModuleKind.StandardModule));
        var (_, designer) = Open("UserControl", "UserControl1", project);

        var rename = () => designer.Form.Instance.SetUntypedProperty(VBProperties.NameProperty, "Module1");

        rename.Should().Throw<Avalonia.Data.DataValidationException>();
        designer.Form.Name.Should().Be("UserControl1");
    }

    // Designer measurements: docs/vb6-fidelity-oracle/
    // a-picture-in-the-properties-window-and-the-save-and-the-designers-caption-icon.md (#2716).
    private static byte[] IconBytes(string asset)
    {
        using var stream = AssetLoader.Open(new Uri(asset));
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static byte[] Png(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private static void Caption(FormEditViewModel designer, Action<Panel> check)
    {
        var view = new FormEditView { DataContext = designer };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var panel = (Panel)view.FindControl<Border>("PART_FormTitleBar")!.Parent!;
            check(panel);
        }
        finally { window.Close(); }
    }

    private static Image[] CaptionImages(Panel caption) =>
        caption.GetVisualDescendants().OfType<Image>()
            .Where(i => i.IsEffectivelyVisible).ToArray();

    private static Avalonia.Controls.Shapes.Path[] CaptionGlyphs(Panel caption) =>
        caption.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
            .Where(p => p.IsEffectivelyVisible && ReferenceEquals(p.Data, caption.FindResource("Geo.Form")))
            .ToArray();

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_caption_draws_the_forms_own_icon(int border)
    {
        var (_, designer) = Open("Form", "Form1");
        using (designer)
        {
            designer.Form.Instance.UnknownRawProperties.Add(new ComponentInstance.UnknownRawProperty("BorderStyle", [$"BorderStyle = {border}"]));
            var bytes = IconBytes("avares://HexIDE/Icons/AppIcon/hexide.ico");
            designer.Form.Instance.SetProperty(VBProperties.IconProperty, bytes);
            Caption(designer, caption =>
            {
                var images = CaptionImages(caption);
                images.Should().ContainSingle();
                images[0].Width.Should().Be(16);
                images[0].Height.Should().Be(16);
                using var expected = new Bitmap(new MemoryStream(bytes));
                Png((Bitmap)images[0].Source!).Should().Equal(Png(expected));
                CaptionGlyphs(caption).Should().BeEmpty();
            });
        }
    }

    [AvaloniaFact]
    public void No_Icon_line_draws_the_shared_neutral_form_icon()
    {
        var (_, designer) = Open("Form", "Form1");
        using (designer)
        Caption(designer, caption =>
        {
            var images = CaptionImages(caption);
            images.Should().ContainSingle();
            using var expected = new Bitmap(new MemoryStream(IconBytes("avares://HexIDE.Runtime/form.ico")));
            Png((Bitmap)images[0].Source!).Should().Equal(Png(expected));
            CaptionGlyphs(caption).Should().BeEmpty();
        });
    }

    [AvaloniaTheory]
    [InlineData(4, -1)]
    [InlineData(5, -1)]
    [InlineData(2, 0)]
    public void Tool_windows_and_ControlBox_False_draw_no_caption_icon(int border, int controlBox)
    {
        var (_, designer) = Open("Form", "Form1");
        using (designer)
        {
            designer.Form.Instance.UnknownRawProperties.Add(new ComponentInstance.UnknownRawProperty("BorderStyle", [$"BorderStyle = {border}"]));
            designer.Form.Instance.UnknownRawProperties.Add(new ComponentInstance.UnknownRawProperty("ControlBox", [$"ControlBox = {controlBox}"]));
            designer.Form.Instance.SetProperty(VBProperties.IconProperty, IconBytes("avares://HexIDE/Icons/AppIcon/hexide.ico"));
            Caption(designer, caption =>
            {
                CaptionImages(caption).Should().BeEmpty();
                CaptionGlyphs(caption).Should().BeEmpty();
            });
        }
    }

    [AvaloniaTheory]
    [InlineData(2)]
    [InlineData(3)]
    public void None_keeps_the_form_glyph_as_the_generic_window_standin(int border)
    {
        var (_, designer) = Open("Form", "Form1");
        using (designer)
        {
            designer.Form.Instance.UnknownRawProperties.Add(new ComponentInstance.UnknownRawProperty("BorderStyle", [$"BorderStyle = {border}"]));
            designer.Form.Instance.SetProperty(VBProperties.IconProperty, new byte[] { 0x6c, 0x74, 0, 0, 0, 0, 0, 0 });
            Caption(designer, caption =>
            {
                CaptionImages(caption).Should().BeEmpty();
                CaptionGlyphs(caption).Should().ContainSingle();
            });
        }
    }

    [AvaloniaFact]
    public void A_loaded_tool_window_draws_no_caption_icon()
    {
        var text = File("Form", "Form1").Replace("End\r\n", "   BorderStyle = 4 'Fixed ToolWindow\r\nEnd\r\n");
        var (_, designer) = Open("Form", "Form1", text: text);
        using (designer)
        {
            designer.Form.Instance.UnknownRawProperties.Should().NotContain(p => p.Name == "BorderStyle");
            Caption(designer, caption =>
            {
                CaptionImages(caption).Should().BeEmpty();
                CaptionGlyphs(caption).Should().BeEmpty();
            });
        }
    }

    [AvaloniaFact]
    public void BorderStyle_row_updates_the_caption_through_undo_and_redo()
    {
        var bus = new EventBus();
        var loc = Substitute.For<ILocalizationService>();
        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
        using var designer = new FormEditViewModel(null!, bus, null!, null!, null!, Substitute.For<ISettingsService>(), loc).Initialize(form);
        var dock = Substitute.For<IDocumentDockService>();
        dock.ActiveDocument.Returns(designer);
        var grid = new PropertiesToolViewModel(dock, bus, Substitute.For<IWindowManager>(), loc);
        designer.SelectedComponent = designer.Form;
        var row = grid.Properties.OfType<PropertyViewModel>().Single(p => p.PropertyClass == VBProperties.FormBorderStyleProperty);
        Caption(designer, caption =>
        {
            CaptionImages(caption).Should().ContainSingle();
            var original = Png((Bitmap)CaptionImages(caption).Single().Source!);
            row.Value = Enum.ToObject(row.PropertyClass.PropertyType, 4);
            CaptionImages(caption).Should().BeEmpty();
            CaptionGlyphs(caption).Should().BeEmpty();
            designer.UndoStack.Undo();
            Png((Bitmap)CaptionImages(caption).Single().Source!).Should().Equal(original);
            designer.UndoStack.Redo();
            CaptionImages(caption).Should().BeEmpty();
            CaptionGlyphs(caption).Should().BeEmpty();
        });
    }

    [AvaloniaFact]
    public async Task Loading_the_Icon_row_updates_the_caption_and_undo_restores_it()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".ico");
        System.IO.File.WriteAllBytes(path, IconBytes("avares://HexIDE/Icons/AppIcon/hexide.ico"));
        try
        {
            var bus = new EventBus();
            var loc = Substitute.For<ILocalizationService>();
            var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
            using var designer = new FormEditViewModel(null!, bus, null!, null!, null!, Substitute.For<ISettingsService>(), loc).Initialize(form);
            var dock = Substitute.For<IDocumentDockService>();
            dock.ActiveDocument.Returns(designer);
            var windows = Substitute.For<IWindowManager>();
            windows.OpenFilePickerAsync(Arg.Any<FilePickerOpenOptions>()).Returns(new[] { path });
            var grid = new PropertiesToolViewModel(dock, bus, windows, loc);
            designer.SelectedComponent = designer.Form;
            var row = grid.Properties.OfType<PropertyViewModel>().Single(p => p.PropertyClass == VBProperties.IconProperty);
            var view = new FormEditView { DataContext = designer };
            var window = new Window { Content = view, Width = 800, Height = 600 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var caption = (Panel)view.FindControl<Border>("PART_FormTitleBar")!.Parent!;
                var before = CaptionImages(caption);
                before.Should().ContainSingle();
                var original = Png((Bitmap)before[0].Source!);
                await row.LoadPictureCommand.ExecuteAsync(null);
                window.UpdateLayout();
                using var expected = new Bitmap(path);
                Png((Bitmap)CaptionImages(caption).Single().Source!).Should().Equal(Png(expected)).And.NotEqual(original);
                designer.UndoStack.Undo();
                window.UpdateLayout();
                Png((Bitmap)CaptionImages(caption).Single().Source!).Should().Equal(original);
                designer.UndoStack.Redo();
                window.UpdateLayout();
                Png((Bitmap)CaptionImages(caption).Single().Source!).Should().Equal(Png(expected));
            }
            finally { window.Close(); }
        }
        finally { System.IO.File.Delete(path); }
    }

}
