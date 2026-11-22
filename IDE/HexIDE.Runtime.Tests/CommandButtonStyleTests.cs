using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using System.Linq;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

// Measured in oracle PR #5217: a-commandbuttons-style-assigned-from-code-and-painted-in-the-designer.md.
public class CommandButtonStyleTests : BaseVBTestFixture
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static FormDefinition Load(int style = 1) => new FormDeserializer().Deserialize(
        new ProjectDefinition(VBProjectType.EXE, "P"), Source(style), new NullSink())!;

    private static string Source(int style) => $$"""
        VERSION 5.00
        Begin VB.Form Form1 
           Caption         =   "Form1"
           ClientHeight    =   3600
           ClientLeft      =   60
           ClientTop       =   405
           ClientWidth     =   4800
           LinkTopic       =   "Form1"
           ScaleHeight     =   3600
           ScaleWidth      =   4800
           Begin VB.CommandButton Command1 
              BackColor       =   &H000000FF&
              Caption         =   "Command1"
              Height          =   495
              Left            =   120
        {{(style == 1 ? "      Style           =   1  'Graphical\n" : "")}}      TabIndex        =   0
              Top             =   120
              Width           =   1215
           End
        End
        Attribute VB_Name = "Form1"

        """.ReplaceLineEndings("\r\n");

    private void Button(int style)
    {
        context.AllocVariable(rootEnv, "Command1", new Vb6Value(new VBCommandButton { Style = (VBButtonStyle)style }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Style_reads_as_Integer(int style)
    {
        Button(style);
        await Run("Debug.Print Command1.Style, TypeName(Command1.Style)");
        AssertDebugLog([new(style), new("Integer")]);
    }

    [Theory]
    [InlineData("Object", 0, 1)]
    [InlineData("Control", 1, 0)]
    [InlineData("Variant", 0, 1)]
    public async Task Late_bound_assignment_is_382_and_keeps_Style(string type, int style, int assigned)
    {
        Button(style);
        await Run($$"""
            Dim target As {{type}}
            Set target = Command1
            On Error Resume Next
            target.Style = {{assigned}}
            Debug.Print Err.Number, Err.Description, target.Style
            """);
        AssertDebugLog([new(382L), new("'Style' property cannot be set at run time"), new(style)]);
    }

    [Fact]
    public async Task Controls_collection_assignment_is_382_and_keeps_Style()
    {
        var button = new VBCommandButton { Style = VBButtonStyle.Graphical };
        VBProps.SetName(button, "Command1");
        context.AllocVariable(rootEnv, "Controls", new Vb6Value(new FormControls([button],
            new System.Collections.Generic.Dictionary<string, ControlArrayGroup>())));
        await Run("On Error Resume Next\nControls(\"Command1\").Style = 0\nDebug.Print Err.Number, Err.Description, Controls(\"Command1\").Style");
        AssertDebugLog([new(382L), new("'Style' property cannot be set at run time"), new(1)]);
    }

    [Fact]
    public async Task Direct_assignment_uses_the_interpreters_runtime_refusal()
    {
        Button(0);
        await Run("On Error Resume Next\nCommand1.Style = 1\nDebug.Print Err.Number, Err.Description, Command1.Style");
        AssertDebugLog([new(382L), new("'Style' property cannot be set at run time"), new(0)]);
    }

    [Fact]
    public void Returning_to_Standard_drops_the_saved_Style_line()
    {
        var form = Load();
        var component = form.Components.Single(c => c.BaseClass is CommandButtonComponentClass);
        component.BaseClass.PropertiesByName.Should().ContainKey("Style");
        var property = component.BaseClass.PropertiesByName["Style"];
        component.SetUntypedProperty(property, System.Enum.ToObject(property.PropertyType, 0));
        new FormSerializer().Serialize(form, "Form1.frm").Item1.Should().Be(Source(0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Style_is_modelled_and_round_trips_in_VB6s_position(int style)
    {
        var form = Load(style);
        var component = form.Components.Single(c => c.BaseClass is CommandButtonComponentClass);
        component.BaseClass.PropertiesByName.Should().ContainKey("Style");
        var property = component.BaseClass.PropertiesByName["Style"];
        System.Convert.ToInt32(component.GetBoxedPropertyOrDefault(property)).Should().Be(style);
        component.UnknownRawProperties.Should().NotContain(p => p.Name == "Style");
        new FormSerializer().Serialize(form, "Form1.frm").Item1.Should().Be(Source(style));
    }
}
