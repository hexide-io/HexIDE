using System.ComponentModel;
using HexIDE.Tools;

namespace HexIDE.Tests.Tools;

public class ProjectTreeBuilderTests
{
    // Pure string math against an absolute, platform-native root — no disk IO.
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "hexpe-builder-tests");
    private static readonly string Anchor = Path.Combine(Root, "App");

    private sealed class FakeFileNode(string name, string? absolutePath) : IProjectFileNode
    {
        public string Name { get; } = name;
        public string? AbsolutePath { get; } = absolutePath;
        public bool IsExpanded { get; set; }
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }

    private static FakeFileNode Node(string name, params string[] pathBelowAnchor) =>
        new(name, pathBelowAnchor.Length == 0 ? null : Path.Combine([Anchor, .. pathBelowAnchor]));

    // ── Flat projects ─────────────────────────────────────────────

    [Fact]
    public void FlatProject_AllMembersAtRoot_NoDirectoryNodes()
    {
        var result = ProjectTreeBuilder.BuildChildren(
            [Node("Form1", "Form1.frm"), Node("Module1", "Module1.bas")], Anchor);

        result.Should().HaveCount(2);
        result.Should().AllBeAssignableTo<IProjectFileNode>();
    }

    [Fact]
    public void RootLeaves_SortedByNameCaseInsensitive()
    {
        var result = ProjectTreeBuilder.BuildChildren(
            [Node("beta", "b.bas"), Node("Alpha", "a.frm"), Node("ALSO", "c.cls")], Anchor);

        // OrdinalIgnoreCase: "Alpha" < "ALSO" ('p' < 's'), both before "beta".
        result.Cast<IProjectFileNode>().Select(n => n.Name)
            .Should().ContainInOrder("Alpha", "ALSO", "beta");
    }

    [Fact]
    public void NullAnchor_UnsavedProject_EverythingAtRoot()
    {
        var result = ProjectTreeBuilder.BuildChildren(
            [new FakeFileNode("F", Path.Combine(Anchor, "Forms", "F.frm"))], anchorDir: null);

        result.Should().ContainSingle().Which.Should().BeAssignableTo<IProjectFileNode>();
    }

    [Fact]
    public void NullAbsolutePath_UnsavedMember_AtRoot()
    {
        var result = ProjectTreeBuilder.BuildChildren([Node("Unsaved")], Anchor);

        result.Should().ContainSingle().Which.Should().BeAssignableTo<IProjectFileNode>();
    }

    // ── Directory nesting ─────────────────────────────────────────

    [Fact]
    public void SubdirectoryMembers_NestUnderDirectoryNodes()
    {
        var result = ProjectTreeBuilder.BuildChildren(
            [
                Node("Main", "Forms", "Main.frm"),
                Node("About", "Forms", "ui", "About.frm"),
                Node("Util", "Modules", "Util.bas"),
                Node("Globals", "Globals.bas"),
            ],
            Anchor);

        // Directories first (sorted), then root leaves.
        result.Should().HaveCount(3);
        var forms = result[0].Should().BeOfType<DirectoryViewModel>().Subject;
        forms.Name.Should().Be("Forms");
        var modules = result[1].Should().BeOfType<DirectoryViewModel>().Subject;
        modules.Name.Should().Be("Modules");
        result[2].Should().BeAssignableTo<IProjectFileNode>()
            .Which.Name.Should().Be("Globals");

        // Inside Forms: the ui\ directory before the Main leaf.
        forms.Children.Should().HaveCount(2);
        var ui = forms.Children[0].Should().BeOfType<DirectoryViewModel>().Subject;
        ui.Name.Should().Be("ui");
        ui.Key.Should().Be(Path.Combine("Forms", "ui"));
        ui.Children.Should().ContainSingle()
            .Which.Should().BeAssignableTo<IProjectFileNode>()
            .Which.Name.Should().Be("About");
        forms.Children[1].Should().BeAssignableTo<IProjectFileNode>()
            .Which.Name.Should().Be("Main");

        modules.Children.Should().ContainSingle();
    }

    [Fact]
    public void NoEmptyDirectories_TreeDerivedFromMembershipOnly()
    {
        var result = ProjectTreeBuilder.BuildChildren(
            [Node("Main", "Forms", "Main.frm")], Anchor);

        var allDirs = Flatten(result).OfType<DirectoryViewModel>().ToList();
        allDirs.Should().ContainSingle();
        allDirs[0].Children.Should().NotBeEmpty();
    }

    [Fact]
    public void CaseOnlyDirectorySpellings_MergeWithFirstSeenCasing()
    {
        var result = ProjectTreeBuilder.BuildChildren(
            [
                Node("A", "FORMS", "A.frm"),
                Node("B", "Forms", "B.frm"),
            ],
            Anchor);

        var dir = result.Should().ContainSingle().Subject
            .Should().BeOfType<DirectoryViewModel>().Subject;
        dir.Name.Should().Be("FORMS");
        dir.Children.Should().HaveCount(2);
    }

    [Fact]
    public void DirectoriesSortBeforeLeaves_AndAlphabetically()
    {
        var result = ProjectTreeBuilder.BuildChildren(
            [
                Node("aaa", "aaa.bas"),
                Node("Z", "zebra", "z.bas"),
                Node("A", "apple", "a.bas"),
            ],
            Anchor);

        result.Should().HaveCount(3);
        result[0].Should().BeOfType<DirectoryViewModel>().Which.Name.Should().Be("apple");
        result[1].Should().BeOfType<DirectoryViewModel>().Which.Name.Should().Be("zebra");
        result[2].Should().BeAssignableTo<IProjectFileNode>();
    }

    // ── Outside the project cone (Phase 1 fallback: root, never dropped) ──

    [Fact]
    public void ParentTraversalMember_RendersAtRoot()
    {
        var outside = Path.Combine(Root, "Common", "Shared.bas");

        var result = ProjectTreeBuilder.BuildChildren(
            [new FakeFileNode("Shared", outside)], Anchor);

        result.Should().ContainSingle().Which.Should().BeAssignableTo<IProjectFileNode>();
    }

    [Fact]
    public void CrossDriveMember_RendersAtRoot()
    {
        if (!OperatingSystem.IsWindows())
            return; // no drive letters elsewhere

        var result = ProjectTreeBuilder.BuildChildren(
            [new FakeFileNode("Registry", @"D:\Lib\Registry.bas")], @"C:\proj\App");

        result.Should().ContainSingle().Which.Should().BeAssignableTo<IProjectFileNode>();
    }

    [Fact]
    public void ForwardSlashPath_NormalizesAndNests()
    {
        if (!OperatingSystem.IsWindows())
            return; // '/' is the canonical separator elsewhere

        var anchor = @"C:\proj\App";
        var result = ProjectTreeBuilder.BuildChildren(
            [new FakeFileNode("Main", "C:/proj/App/Forms/Main.frm")], anchor);

        result.Should().ContainSingle().Which.Should().BeOfType<DirectoryViewModel>()
            .Which.Name.Should().Be("Forms");
    }

    // ── Expansion state ───────────────────────────────────────────

    [Fact]
    public void DirectoryNodes_DefaultExpanded()
    {
        var result = ProjectTreeBuilder.BuildChildren(
            [Node("Main", "Forms", "Main.frm")], Anchor);

        result.OfType<DirectoryViewModel>().Single().IsExpanded.Should().BeTrue();
    }

    [Fact]
    public void ExpansionState_AppliedByKey_CaseInsensitive()
    {
        var expansion = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine("forms", "UI")] = false,
        };

        var result = ProjectTreeBuilder.BuildChildren(
            [Node("About", "Forms", "ui", "About.frm")], Anchor, expansion);

        var forms = result.OfType<DirectoryViewModel>().Single();
        forms.IsExpanded.Should().BeTrue();
        forms.Children.OfType<DirectoryViewModel>().Single().IsExpanded.Should().BeFalse();
    }

    private static IEnumerable<IProjectTreeElement> Flatten(IEnumerable<IProjectTreeElement> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            if (node is DirectoryViewModel dir)
                foreach (var child in Flatten(dir.Children))
                    yield return child;
        }
    }
}
