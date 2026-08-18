using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.Components;
using static HexIDE.Runtime.Components.VBProperties;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Guards the load half of issue #83 — a .frm expresses a menu as nested Begin VB.Menu blocks, and
/// HexIDE used to drop the nesting on the floor because FormDefinition.Components is flat.
///
/// The tree is recorded on the parent's SubItems, which is where the designer's menu editor already
/// keeps it, so the save path can walk the same structure. The flat list keeps every menu as well —
/// 127 non-test call sites read it, and none of them should have to learn about the tree.
///
/// The depth these tests assert on is the one the refusal gate will move to: menu nesting is
/// reproducible once the writer walks it, container nesting (#84) still is not.
/// </summary>
public class MenuHierarchyLoadTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = new();
        public void LogError(string error) => Errors.Add(error);
    }

    private static FormDefinition Load(string source) =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;

    private static string NameOf(ComponentInstance c) => c.GetPropertyOrDefault(NameProperty) ?? "";

    private static List<ComponentInstance> SubItemsOf(ComponentInstance menu) =>
        menu.GetPropertyOrDefault(MenuComponentClass.SubItemsProperty) ?? new List<ComponentInstance>();

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

    private const string FrameForm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   Begin VB.Frame Frame1 \r\n" +
        "      Caption         =   \"Frame1\"\r\n" +
        "      Begin VB.CommandButton Command1 \r\n" +
        "         Caption         =   \"Command1\"\r\n" +
        "      End\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    [Fact]
    public void NestedMenus_AreRecordedOnTheParent()
    {
        var form = Load(MenuForm);

        var file = form.Components.Single(c => NameOf(c) == "mnuFile");
        SubItemsOf(file).Select(NameOf).Should().Equal("mnuFileNew", "mnuFileRecent");
    }

    [Fact]
    public void NestedMenus_RecordEveryLevel()
    {
        var form = Load(MenuForm);

        var recent = form.Components.Single(c => NameOf(c) == "mnuFileRecent");
        SubItemsOf(recent).Select(NameOf).Should().Equal("mnuFileRecentItem");
    }

    [Fact]
    public void TopLevelMenu_HasNoParentEntry()
    {
        var form = Load(MenuForm);

        // mnuHelp is a child of the form, not of a menu, so nothing claims it as a sub-item.
        form.Components
            .Where(c => c.BaseClass is MenuComponentClass)
            .SelectMany(SubItemsOf)
            .Select(NameOf)
            .Should().NotContain("mnuHelp");
    }

    [Fact]
    public void EveryMenu_StaysInTheFlatComponentList()
    {
        var form = Load(MenuForm);

        form.Components.Select(NameOf).Should()
            .Contain(new[] { "mnuFile", "mnuFileNew", "mnuFileRecent", "mnuFileRecentItem", "mnuHelp" });
    }

    [Fact]
    public void MenuOnlyNesting_IsNotCountedAsUnreproducible()
    {
        var form = Load(MenuForm);

        // Three Begin levels deep, but all of it menu nesting the writer can walk back out.
        form.MaxUnreproducibleNestingDepth.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public void ControlInsideAContainer_IsNoLongerCountedAsUnreproducible()
    {
        var form = Load(FrameForm);

        // Successor to ControlInsideAContainer_IsStillCountedAsUnreproducible. The button is at depth 3
        // inside a Frame, and every layer now agrees where it belongs: the loader records the containment,
        // the writer nests it back, the runtime hosts it on the Frame's own canvas and the designer draws it
        // at the Frame's origin. So it no longer counts, exactly as menu nesting stopped counting in #86.
        form.MaxUnreproducibleNestingDepth.Should().BeLessThanOrEqualTo(2);
    }
}
