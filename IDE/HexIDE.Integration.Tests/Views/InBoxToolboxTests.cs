using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The toolbox offers an in-box control while its project references the control's library, and placing one declares the
/// library in its form's header (#1103). VB6's toolbox follows the Components dialog in the same way, and its designer
/// writes the form's <c>Object =</c> line once a control of the library is on the form; a form holding none of a
/// referenced library's controls has no line for it (measured on vb6.exe).
/// </summary>
public class InBoxToolboxTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private const string Comctl32Form = "Object = \"{6B7E6392-850A-101B-AFC0-4210102A8DA7}#1.3#0\"; \"COMCTL32.OCX\"";
    private const string MsComctlForm = "Object = \"{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX\"";

    // #1083 issuecomment-6427015606: 38 px, centred in pixels without grid snapping.
    [AvaloniaFact]
    public void ImageList_is_offered_and_double_clicked_at_its_measured_size_and_position()
    {
        var (_, toolbox, vm, form) = Designer(ControlLibraries.MSComctlLib);
        var tool = toolbox.Components.Single(c => c.VBTypeName == "MSComctlLib.ImageList");
        vm.Form.Width = 400;
        vm.Form.Height = 300;
        vm.SpawnControlCenter((ComponentBaseClass)tool.BaseClass!, ControlLibraries.MSComctlLib);
        vm.CommitLayout();
        var output = new FormSerializer().Serialize(form, "Form1.frm").frmText.ReplaceLineEndings();
        output.Should().Contain("Left            =   2715").And.Contain("Top             =   1965")
            .And.Contain("_ExtentX        =   1005").And.Contain("_ExtentY        =   1005");
        var imageList = vm.Components.Single(c => c.Instance.BaseClass.VBTypeName == "MSComctlLib.ImageList");
        imageList.Width.Should().Be(38);
        imageList.Height.Should().Be(38);
    }

    [AvaloniaFact]
    public void UpDown_toolbox_default_has_the_measured_size_and_no_own_properties()
    {
        var (_, toolbox, vm, form) = Designer(ControlLibraries.MSComCtl2);
        var tool = toolbox.Components.Single(c => c.VBTypeName == "MSComCtl2.UpDown");
        vm.SpawnControlCenter((ComponentBaseClass)tool.BaseClass!, ControlLibraries.MSComCtl2);
        vm.CommitLayout();
        var output = new FormSerializer().Serialize(form, "Form1.frm").frmText.ReplaceLineEndings();
        output.Should().Contain("Height          =   750").And.Contain("Width           =   255")
            .And.Contain("_ExtentX        =   450").And.Contain("_ExtentY        =   1323")
            .And.Contain("_Version        =   393216" + Environment.NewLine + "   End");
    }

    private static (ProjectDefinition project, ToolBoxToolViewModel toolbox, FormEditViewModel vm, FormDefinition form) Designer(
        params RuntimeLibrary[] referenced)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        foreach (var library in referenced)
            project.UnknownPreSectionLines.Add((1, library.ProjectLine));
        var form = new FormDeserializer().Deserialize(project,
            "VERSION 5.00\r\nBegin VB.Form Form1\r\n   Caption = \"Form1\"\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n", new NullSink())!;
        project.AddForm(form);
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IComponentRegistry>(), Substitute.For<ILocalizationService>());
        var settings = Substitute.For<ISettingsService>();
        settings.AlignToGrid.Returns(true);
        settings.GridWidth.Returns(8);
        settings.GridHeight.Returns(8);
        var vm = new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            settings, Substitute.For<ILocalizationService>()).Initialize(form);
        toolbox.ShowProject(project);
        return (project, toolbox, vm, form);
    }

    private static RuntimeLibrary?[] InBoxLibraries(ToolBoxToolViewModel toolbox) =>
        toolbox.Components.Where(c => c.BaseClass is InBoxComponentClass).Select(c => c.Library).ToArray();

    [AvaloniaFact]
    public void RichTextBox_toolbox_placement_seeds_its_name_and_declares_its_library()
    {
        var library = ControlLibraries.All.Single(l => l.Name == "RichTextLib");
        var (_, toolbox, vm, form) = Designer(library);
        toolbox.Components.Single(c => c.BaseClass is RichTextBoxComponentClass).Library.Should().BeSameAs(library);
        vm.SpawnControlCenter(RichTextBoxComponentClass.Instance, library);
        vm.CommitLayout();
        form.HeaderLines.Should().Equal("Object = \"{3B7C8863-D78F-101B-B9B5-04021C009402}#1.2#0\"; \"RICHTX32.OCX\"");
        var box = form.Components.Single(c => c.BaseClass is RichTextBoxComponentClass);
        box.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(100);
        box.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(50);
        box.BaseClass.Properties.Should().NotContain(p => p.Name == "TextRTF" || p.Name == "Font" || p.Name == "MultiLine" || p.Name == "Locked",
            "saved own properties are carried until their designer editing is faithful");
        var (text, bytes) = new FormSerializer().Serialize(form, "Form1.frm");
        text.Should().Contain("Begin RichTextLib.RichTextBox RichTextBox1").And.NotContain("Text            =");
        System.Text.Encoding.ASCII.GetString(bytes!, 4, bytes!.Length - 4).Should().Be(
            "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0\\fnil\\fcharset0 MS Sans Serif;}}\r\n\\viewkind4\\uc1\\pard\\lang1033\\f0\\fs17 RichTextBox1\r\n\\par }\r\n");
    }

    [AvaloniaFact]
    public void A_project_that_references_no_library_offers_no_in_box_control()
    {
        var (_, toolbox, _, _) = Designer();

        InBoxLibraries(toolbox).Should().BeEmpty();
    }

    // Each referenced library offers its implemented controls after the intrinsic controls.
    [AvaloniaFact]
    public void Each_referenced_library_offers_its_ProgressBar_after_the_intrinsic_controls()
    {
        var (_, toolbox, _, _) = Designer(ControlLibraries.MSComctlLib, ControlLibraries.ComctlLib);

        InBoxLibraries(toolbox).Should().Equal(ControlLibraries.MSComctlLib, ControlLibraries.MSComctlLib,
            ControlLibraries.MSComctlLib, ControlLibraries.ComctlLib);
        var shape = toolbox.Components.ToList().FindIndex(c => c.BaseClass is ShapeComponentClass);
        toolbox.Components[shape + 1].BaseClass.Should().BeSameAs(ProgressBarComponentClass.Instance);
        toolbox.Components[shape + 1].Name.Should().Be("ProgressBar");
        toolbox.Components.Where(c => c.Library is not null).Select(c => c.VBTypeName)
            .Should().Equal("MSComctlLib.ProgressBar", "MSComctlLib.StatusBar", "MSComctlLib.ImageList", "ComctlLib.ProgressBar");
    }

    // The Components dialog is how a project comes to reference a library, and the toolbox follows it both ways.
    [AvaloniaFact]
    public void The_toolbox_follows_the_Components_dialog()
    {
        var (project, toolbox, _, _) = Designer();
        var dialog = new ComponentsViewModel(project);

        dialog.Controls.Single(c => c.Library == ControlLibraries.MSComctlLib).IsSelected = true;
        dialog.ApplyCommand.Execute(null);
        InBoxLibraries(toolbox).Should().Equal(ControlLibraries.MSComctlLib, ControlLibraries.MSComctlLib, ControlLibraries.MSComctlLib);

        toolbox.SelectedComponent = toolbox.Components.First(c => c.Library is not null);
        dialog.Controls.Single(c => c.Library == ControlLibraries.MSComctlLib).IsSelected = false;
        dialog.ApplyCommand.Execute(null);
        InBoxLibraries(toolbox).Should().BeEmpty();
        toolbox.SelectedComponent.Should().BeSameAs(toolbox.Arrow, "the tool it had selected is gone");
    }

    // Measured: a form holding none of a referenced library's controls has no line for it.
    [AvaloniaFact]
    public void A_form_declares_a_library_only_once_one_of_its_controls_is_placed()
    {
        var (_, _, vm, form) = Designer(ControlLibraries.MSComctlLib);

        form.HeaderLines.Should().BeEmpty();

        vm.SpawnControlCenter(ProgressBarComponentClass.Instance, ControlLibraries.MSComctlLib);
        vm.SpawnControlCenter(ProgressBarComponentClass.Instance, ControlLibraries.MSComctlLib);
        form.HeaderLines.Should().Equal(MsComctlForm);

        vm.CommitLayout();
        var (frm, _) = new FormSerializer().Serialize(form, "Form1.frm");
        frm.Should().StartWith("VERSION 5.00\r\n" + MsComctlForm + "\r\nBegin VB.Form Form1");
        frm.Should().Contain("Begin MSComctlLib.ProgressBar ProgressBar1").And.Contain("Begin MSComctlLib.ProgressBar ProgressBar2");
    }

    [AvaloniaFact]
    public void A_StatusBar_is_offered_only_by_its_measured_library_and_placement_declares_it()
    {
        var (_, toolbox, vm, form) = Designer(ControlLibraries.MSComctlLib, ControlLibraries.ComctlLib);
        var tool = toolbox.Components.Single(c => c.Name == "StatusBar");
        tool.VBTypeName.Should().Be("MSComctlLib.StatusBar");
        vm.SpawnControlCenter((ComponentBaseClass)tool.BaseClass!, tool.Library);
        form.HeaderLines.Should().Equal(MsComctlForm);
        var control = vm.Components.Single(c => c.Instance.BaseClass is StatusBarComponentClass);
        control.Instance.GetPropertyOrDefault(VBProperties.AlignProperty).Should().Be(HexIDE.Runtime.BuiltinTypes.VBAlign.vbAlignBottom);
        control.Instance.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(25);
    }

    // A ProgressBar from COMCTL32.OCX's button is 5.0's, under its type name, and its form declares COMCTL32.OCX.
    [AvaloniaFact]
    public void A_ProgressBar_placed_from_the_older_library_is_that_librarys()
    {
        var (_, toolbox, vm, form) = Designer(ControlLibraries.MSComctlLib, ControlLibraries.ComctlLib);
        var comctl = toolbox.Components.Single(c => c.Library == ControlLibraries.ComctlLib);

        toolbox.SelectedComponent = comctl;
        vm.SpawnControlDrawn(new Avalonia.Point(8, 8), new Avalonia.Point(128, 28));

        form.HeaderLines.Should().Equal(Comctl32Form);
        vm.CommitLayout();
        var bar = form.Components.Single(c => c.BaseClass is ProgressBarComponentClass);
        ProgressBarComponentClass.Instance.LibraryOf(bar).Should().BeSameAs(ControlLibraries.ComctlLib);
        new FormSerializer().Serialize(form, "Form1.frm").frmText.Should().Contain("Begin ComctlLib.ProgressBar ProgressBar1");
    }
}
