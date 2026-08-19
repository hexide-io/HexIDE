using System;
using System.Collections.Generic;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The .frm text format itself — the shape of a line and the order of the lines, as VB6 writes them.
///
/// These are what took the corpus from 0 of 22 VB6-authored files round-tripping to 16. Every rule was
/// measured across VB6's own Template tree rather than inferred, and every one is asserted here against a
/// fixture as well, so it still holds on CI where there is no VB6 install and the corpus lane checks
/// nothing at all.
///
/// The assertions are deliberately on the TEXT. A test that the writer was called with the right arguments
/// is exactly the kind that passed while every file in the corpus differed on every line.
/// </summary>
public class WriterFormatTests
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

    private static string[] Lines(string frm) => frm.Split("\r\n");

    /// <summary>A designer file around the given root-level body.</summary>
    private static string Frm(string body) =>
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   ClientWidth     =   4800\r\n" +
        body +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    [Fact]
    public void EveryBeginLineEndsWithOneSpace_AndNoEndLineDoes()
    {
        var saved = Save(Load(Frm(
            "   Begin VB.CommandButton Command1 \r\n" +
            "      Caption         =   \"OK\"\r\n" +
            "   End\r\n")));

        var lines = Lines(saved);
        lines.Where(l => l.TrimStart().StartsWith("Begin "))
             .Should().NotBeEmpty()
             .And.OnlyContain(l => l.EndsWith(" ") && !l.EndsWith("  "));
        lines.Where(l => l.Trim() == "End")
             .Should().NotBeEmpty()
             .And.OnlyContain(l => l == l.TrimEnd());
    }

    [Fact]
    public void APropertyNameSitsInASixteenCharacterField()
    {
        var saved = Save(Load(Frm("   Caption         =   \"Hi\"\r\n")));

        // "Caption" is seven characters, so nine spaces bring the = to column sixteen.
        saved.Should().Contain("   Caption         =   \"Hi\"");

        foreach (var line in Lines(saved).Where(l => l.StartsWith("   ") && l.Contains('=')))
            line.TrimStart().IndexOf('=').Should().Be(16, "every designer line puts = at column sixteen");
    }

    [Fact]
    public void PropertiesComeOutInNameOrder_WhateverOrderTheWriterProducedThem()
    {
        // Deliberately scrambled going in, and mixing all three sources the root draws from: a modelled
        // property (Caption), the form measurements (Client*/Scale*), and one HexIDE does not model
        // (LinkTopic), which used to be appended after everything else rather than sorted among them.
        var saved = Save(Load(
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   ClientWidth     =   4800\r\n" +
            "   LinkTopic       =   \"Form1\"\r\n" +
            "   Caption         =   \"Hi\"\r\n" +
            "   ClientHeight    =   3600\r\n" +
            "End\r\n" +
            "Attribute VB_Name = \"Form1\"\r\n"));

        var names = Lines(saved)
            .Where(l => l.StartsWith("   ") && !l.StartsWith("      ") && l.Contains('='))
            .Select(l => l.Trim().Split(' ')[0])
            .ToList();

        names.Should().Equal("Caption", "ClientHeight", "ClientWidth", "LinkTopic", "ScaleHeight", "ScaleWidth");
    }

    [Fact]
    public void AnUnmodelledBeginBlock_KeepsTheTrailingSpaceToo()
    {
        // The Begin and End of a preserved subtree are REGENERATED at the right indent rather than
        // replayed, so they are the one part of it that has to reproduce VB6's formatting itself.
        // About Dialog.frm's two VB.Line controls are the corpus case, and they were the last two
        // differing lines in that file.
        var saved = Save(Load(Frm(
            "   Begin VB.Line Line1 \r\n" +
            "      X1              =   120\r\n" +
            "   End\r\n")));

        saved.Should().Contain("   Begin VB.Line Line1 \r\n");
    }

    [Fact]
    public void ABooleanCarriesItsVb6Comment_AlignedAcrossBothValues()
    {
        var saved = Save(Load(Frm(
            "   Begin VB.CommandButton Command1 \r\n" +
            "      Enabled         =   0   'False\r\n" +
            "      Visible         =   -1  'True\r\n" +
            "   End\r\n")));

        // Four characters of value field either way, so the two comments line up under each other.
        saved.Should().Contain("      Enabled         =   0   'False");
        saved.Should().Contain("      Visible         =   -1  'True");
    }

    [Fact]
    public void AnEnumCarriesVb6sOwnNameForTheValue()
    {
        var saved = Save(Load(Frm("   StartUpPosition =   3  'Windows Default\r\n")));

        saved.Should().Contain("   StartUpPosition =   3  'Windows Default");
    }

    [Fact]
    public void AnEnumValueWithNoVb6Name_WritesABareNumber_RatherThanAWrongName()
    {
        // 9 is outside VBStartupPosition. A designer file may perfectly well contain it, and the honest
        // answer is the number on its own rather than the nearest name.
        var saved = Save(Load(Frm("   StartUpPosition =   9\r\n")));

        saved.Should().Contain("   StartUpPosition =   9\r\n");
        saved.Should().NotContain("StartUpPosition =   9  ");
    }

    [Fact]
    public void FillStyleZeroIsSolid_AsMicrosoftsOwnFileLabelsIt()
    {
        // Template\Userctls\Colorful Control.ctl writes FillStyle = 0 with the comment 'Solid, and that
        // comment is VB6's own label for the value. HexIDE had 0 as Transparent and 1 as Solid, so a
        // Shape saved solid rendered transparent and one saved transparent rendered solid.
        ((int)FillStyles.Solid).Should().Be(0);
        ((int)FillStyles.Transparent).Should().Be(1);

        var saved = Save(Load(Frm(
            "   Begin VB.Shape Shape1 \r\n" +
            "      FillStyle       =   0  'Solid\r\n" +
            "   End\r\n")));

        saved.Should().Contain("      FillStyle       =   0  'Solid");
    }
}
