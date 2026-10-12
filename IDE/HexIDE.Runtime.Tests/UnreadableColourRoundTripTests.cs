using System.Collections.Generic;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A known property whose value HexIDE cannot read is kept as written and saved back, rather than dropped (#802).
/// A palette colour is the case: VB6 accepts <c>&amp;H02C0C0C0&amp;</c>, HexIDE has no colour that holds one (#786),
/// and the line used to survive on a TextBox only because TextBox did not model BackColor at all.
/// </summary>
public class UnreadableColourRoundTripTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = new();
        public void LogError(string error) => Errors.Add(error);
    }

    private static string Form(string control, string colour) => $"""
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           ClientHeight    =   3000
           ClientLeft      =   60
           ClientTop       =   345
           ClientWidth     =   4000
           Begin VB.{control} Control1
              BackColor       =   {colour}
              Height          =   375
              Left            =   120
              Top             =   120
              Width           =   1200
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static (FormDefinition Form, Sink Sink) Load(string source)
    {
        var sink = new Sink();
        return (new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, sink)!, sink);
    }

    private static string Save(FormDefinition form) => new FormSerializer().Serialize(form, "Form1.frm").Item1;

    private static string? BackColorLine(string frm) =>
        frm.Split('\n').Select(l => l.Trim()).SingleOrDefault(l => l.StartsWith("BackColor", System.StringComparison.Ordinal));

    [Theory]
    [InlineData("TextBox", "&H02C0C0C0&")]   // a palette-relative colour, #786
    [InlineData("Label", "&H02C0C0C0&")]
    [InlineData("CommandButton", "&H02C0C0C0&")]
    [InlineData("TextBox", "&H80000019&")]   // a system index past &H18, which VB6 refuses at run time with 380
    [InlineData("Label", "&H80000019&")]
    public void A_colour_HexIDE_cannot_read_is_saved_back_as_written(string control, string colour)
    {
        var (form, sink) = Load(Form(control, colour));

        sink.Errors.Should().ContainSingle(e => e.Contains("BackColor") && e.Contains("invalid value"));
        var saved = Save(form);
        BackColorLine(saved).Should().NotBeNull().And.Contain(colour, "the line is kept as written, not dropped");
    }

    [Fact]
    public void A_colour_set_in_the_designer_replaces_the_line_it_could_not_read()
    {
        var (form, _) = Load(Form("TextBox", "&H02C0C0C0&"));
        var textBox = form.Components.Single(c => c.BaseClass is TextBoxComponentClass);

        textBox.SetUntypedProperty(VBProperties.BackColorProperty, VBColor.FromOle(0xFF));

        var saved = Save(form);
        saved.Split('\n').Count(l => l.Trim().StartsWith("BackColor", System.StringComparison.Ordinal))
            .Should().Be(1, "the file must not carry the property twice");
        BackColorLine(saved).Should().Contain("&H000000FF&");
    }

    [Theory]
    [InlineData("&H80000019&")]
    [InlineData("&H80010005&")]   // a system colour with stray bits in its middle bytes
    public void A_system_colour_outside_the_palette_does_not_parse(string text)
    {
        VBColor.TryParse(text, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("&H80000000&")]
    [InlineData("&H80000018&")]
    public void Every_system_colour_in_the_palette_parses(string text)
    {
        VBColor.TryParse(text, out var colour).Should().BeTrue();
        colour.Type.Should().Be(VBColor.ColorType.SystemColor);
    }
}
