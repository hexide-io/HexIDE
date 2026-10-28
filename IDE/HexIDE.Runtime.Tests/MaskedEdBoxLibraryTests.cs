using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Tests;

public class MaskedEdBoxLibraryTests
{
    [Fact]
    public void Library_identity_and_control_are_the_measured_ones()
    {
        ObjectReference.TryParse("Object={C932BA88-4374-101B-A56C-00AA003668DC}#1.1#0; MSMASK32.OCX", out var reference).Should().BeTrue();
        var library = ControlLibraries.For(reference);
        library.Should().NotBeNull();
        library!.Name.Should().Be("MSMask");
        library.Version.Should().Be("1.1");
        library.PersistedVersion.Should().Be(393216);
        library.Controls.Should().ContainSingle().Which.Should().Be(new System.Collections.Generic.KeyValuePair<string, int>("MaskEdBox", 1098));
        library.FormLine.Should().Be("Object = \"{C932BA88-4374-101B-A56C-00AA003668DC}#1.1#0\"; \"MSMASK32.OCX\"");
        InBoxComponentClass.IssueFor("MSMask.MaskEdBox").Should().Be(1098);
    }

    [Fact]
    public void Every_dumped_constant_resolves_bare_and_in_its_own_enum()
    {
        var library = ControlLibraries.All.SingleOrDefault(l => l.Name == "MSMask");
        library.Should().NotBeNull();
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Join(dir.FullName, "corpus", "inbox-controls"))) dir = dir.Parent;
        dir.Should().NotBeNull();
        var rows = File.ReadAllLines(Path.Join(dir!.FullName, "corpus", "inbox-controls", "typelib", "MSMASK32-constants.jsonl"))
            .Where(l => l.Length > 0).Select(l => JsonDocument.Parse(l).RootElement)
            .Where(r => r.GetProperty("kind").GetString() == "ENUM").ToList();
        rows.Should().HaveCount(66);
        foreach (var row in rows)
        {
            var container = row.GetProperty("container").GetString()!;
            var name = row.GetProperty("name").GetString()!;
            var expected = long.Parse(row.GetProperty("value").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            library!.TryBare(name, out var bare).Should().BeTrue(name);
            bare.Value.Should().Be(expected);
            library.TryInContainer(container, name, out var qualified).Should().BeTrue(name);
            qualified.Should().Be(bare);
        }
        library!.TryInContainer("ClipModeConstants", "mskCustom", out _).Should().BeFalse();
    }
}
