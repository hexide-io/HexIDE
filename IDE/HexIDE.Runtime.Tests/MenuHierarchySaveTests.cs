using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Guards the save half of issue #83. The writer walks the SubItems tree the loader records, so a menu
/// hierarchy comes back out nested instead of flattened into siblings.
///
/// Indentation is not asserted from taste: VB6's own Template\Menus forms step three spaces per level
/// and align End with its Begin, and <see cref="RoundTrip_OfVb6sOwnMenuTemplate_ReproducesTheMenuShape"/>
/// compares against those files directly where they are installed.
/// </summary>
public class MenuHierarchySaveTests
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

    /// <summary>Just the Begin/End skeleton, indentation preserved — the shape, without the properties.</summary>
    private static List<string> MenuShape(string frm) =>
        frm.Split(["\r\n", "\n"], StringSplitOptions.None)
           .Where(l => l.TrimStart().StartsWith("Begin VB.Menu") || l.Trim() == "End")
           .Select(l => l.TrimEnd())
           .ToList();

    private const string MenuForm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   Begin VB.Menu mnuFile \r\n" +
        "      Caption         =   \"&File\"\r\n" +
        "      Begin VB.Menu mnuFileNew \r\n" +
        "         Caption         =   \"&New\"\r\n" +
        "      End\r\n" +
        "      Begin VB.Menu mnuFileRecent \r\n" +
        "         Caption         =   \"&Recent\"\r\n" +
        "         Begin VB.Menu mnuFileRecentItem \r\n" +
        "            Caption         =   \"(empty)\"\r\n" +
        "         End\r\n" +
        "      End\r\n" +
        "   End\r\n" +
        "   Begin VB.Menu mnuHelp \r\n" +
        "      Caption         =   \"&Help\"\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    [Fact]
    public void NestedMenus_AreWrittenNested_NotFlattened()
    {
        var output = Save(Load(MenuForm));

        MenuShape(output).Should().Equal(
            "   Begin VB.Menu mnuFile",
            "      Begin VB.Menu mnuFileNew",
            "      End",
            "      Begin VB.Menu mnuFileRecent",
            "         Begin VB.Menu mnuFileRecentItem",
            "         End",
            "      End",
            "   End",
            "   Begin VB.Menu mnuHelp",
            "   End",
            "End");
    }

    [Fact]
    public void EachMenu_IsWrittenExactlyOnce()
    {
        var output = Save(Load(MenuForm));

        foreach (var name in new[] { "mnuFile", "mnuFileNew", "mnuFileRecent", "mnuFileRecentItem", "mnuHelp" })
        {
            // Match whole lines rather than substrings. HexIDE does not yet emit VB6's trailing space
            // after the name — a separate known difference — so allow the line with or without it, but
            // still require the name to end there so mnuFile does not also match mnuFileNew.
            var occurrences = output.Split(["\r\n", "\n"], StringSplitOptions.None)
                                    .Count(l => l.TrimEnd() == "   Begin VB.Menu " + name
                                             || l.Trim() == "Begin VB.Menu " + name);
            occurrences.Should().Be(1, $"{name} is reachable both from the flat list and from its parent");
        }
    }

    [Fact]
    public void ASavedMenuTree_SurvivesASecondRoundTrip()
    {
        // Idempotence: whatever the writer emits, the loader must read back to the same shape.
        var once = Save(Load(MenuForm));
        var twice = Save(Load(once));

        MenuShape(twice).Should().Equal(MenuShape(once));
    }

    [Fact]
    public void AMenuOnlyForm_IsNoLongerHeldReadOnly()
    {
        var form = Load(MenuForm);

        // Three Begin levels deep, all of it menus — which now round-trip, so the gate must let go.
        form.CanSaveFaithfully.Should().BeTrue();
        form.UnfaithfulSaveReason.Should().BeNull();
    }

    [Fact]
    public void AFormWithAPopulatedContainer_IsStillHeldReadOnly()
    {
        const string frameForm =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Begin VB.Frame Frame1 \r\n" +
            "      Begin VB.CommandButton Command1 \r\n" +
            "      End\r\n" +
            "   End\r\n" +
            "End\r\n" +
            "Attribute VB_Name = \"Form1\"\r\n";

        var form = Load(frameForm);

        // #84 is not fixed by this change, and the gate is the only thing standing between that form
        // and a save that re-parents the button to the form.
        form.CanSaveFaithfully.Should().BeFalse();
        form.UnfaithfulSaveReason.Should().Contain("container");
    }

    [Fact]
    public void AFormMixingMenusAndAPopulatedContainer_IsStillHeldReadOnly()
    {
        const string mixedForm =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Begin VB.Frame Frame1 \r\n" +
            "      Begin VB.CommandButton Command1 \r\n" +
            "      End\r\n" +
            "   End\r\n" +
            "   Begin VB.Menu mnuFile \r\n" +
            "      Begin VB.Menu mnuFileNew \r\n" +
            "      End\r\n" +
            "   End\r\n" +
            "End\r\n" +
            "Attribute VB_Name = \"Form1\"\r\n";

        var form = Load(mixedForm);

        // The menus being fine must not excuse the Frame — the gate narrows, it does not open.
        form.CanSaveFaithfully.Should().BeFalse();
    }

    [Theory]
    [InlineData("Edit Menu.frm")]
    [InlineData("Explorer File Menu.frm")]
    [InlineData("File Menu.frm")]
    [InlineData("Help Menu.frm")]
    [InlineData("View Menu.frm")]
    [InlineData("Window Menu.frm")]
    public void RoundTrip_OfVb6sOwnMenuTemplates_ReproducesTheMenuShape(string fileName)
    {
        var path = MenuTemplatePath(fileName);
        if (path is null)
            return; // VB6 is a Windows dev-machine oracle; CI has no install.

        var original = File.ReadAllText(path);
        var output = Save(Load(original));

        // Microsoft wrote the input, so any difference in the menu skeleton is HexIDE's defect.
        MenuShape(output).Should().Equal(MenuShape(original));
    }

    [Theory]
    [InlineData("Edit Menu.frm")]
    [InlineData("Explorer File Menu.frm")]
    [InlineData("File Menu.frm")]
    [InlineData("Help Menu.frm")]
    [InlineData("View Menu.frm")]
    [InlineData("Window Menu.frm")]
    public void Vb6sOwnMenuTemplates_AreNoLongerHeldReadOnly(string fileName)
    {
        var path = MenuTemplatePath(fileName);
        if (path is null)
            return;

        var form = Load(File.ReadAllText(path));

        // These six were half of the twelve corpus forms the refusal gate held read-only.
        form.CanSaveFaithfully.Should().BeTrue();
    }

    private static string? MenuTemplatePath(string fileName)
    {
        var templates = Environment.GetEnvironmentVariable("VB6_TEMPLATES")
                        ?? @"C:\Program Files (x86)\Microsoft Visual Studio\VB98\Template";
        var path = Path.Join(templates, "Menus", fileName);
        return File.Exists(path) ? path : null;
    }
}
