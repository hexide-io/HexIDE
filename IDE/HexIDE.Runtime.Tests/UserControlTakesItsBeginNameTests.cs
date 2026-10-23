using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A UserControl or PropertyPage is named by its designer block's <c>Begin</c> line, as VB6 names it, whatever its file
/// or its <c>Attribute VB_Name</c> says (#1411). HexIDE named it after its file, so a form that used the control by
/// VB6's name found no such class, and kept the control as text it could not draw or run.
/// </summary>
/// <remarks>
/// Measured with <c>vb6.exe /make</c> (6.00.8176 SP6, p-code), on a Standard EXE called <c>probe</c> with a private
/// UserControl. These are the probes' own layouts:
/// <list type="bullet">
///   <item><c>Gauge.ctl</c> holding <c>Begin VB.UserControl ctlDial</c> and <c>VB_Name = "ctlDial"</c>: a form's
///   <c>Begin probe.ctlDial</c> builds, and <c>Begin probe.Gauge</c> fails, "Class probe.Gauge of control uc1 was not a
///   loaded control class".</item>
///   <item><c>Dial.ctl</c> holding <c>Begin VB.UserControl ucBegin</c> and <c>VB_Name = "Gauge"</c>: only
///   <c>probe.ucBegin</c> builds; <c>probe.Gauge</c> and <c>probe.Dial</c> fail the same way.</item>
/// </list>
/// </remarks>
public class UserControlTakesItsBeginNameTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "hexide-ucname-" + Guid.NewGuid().ToString("N"));

    public UserControlTakesItsBeginNameTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string error) { }
    }

    private static string Ctl(string begin, string vbName) =>
        "VERSION 5.00\r\n" +
        $"Begin VB.UserControl {begin} \r\n" +
        "   ClientHeight    =   735\r\n   ClientLeft      =   0\r\n   ClientTop       =   0\r\n   ClientWidth     =   1215\r\n" +
        "   ScaleHeight     =   735\r\n   ScaleWidth      =   1215\r\n" +
        "End\r\n" +
        $"Attribute VB_Name = \"{vbName}\"\r\n" +
        "Attribute VB_GlobalNameSpace = False\r\nAttribute VB_Creatable = True\r\nAttribute VB_PredeclaredId = False\r\n" +
        "Attribute VB_Exposed = False\r\n";

    private static string FormUsing(string controlClass) =>
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n   ClientHeight    =   1800\r\n   ClientLeft      =   60\r\n" +
        "   ClientTop       =   450\r\n   ClientWidth     =   3000\r\n   ScaleHeight     =   1800\r\n   ScaleWidth      =   3000\r\n" +
        $"   Begin probe.{controlClass} uc1 \r\n" +
        "      Height          =   735\r\n      Left            =   240\r\n      TabIndex        =   0\r\n" +
        "      Top             =   240\r\n      Width           =   1215\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    private async Task<ProjectDefinition> Load(string ctlFile, string begin, string vbName, string formUses)
    {
        var token = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(dir, ctlFile), Ctl(begin, vbName), token);
        await File.WriteAllTextAsync(Path.Combine(dir, "Form1.frm"), FormUsing(formUses), token);
        var vbp = Path.Combine(dir, "probe.vbp");
        await File.WriteAllTextAsync(vbp,
            $"Type=Exe\r\nForm=Form1.frm\r\nUserControl={ctlFile}\r\nStartup=\"Form1\"\r\nName=\"probe\"\r\n", token);
        return (await new ProjectLoader().LoadAsync(vbp, new NullSink())).Project;
    }

    private static bool Drawn(ProjectDefinition project) =>
        project.Forms.Single().Components.Any(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "uc1");

    [Fact]
    public async Task A_UserControl_is_named_by_its_Begin_line_not_its_file()
    {
        var project = await Load("Gauge.ctl", "ctlDial", "ctlDial", "ctlDial");

        project.Modules.Single().Name.Should().Be("ctlDial");
        Drawn(project).Should().BeTrue("the form names the control as VB6 does, probe.ctlDial");
    }

    [Fact]
    public async Task A_form_naming_the_control_by_its_file_finds_no_class_as_in_VB6()
    {
        var project = await Load("Gauge.ctl", "ctlDial", "ctlDial", "Gauge");

        Drawn(project).Should().BeFalse("vb6.exe: Class probe.Gauge of control uc1 was not a loaded control class");
        project.Forms.Single().UnknownChildSubtreeTexts.Should().ContainSingle(t => t.Contains("Begin probe.Gauge uc1"),
            "the control it cannot load is still kept, so a save writes it back");
    }

    [Theory]
    [InlineData("ucBegin", true)]
    [InlineData("Gauge", false)]
    [InlineData("Dial", false)]
    public async Task Where_Begin_and_VB_Name_disagree_only_the_Begin_name_is_the_class(string formUses, bool drawn)
    {
        var project = await Load("Dial.ctl", "ucBegin", "Gauge", formUses);

        project.Modules.Single().Name.Should().Be("ucBegin");
        Drawn(project).Should().Be(drawn);
    }

    [Fact]
    public async Task A_PropertyPage_is_named_by_its_Begin_line_too()
    {
        // Measured: Page1.pag holding Begin VB.PropertyPage ppBegin and VB_Name "Page1" builds with "The name 'ppBegin'
        // will be used".
        var token = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(dir, "Page1.pag"),
            "VERSION 5.00\r\nBegin VB.PropertyPage ppBegin \r\n   Caption         =   \"Page\"\r\n   ClientHeight    =   1200\r\n" +
            "   ClientLeft      =   0\r\n   ClientTop       =   0\r\n   ClientWidth     =   1200\r\n   ScaleHeight     =   1200\r\n" +
            "   ScaleWidth      =   1200\r\nEnd\r\nAttribute VB_Name = \"Page1\"\r\n", token);
        var vbp = Path.Combine(dir, "Ctl.vbp");
        await File.WriteAllTextAsync(vbp, "Type=Control\r\nPropertyPage=Page1.pag\r\nStartup=\"(None)\"\r\nName=\"CtlProbe\"\r\n", token);

        var project = (await new ProjectLoader().LoadAsync(vbp, new NullSink())).Project;

        project.Modules.Single().Name.Should().Be("ppBegin");
    }

    [Fact]
    public async Task The_project_file_still_names_the_control_by_its_file()
    {
        var project = await Load("Gauge.ctl", "ctlDial", "ctlDial", "ctlDial");

        new ProjectSerializer().Serialize(project, Path.Combine(dir, "probe.vbp"))
            .Should().Contain("UserControl=Gauge.ctl\r\n", "the line is a path, and the file has not moved");
    }
}
