using System.Collections.Generic;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A <c>.frm</c> property name is read in any case, as VB6 reads it (#811). Measured: a Label written with
/// <c>caption</c> and <c>backcolor</c>, and a TextBox with <c>TEXT</c>, all load in vb6.exe.
/// </summary>
public class FrmPropertyNameCaseTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = new();
        public void LogError(string error) => Errors.Add(error);
    }

    private const string Frm = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Probe"
           Begin VB.TextBox Text1
              Height          =   375
              Left            =   120
              TEXT            =   "upper key"
              Top             =   120
              Width           =   1800
           End
           Begin VB.Label Label1
              caption         =   "lower key"
              Height          =   375
              Left            =   2160
              Top             =   120
              Width           =   1200
              backcolor       =   &H000000FF&
           End
        End
        Attribute VB_Name = "Form1"
        """;

    [Fact]
    public void A_property_name_in_another_case_loads_as_the_property()
    {
        var sink = new Sink();
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), Frm, sink)!;

        var text1 = form.Components.Single(c => c.BaseClass is TextBoxComponentClass);
        var label1 = form.Components.Single(c => c.BaseClass is LabelComponentClass);

        text1.GetPropertyOrDefault(VBProperties.TextProperty).Should().Be("upper key");
        label1.GetPropertyOrDefault(VBProperties.CaptionProperty).Should().Be("lower key");
        label1.GetPropertyOrDefault(VBProperties.BackColorProperty).Should().Be(VBColor.FromOle(255));
        label1.UnknownRawProperties.Should().BeEmpty("each line named a property the Label has");
        sink.Errors.Should().BeEmpty();
    }
}
