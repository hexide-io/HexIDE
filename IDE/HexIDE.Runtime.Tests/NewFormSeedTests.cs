using System.Collections.Generic;
using Avalonia.Controls;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// What a form HexIDE creates is written as, set against what VB6 writes for one. Every expectation is a literal
/// taken from VB6's own files, never composed from a host API, so it cannot pass on one OS by accident.
///
/// <para>
/// Three defects met in this one seed. The code section was bare LF with no final line break, under a CRLF
/// designer block (hexide-io/HexIDE#931). It had no <c>Attribute</c> block, where VB6 writes five
/// (hexide-io/HexIDE#516). And it had no <c>StartUpPosition</c> line, which VB6 reads as 0, Manual, so the form
/// opened at the screen's origin with its title bar off the top (hexide-io/HexIDE#932).
/// </para>
/// </summary>
public class NewFormSeedTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = new();
        public void LogError(string error) => Errors.Add(error);
    }

    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "Project1");

    private static string Written(FormDefinition form) => new FormSerializer().Serialize(form, "Form1.frm").Item1;

    /// <summary>The block every new form in VB6's Template tree carries, nineteen files of twenty.</summary>
    private const string VbAttributes =
        "Attribute VB_Name = \"Form1\"\r\n"
      + "Attribute VB_GlobalNameSpace = False\r\n"
      + "Attribute VB_Creatable = False\r\n"
      + "Attribute VB_PredeclaredId = True\r\n"
      + "Attribute VB_Exposed = False\r\n";

    [Fact]
    public void A_new_form_is_written_in_CRLF_throughout_and_ends_with_a_line_break()
    {
        var text = Written(FormDefinition.NewForm(Project, FormComponentClass.Instance, "Form1"));

        text.Replace("\r\n", "").Should().NotContain("\n", "VB6 writes a .frm in CRLF throughout");
        text.Replace("\r\n", "").Should().NotContain("\r");
        text.Should().EndWith("\r\n");
    }

    [Fact]
    public void The_seed_every_form_starts_from_is_CRLF_too()
    {
        // The shared constructor also builds the designer half of a new UserControl or PropertyPage, which takes
        // no Attribute block of a form's, but whose code reaches a file all the same.
        var code = new FormDefinition(Project, FormComponentClass.Instance, "Form1").Code;

        code.Replace("\r\n", "").Should().NotContain("\n");
        code.Should().EndWith("\r\n");
    }

    [Fact]
    public void A_new_form_opens_its_code_with_the_five_attributes_VB6_writes()
    {
        var form = FormDefinition.NewForm(Project, FormComponentClass.Instance, "Form1");

        FormCodeText.AttributeBlock(form.Code).Should().Be(VbAttributes);
        Written(form).Should().Contain("End\r\n" + VbAttributes,
            "the block follows the designer block's End directly, as in VB6's Template/Projects/Form1.frm");
    }

    [Fact]
    public void A_new_form_names_itself_in_VB_Name()
    {
        var form = FormDefinition.NewForm(Project, FormComponentClass.Instance, "Orders");

        FormCodeText.AttributeBlock(form.Code).Should().StartWith("Attribute VB_Name = \"Orders\"\r\n");
    }

    [Fact]
    public void A_new_form_is_written_with_StartUpPosition_3()
    {
        // A form with no line would open, in VB6, at the origin with its caption off the screen.
        Written(FormDefinition.NewForm(Project, FormComponentClass.Instance, "Form1"))
            .Should().Contain("\r\n   StartUpPosition =   3  'Windows Default\r\n");
    }

    [Fact]
    public void A_form_with_no_StartUpPosition_line_starts_where_its_file_puts_it()
    {
        // Measured (#932): VB6 reads a missing line as 0, Manual, and places the window at ClientLeft/ClientTop.
        // HexIDE read it as 3 and centred VB6's own About Dialog, which carries no line.
        var form = new FormDeserializer().Deserialize(Project,
            "VERSION 5.00\r\n"
          + "Begin VB.Form Form1 \r\n"
          + "   ClientLeft      =   2340\r\n"
          + "   ClientTop       =   1935\r\n"
          + "   ClientWidth     =   4800\r\n"
          + "End\r\n"
          + "Attribute VB_Name = \"Form1\"\r\n", new Sink())!;

        var position = form.Components[0].GetPropertyOrDefault(VBProperties.StartUpPositionProperty);
        position.Should().Be(VBStartupPosition.StartUpManual);
        position.ToAvalonia().Should().Be(WindowStartupLocation.Manual);
        Written(form).Should().NotContain("StartUpPosition", "a form that had no line is written back without one");
    }
}
