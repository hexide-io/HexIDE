using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A form, UserControl or PropertyPage HexIDE creates is written as VB6's IDE writes one it has just created: byte for
/// byte, each expectation being the file VB6 6.00.8176 wrote for Add Form, Add User Control and Add Property Page then
/// Ctrl+S (hexide-io/HexIDE#1538; docs/vb6-fidelity-oracle/what-vb6s-add-form-saves.md, "What VB6's Add Form saves").
/// </summary>
/// <remarks>
/// A form's client area and its <c>ClientLeft</c>/<c>ClientTop</c> are VB6's 4800 × 3600 window less the oracle's Windows
/// 10 borders and caption, so they are that machine's figures. A UserControl and a PropertyPage have no border, so
/// theirs are the window itself.
/// </remarks>
public class NewDesignerFileTests
{
    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "Project1");

    [Fact]
    public void A_new_form_is_the_file_VB6s_Add_Form_saves()
    {
        var form = FormDefinition.NewForm(Project, FormComponentClass.Instance, "Form1");

        new FormSerializer().Serialize(form, "Form1.frm").Item1.Should().Be(
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Caption         =   \"Form1\"\r\n" +
            "   ClientHeight    =   3015\r\n" +
            "   ClientLeft      =   120\r\n" +
            "   ClientTop       =   465\r\n" +
            "   ClientWidth     =   4560\r\n" +
            "   LinkTopic       =   \"Form1\"\r\n" +
            "   ScaleHeight     =   3015\r\n" +
            "   ScaleWidth      =   4560\r\n" +
            "   StartUpPosition =   3  'Windows Default\r\n" +
            "End\r\n" +
            "Attribute VB_Name = \"Form1\"\r\n" +
            "Attribute VB_GlobalNameSpace = False\r\n" +
            "Attribute VB_Creatable = False\r\n" +
            "Attribute VB_PredeclaredId = True\r\n" +
            "Attribute VB_Exposed = False\r\n");
    }

    [Fact]
    public void A_new_UserControl_is_the_file_VB6s_Add_User_Control_saves()
    {
        var module = ModuleDefinition.NewDesigner(Project, "UserControl1", ModuleKind.UserControl,
            FormComponentClass.ForRoot("VB.UserControl"));

        new FormSerializer().Serialize(module.FormPart!, module.Code, "UserControl1.ctl").Item1.Should().Be(
            "VERSION 5.00\r\n" +
            "Begin VB.UserControl UserControl1 \r\n" +
            "   ClientHeight    =   3600\r\n" +
            "   ClientLeft      =   0\r\n" +
            "   ClientTop       =   0\r\n" +
            "   ClientWidth     =   4800\r\n" +
            "   ScaleHeight     =   3600\r\n" +
            "   ScaleWidth      =   4800\r\n" +
            "End\r\n" +
            "Attribute VB_Name = \"UserControl1\"\r\n" +
            "Attribute VB_GlobalNameSpace = False\r\n" +
            "Attribute VB_Creatable = True\r\n" +
            "Attribute VB_PredeclaredId = False\r\n" +
            "Attribute VB_Exposed = False\r\n");
    }

    [Fact]
    public void A_new_PropertyPage_is_the_file_VB6s_Add_Property_Page_saves()
    {
        var module = ModuleDefinition.NewDesigner(Project, "PropertyPage1", ModuleKind.PropertyPage,
            FormComponentClass.ForRoot("VB.PropertyPage"));

        new FormSerializer().Serialize(module.FormPart!, module.Code, "PropertyPage1.pag").Item1.Should().Be(
            "VERSION 5.00\r\n" +
            "Begin VB.PropertyPage PropertyPage1 \r\n" +
            "   Caption         =   \"PropertyPage1\"\r\n" +
            "   ClientHeight    =   3600\r\n" +
            "   ClientLeft      =   0\r\n" +
            "   ClientTop       =   0\r\n" +
            "   ClientWidth     =   4800\r\n" +
            "   PaletteMode     =   0  'Halftone\r\n" +
            "   ScaleHeight     =   3600\r\n" +
            "   ScaleWidth      =   4800\r\n" +
            "End\r\n" +
            "Attribute VB_Name = \"PropertyPage1\"\r\n" +
            "Attribute VB_GlobalNameSpace = False\r\n" +
            "Attribute VB_Creatable = False\r\n" +
            "Attribute VB_PredeclaredId = False\r\n" +
            "Attribute VB_Exposed = False\r\n");
    }

    [Theory]
    [InlineData(ModuleKind.UserControl, "Gauge")]
    [InlineData(ModuleKind.PropertyPage, "General")]
    public void A_new_designer_module_is_linked_to_its_designer_half_under_its_own_name(ModuleKind kind, string name)
    {
        var module = ModuleDefinition.NewDesigner(Project, name, kind, FormComponentClass.Instance);

        module.FormPart.Should().NotBeNull();
        module.FormPart!.OwningModule.Should().BeSameAs(module);
        module.FormPart.Name.Should().Be(name);
        module.Code.Should().StartWith($"Attribute VB_Name = \"{name}\"\r\n");
    }
}
