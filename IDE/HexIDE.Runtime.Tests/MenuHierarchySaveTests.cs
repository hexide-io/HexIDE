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
    public void AFormWithAPopulatedContainer_IsNoLongerHeldReadOnly()
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

        // Successor to AFormWithAPopulatedContainer_IsStillHeldReadOnly. #84 is fixed: the gate was the only
        // thing standing between this form and a save that re-parented the button onto the form, and there is
        // now nothing for it to stand in the way of.
        form.CanSaveFaithfully.Should().BeTrue();
        Save(form).Should().Contain("   Begin VB.Frame Frame1")
                  .And.Contain("      Begin VB.CommandButton Command1");
    }

    [Fact]
    public void AControlNestedUnderANonContainer_IsStillHeldReadOnly()
    {
        // What the gate is FOR now. The format permits writing a control inside a ListBox and VB6 loads such
        // a file without complaint, so it is corrupt input rather than an exotic container — HexIDE has
        // nowhere to host it, records no containment link for it, and a save would re-parent it onto the form
        // still carrying its container-relative coordinates.
        const string listForm =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Begin VB.ListBox List1 \r\n" +
            "      Begin VB.CommandButton Command1 \r\n" +
            "      End\r\n" +
            "   End\r\n" +
            "End\r\n" +
            "Attribute VB_Name = \"Form1\"\r\n";

        var form = Load(listForm);

        form.CanSaveFaithfully.Should().BeFalse();
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.NestedContainers);
    }

    [Fact]
    public void AFormMixingMenusAndAPopulatedContainer_RoundTripsBothTrees()
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

        // Successor to AFormMixingMenusAndAPopulatedContainer_IsStillHeldReadOnly. The two trees are separate
        // mechanisms — a menu nests through SubItems, a control through the containment link — and this is the
        // fixture that would catch one being wired through the other. Both now come back out nested.
        form.CanSaveFaithfully.Should().BeTrue();

        // Not MenuShape here: it keeps every bare End, which is meaningless once the fixture also holds
        // controls. Both trees are checked by the nesting of their own Begin lines instead.
        var output = Save(form);
        output.Should().Contain("   Begin VB.Frame Frame1")
              .And.Contain("      Begin VB.CommandButton Command1")
              .And.Contain("   Begin VB.Menu mnuFile")
              .And.Contain("      Begin VB.Menu mnuFileNew");
    }

    [Fact]
    public void AnUnmodelledControlInsideAContainer_IsWrittenBackInsideIt()
    {
        // Successor to AnUnmodelledControlInsideAContainer_IsHeldReadOnly, which pinned the hole this shape
        // fell through: VB.Image is not modelled, so the loader preserved it as raw text and re-emitted it
        // just inside the ROOT's closing End with no memory of the Frame it came from. The image was silently
        // re-parented onto the form by a save the gate called faithful, and the phase that closed the hole
        // closed it by gating the form.
        //
        // The block is now recorded on the Frame, indented to its real depth and written back at the ordinal
        // it held, so there is nothing left to gate. No modelled sibling here on purpose: with one, the form
        // would once have been gated for that instead and this case would have stayed invisible.
        const string frm =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Begin VB.Frame Frame1 \r\n" +
            "      Begin VB.Image Image1 \r\n" +
            "      End\r\n" +
            "   End\r\n" +
            "End\r\nAttribute VB_Name = \"Form1\"\r\n";

        var form = Load(frm);

        form.CanSaveFaithfully.Should().BeTrue();

        var output = Save(form);
        var lines = output.Split(["\r\n", "\n"], StringSplitOptions.None).Select(l => l.TrimEnd()).ToList();
        var frame = lines.IndexOf("   Begin VB.Frame Frame1");
        var image = lines.IndexOf("      Begin VB.Image Image1");

        frame.Should().BeGreaterThanOrEqualTo(0);
        image.Should().BeGreaterThan(frame, "the preserved block belongs inside the Frame it was read from");
    }

    [Fact]
    public void AnUnmodelledControlDirectlyOnTheForm_IsNotHeldReadOnly()
    {
        // Depth 2 — nothing re-parents it, because the form is already where it would be written back.
        // Every unmodelled control in the VB6 corpus is this shape, which is why tightening the rule
        // above moves no corpus form.
        const string frm =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Begin VB.Image Image1 \r\n" +
            "   End\r\n" +
            "End\r\nAttribute VB_Name = \"Form1\"\r\n";

        var form = Load(frm);

        form.CanSaveFaithfully.Should().BeTrue();
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
