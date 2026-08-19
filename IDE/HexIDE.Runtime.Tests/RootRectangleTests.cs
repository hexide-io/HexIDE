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
/// Guards issue #104 — a designer root has TWO rectangles and HexIDE stored one.
///
/// A .frm records the root's client rectangle as ClientLeft/ClientTop/ClientWidth/ClientHeight, and may
/// record the outer window rectangle beside it as the plain four. VB6's own Forms\Dialog.frm does exactly
/// that, and its two rectangles differ by the window frame of a Fixed Dialog border.
///
/// Corpus-INDEPENDENT on purpose, for the reason the container tests give: CI is Linux with no VB6 install,
/// so anything asserted only against the Template tree passes there by having nothing to check. These carry
/// their own fixtures.
/// </summary>
public class RootRectangleTests
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

    /// <summary>Every root-level property line, as "Name" → "value", indentation and padding stripped.</summary>
    private static Dictionary<string, string> RootProperties(string frm) =>
        frm.Split(["\r\n", "\n"], StringSplitOptions.None)
           .Where(l => l.StartsWith("   ") && !l.StartsWith("      ") && l.Contains('='))
           .Select(l => l.Split('=', 2))
           .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.Ordinal);

    /// <summary>The shape of VB6's own new-project template: a client rectangle and nothing else.</summary>
    private const string ClientRectOnly =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   ClientHeight    =   4980\r\n" +
        "   ClientLeft      =   60\r\n" +
        "   ClientTop       =   348\r\n" +
        "   ClientWidth     =   6972\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    /// <summary>The shape of VB6's own Forms\Dialog.frm: both rectangles, a frame apart.</summary>
    private const string BothRectangles =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Dialog \r\n" +
        "   ClientHeight    =   3195\r\n" +
        "   ClientLeft      =   2760\r\n" +
        "   ClientTop       =   3750\r\n" +
        "   ClientWidth     =   6030\r\n" +
        "   Height          =   3600\r\n" +
        "   Left            =   2700\r\n" +
        "   Top             =   3405\r\n" +
        "   Width           =   6150\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Dialog\"\r\n";

    [Fact]
    public void AFormThatDeclaredNoOuterRectangle_IsNotGivenOne()
    {
        // The defect this guards: WriteAllProperties emitted the root's Left/Top/Width/Height from the
        // same numbers WriteFormMeasurements had already emitted as Client*, so a saved Form1.frm claimed
        // an outer window exactly the size of its client area — a form with no frame at all.
        var properties = RootProperties(Save(Load(ClientRectOnly)));

        properties.Should().ContainKeys("ClientLeft", "ClientTop", "ClientWidth", "ClientHeight");
        properties.Should().NotContainKeys("Left", "Top", "Width", "Height");
    }

    [Fact]
    public void AFormThatDeclaredBothRectangles_KeepsBoth_AndTheFrameBetweenThem()
    {
        var properties = RootProperties(Save(Load(BothRectangles)));

        properties["ClientLeft"].Should().Be("2760");
        properties["ClientTop"].Should().Be("3750");
        properties["ClientWidth"].Should().Be("6030");
        properties["ClientHeight"].Should().Be("3195");

        // The frame, not a copy of the client rectangle: 60 twips of border each side, 345 of
        // caption-plus-top-border against 60 of bottom border.
        properties["Left"].Should().Be("2700");
        properties["Top"].Should().Be("3405");
        properties["Width"].Should().Be("6150");
        properties["Height"].Should().Be("3600");
    }

    [Fact]
    public void TheModelHoldsTheClientRectangle_EvenWhenTheFileDeclaredBoth()
    {
        // Both rectangles used to set the same four properties, so the model ended up holding whichever
        // the file wrote last. VB6 writes alphabetically, which puts Client* first — so a form declaring
        // both was sized to its OUTER rectangle while every other form was sized to its client one, and
        // the controls on it were laid out inside the wrong box.
        var root = Load(BothRectangles).Components.Single(c => c.BaseClass is FormComponentClass);

        root.GetPropertyOrDefault(WidthProperty).Should().Be(6030 / 15.0);
        root.GetPropertyOrDefault(HeightProperty).Should().Be(3195 / 15.0);
        root.GetPropertyOrDefault(LeftProperty).Should().Be(2760 / 15.0);
        root.GetPropertyOrDefault(TopProperty).Should().Be(3750 / 15.0);
    }

    [Fact]
    public void ResizingTheClientRectangle_MovesTheOuterOneWithIt()
    {
        // The offset is preserved rather than the absolute numbers, so a resize in the designer keeps the
        // frame the author saved instead of leaving a stale outer rectangle behind. Sizing the form 15
        // twips (1 px) wider must widen the outer rectangle by the same 15 and no more.
        var form = Load(BothRectangles);
        var root = form.Components.Single(c => c.BaseClass is FormComponentClass);
        root.SetProperty(WidthProperty, root.GetPropertyOrDefault(WidthProperty) + 1);

        var properties = RootProperties(Save(form));

        properties["ClientWidth"].Should().Be("6045");
        properties["Width"].Should().Be("6165");
    }

    [Fact]
    public void AFormWithNoOuterRectangle_StillSizesItsControlsAgainstTheClientOne()
    {
        // A control is written with its own Left/Top/Width/Height — the skip added for the root must not
        // reach the children it contains.
        var frm =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   ClientWidth     =   6972\r\n" +
            "   Begin VB.CommandButton Command1 \r\n" +
            "      Height          =   375\r\n" +
            "      Left            =   240\r\n" +
            "      Top             =   120\r\n" +
            "      Width           =   1215\r\n" +
            "   End\r\n" +
            "End\r\n" +
            "Attribute VB_Name = \"Form1\"\r\n";

        var saved = Save(Load(frm));

        saved.Should().Contain("      Left =   240");
        saved.Should().Contain("      Width =   1215");
    }
}
