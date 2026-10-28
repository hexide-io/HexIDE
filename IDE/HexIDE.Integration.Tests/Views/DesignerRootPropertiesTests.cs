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

    private static (FormDefinition File, FormEditViewModel Designer) Open(string kind, string name, ProjectDefinition? project = null)
    {
        project ??= new ProjectDefinition(VBProjectType.EXE, "P");
        var file = new FormDeserializer().Deserialize(project, File(kind, name), new NullSink())!;
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

    // Unchanged: a form keeps the list it had.
    [AvaloniaFact]
    public void A_form_keeps_its_list()
    {
        var (_, designer) = Open("Form", "Form1");

        Listed(designer).Should().Equal("(Name)", "BackColor", "Caption", "Enabled", "Font", "ForeColor", "Height", "Icon",
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
}
