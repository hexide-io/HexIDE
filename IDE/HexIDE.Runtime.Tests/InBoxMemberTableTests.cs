using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The in-box control libraries and the ProgressBar's member table against the type libraries themselves (#1082):
/// corpus/inbox-controls/typelib, which scripts/dump-typelib-types.ps1 and scripts/dump-typelib-members.ps1 wrote from
/// MSCOMCTL.OCX and COMCTL32.OCX on the oracle VM. So the marks on each member are the type libraries', not typed by hand.
/// </summary>
public class InBoxMemberTableTests
{
    private static IReadOnlyList<JsonElement> Rows(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Join(dir.FullName, "corpus", "inbox-controls")))
            dir = dir.Parent;
        dir.Should().NotBeNull("corpus/inbox-controls ships with the repository");
        return File.ReadAllLines(Path.Join(dir!.FullName, "corpus", "inbox-controls", "typelib", file))
            .Where(l => l.Length > 0).Select(l => JsonDocument.Parse(l).RootElement).ToList();
    }

    public static TheoryData<string, string> Libraries => new() { { "MSCOMCTL", "MSComctlLib" }, { "COMCTL32", "ComctlLib" } };

    private static RuntimeLibrary Library(string name) => ControlLibraries.All.Single(l => l.Name == name);

    [Theory]
    [MemberData(nameof(Libraries))]
    public void A_librarys_identity_is_its_type_librarys(string file, string name)
    {
        var lib = Rows($"{file}-types.jsonl").Single(r => r.GetProperty("kind").GetString() == "LIB");

        lib.GetProperty("library").GetString().Should().Be(name);
        lib.GetProperty("guid").GetString().Should().Be(Library(name).Guid);
        lib.GetProperty("version").GetString().Should().Be(Library(name).Version);
    }

    [Theory]
    [MemberData(nameof(Libraries))]
    public void A_library_lists_every_coclass_its_type_library_flags_as_a_control(string file, string name)
    {
        var controls = Rows($"{file}-types.jsonl")
            .Where(r => r.TryGetProperty("control", out var c) && c.GetBoolean())
            .Select(r => r.GetProperty("name").GetString());

        Library(name).Controls.Keys.Should().BeEquivalentTo(controls);
    }

    [Theory]
    [MemberData(nameof(Libraries))]
    public void The_ProgressBars_members_are_its_type_librarys_with_that_librarys_mark(string file, string name)
    {
        var rows = Rows($"{file}-members.jsonl")
            .Where(r => r.TryGetProperty("class", out var c) && c.GetString() == "ProgressBar")
            .Where(r => r.GetProperty("kind").GetString() is "MEMBER" or "EVENT")
            .Where(r => !r.GetProperty("hidden").GetBoolean() && !r.GetProperty("restricted").GetBoolean())
            .ToList();
        var declared = rows
            .GroupBy(r => r.GetProperty("name").GetString()!)
            .Select(g => (g.Key, g.First().GetProperty("kind").GetString() == "EVENT" ? MemberKind.Event
                : g.Any(r => r.GetProperty("invoke").GetString() == "method") ? MemberKind.Method : MemberKind.Property));

        var library = Library(name);
        ProgressBarComponentClass.Instance.Members.Where(m => m.IsIn(library)).Select(m => (m.Name, m.Kind))
            .Should().BeEquivalentTo(declared);
    }

    // Every member the table says is written in a .frm is one the writer writes, modelled or carried, and nothing else is.
    [Fact]
    public void The_members_written_in_a_frm_are_exactly_the_ones_the_writer_writes()
    {
        var bar = ProgressBarComponentClass.Instance;

        bar.Members.Where(m => m.IsFrmProperty).Select(m => m.Name).Should().BeEquivalentTo(
            bar.OwnProperties.Select(p => p.Name).Concat(bar.CarriedOwnProperties.Select(c => c.Name)));
    }
}
