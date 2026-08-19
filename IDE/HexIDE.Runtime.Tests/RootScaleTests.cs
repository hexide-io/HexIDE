using System;
using System.Collections.Generic;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using static HexIDE.Runtime.Components.VBProperties;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Guards issue #92 — ScaleWidth/ScaleHeight are in ScaleMode's units, and the writer used to copy the
/// form's own width and height into them in twips regardless.
///
/// Corpus-INDEPENDENT, for the reason RootRectangleTests gives: CI is Linux with no VB6 install, so the
/// two files that actually exercise this — Colorful Control.ctl at ScaleMode = 3 'Pixel and About
/// Dialog.frm at ScaleMode = 0 'User — are not there to be checked.
/// </summary>
public class RootScaleTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = new();
        public void LogError(string error) => Errors.Add(error);
    }

    private static FormDefinition Load(string source) =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;

    private static string Save(FormDefinition form) =>
        new FormSerializer().Serialize(form, "Form1.frm").Item1;

    private static Dictionary<string, string> RootProperties(string frm) =>
        frm.Split(["\r\n", "\n"], StringSplitOptions.None)
           .Where(l => l.StartsWith("   ") && !l.StartsWith("      ") && l.Contains('='))
           .Select(l => l.Split('=', 2))
           .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.Ordinal);

    private static string Root(string extraProperties) =>
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   ClientHeight    =   3600\r\n" +
        "   ClientWidth     =   4800\r\n" +
        extraProperties +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    [Fact]
    public void APixelScale_IsWrittenInPixels_NotInTwips()
    {
        // Colorful Control.ctl's numbers. Writing 4800/3600 beside ScaleMode = 3 leaves the declared
        // scale and the numbers under it a factor of fifteen apart.
        var properties = RootProperties(Save(Load(Root(
            "   ScaleHeight     =   240\r\n" +
            "   ScaleMode       =   3  'Pixel\r\n" +
            "   ScaleWidth      =   320\r\n"))));

        properties["ScaleWidth"].Should().Be("320");
        properties["ScaleHeight"].Should().Be("240");
    }

    [Fact]
    public void AUserScale_IsPreserved_BecauseNothingCanDeriveIt()
    {
        // About Dialog.frm's numbers: ScaleMode = 0 'User, and the pair is a coordinate system the
        // developer chose rather than a measurement of anything. That Microsoft's file still carries them
        // after being saved from the VB6 IDE is also the evidence that VB6 does not recompute them.
        var properties = RootProperties(Save(Load(Root(
            "   ScaleHeight     =   2453.724\r\n" +
            "   ScaleMode       =   0  'User\r\n" +
            "   ScaleWidth      =   5380.766\r\n"))));

        properties["ScaleWidth"].Should().Be("5380.766");
        properties["ScaleHeight"].Should().Be("2453.724");
    }

    [Fact]
    public void ATwipsScale_MatchesTheClientRectangle()
    {
        var properties = RootProperties(Save(Load(Root(
            "   ScaleHeight     =   3600\r\n" +
            "   ScaleWidth      =   4800\r\n"))));

        properties["ScaleWidth"].Should().Be("4800");
        properties["ScaleHeight"].Should().Be("3600");
    }

    [Fact]
    public void AResizeUnderAPixelScale_KeepsTheScaleInPixels()
    {
        // Preserving the loaded pair verbatim would have been enough to round-trip every corpus file and
        // still wrong here: resizing must move the scale with the form, in the form's own units.
        var form = Load(Root(
            "   ScaleHeight     =   240\r\n" +
            "   ScaleMode       =   3  'Pixel\r\n" +
            "   ScaleWidth      =   320\r\n"));
        var root = form.Components.Single(c => c.BaseClass is FormComponentClass);
        root.SetProperty(WidthProperty, root.GetPropertyOrDefault(WidthProperty) + 15); // +15 px = +225 twips

        var properties = RootProperties(Save(form));

        properties["ClientWidth"].Should().Be("5025");
        properties["ScaleWidth"].Should().Be("335");
    }

    [Fact]
    public void ScaleMode_IsStillPreservedVerbatim_CommentAndAll()
    {
        // ScaleMode is captured on load so the writer knows the units, but it is NOT consumed: it is not a
        // modelled property, and it has to keep falling through to verbatim preservation. Consuming it
        // would drop the line entirely and leave a Scale* pair with no declared scale beside it.
        var saved = Save(Load(Root(
            "   ScaleHeight     =   240\r\n" +
            "   ScaleMode       =   3  'Pixel\r\n" +
            "   ScaleWidth      =   320\r\n")));

        saved.Should().Contain("   ScaleMode       =   3  'Pixel");
    }

    [Fact]
    public void AFormThatDeclaredNoScaleAtAll_StillGetsTheTwipsPair()
    {
        // Every one of VB6's twenty-two designer files declares the pair, so a form without one is
        // HexIDE's own construction. Twips is the default mode, which makes the pair the client rectangle.
        var properties = RootProperties(Save(Load(Root(""))));

        properties["ScaleWidth"].Should().Be("4800");
        properties["ScaleHeight"].Should().Be("3600");
    }
}
