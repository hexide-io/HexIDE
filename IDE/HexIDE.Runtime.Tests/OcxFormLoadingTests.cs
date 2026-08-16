using System;
using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Guards forms that host ActiveX controls (issue #19). Two independent defects made every such form
/// unopenable:
///
/// 1. An <c>Object = "{GUID}#2.0#0"; "mscomctl.ocx"</c> declaration sits before the root <c>Begin</c> and
///    contains '=', so it fell through to the scalar-property branch and Peek()'d an empty component
///    stack — "Stack empty".
/// 2. <c>BeginProperty</c> blocks nest (an ImageList persists <c>Images</c> containing a
///    <c>ListImage1</c> per image), but the parser tracked one open block in three scalar fields, so the
///    inner EndProperty cleared them and the outer dereferenced null.
///
/// HexIDE cannot host an OCX. It must still open the form and not corrupt the declaration — exactly the
/// posture the .vbp side already takes with its Object= references.
/// </summary>
public class OcxFormLoadingTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = new();
        public void LogError(string error) => Errors.Add(error);
    }

    private const string OcxForm =
        "VERSION 5.00\r\n" +
        "Object = \"{831FDD16-0C5C-11d2-A9FC-0000F8754DA1}#2.0#0\"; \"mscomctl.ocx\"\r\n" +
        "Object = \"{EAB22AC0-30C1-11CF-A7EB-0000C05BAE0B}#1.1#0\"; \"shdocvw.dll\"\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   ClientHeight    =   3195\r\n" +
        "   ClientLeft      =   60\r\n" +
        "   ClientTop       =   345\r\n" +
        "   ClientWidth     =   4680\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n" +
        "Option Explicit\r\n";

    private const string NestedBagForm =
        "VERSION 5.00\r\n" +
        "Object = \"{831FDD16-0C5C-11d2-A9FC-0000F8754DA1}#2.0#0\"; \"mscomctl.ocx\"\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   Begin MSComctlLib.ImageList ImageList1 \r\n" +
        "      BeginProperty Images {2C247F25-8591-11D1-B16A-00C0F0283628} \r\n" +
        "         NumListImages   =   2\r\n" +
        "         BeginProperty ListImage1 {2C247F27-8591-11D1-B16A-00C0F0283628} \r\n" +
        "            Key             =   \"one\"\r\n" +
        "         EndProperty\r\n" +
        "         BeginProperty ListImage2 {2C247F27-8591-11D1-B16A-00C0F0283628} \r\n" +
        "            Key             =   \"two\"\r\n" +
        "         EndProperty\r\n" +
        "      EndProperty\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    private static FormDefinition? Load(string source, Sink sink) =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, sink);

    [Fact]
    public void A_form_with_an_Object_declaration_opens()
    {
        var sink = new Sink();
        Load(OcxForm, sink).Should().NotBeNull(
            "an Object= declaration before the root Begin must not crash the parser: "
          + string.Join(" | ", sink.Errors));
    }

    [Fact]
    public void The_Object_declarations_survive_a_round_trip_verbatim()
    {
        var form = Load(OcxForm, new Sink())!;
        var (rendered, _) = new FormSerializer().Serialize(form, "Form1.frm");

        rendered.Should().Contain("Object = \"{831FDD16-0C5C-11d2-A9FC-0000F8754DA1}#2.0#0\"; \"mscomctl.ocx\"");
        rendered.Should().Contain("Object = \"{EAB22AC0-30C1-11CF-A7EB-0000C05BAE0B}#1.1#0\"; \"shdocvw.dll\"");

        // Order matters, and they must sit between VERSION and the root Begin.
        var lines = rendered.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        lines.IndexOf("VERSION 5.00").Should().BeLessThan(lines.FindIndex(l => l.StartsWith("Object =")));
        lines.FindLastIndex(l => l.StartsWith("Object ="))
             .Should().BeLessThan(lines.FindIndex(l => l.StartsWith("Begin VB.Form")));
    }

    [Fact]
    public void A_nested_property_bag_does_not_throw()
    {
        var sink = new Sink();
        Load(NestedBagForm, sink).Should().NotBeNull(
            "nested BeginProperty blocks are how OCX controls persist collections: "
          + string.Join(" | ", sink.Errors));
    }

    [Fact]
    public void An_inner_bag_does_not_displace_its_parent()
    {
        // The bug this catches: a nested block overwriting the parent's entry, so Images became
        // ListImage2 and NumListImages vanished.
        var vb = new VbFrmFormatDeserializer();
        var (root, _) = vb.Deserialize(NestedBagForm);

        var imageList = root.SubComponents.Single(c => c.Name == "ImageList1");
        imageList.Properties.Should().ContainKey("Images");

        var images = (Dictionary<string, object>)imageList.Properties["Images"];
        images.Should().ContainKey("NumListImages");
        images.Should().ContainKey("ListImage1");
        images.Should().ContainKey("ListImage2");
    }

    [Fact]
    public void A_form_with_no_header_lines_is_unaffected()
    {
        var plain = OcxForm.Split('\n').Where(l => !l.StartsWith("Object =")).Aggregate((a, b) => a + "\n" + b);
        var form = Load(plain, new Sink())!;

        form.HeaderLines.Should().BeEmpty();
        new FormSerializer().Serialize(form, "Form1.frm").Item1.Should().NotContain("Object =");
    }
}
