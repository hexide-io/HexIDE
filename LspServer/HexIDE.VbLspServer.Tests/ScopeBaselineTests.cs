// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public sealed class ScopeBaselineTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "hexide-scope-" + Guid.NewGuid().ToString("N"));

    public ScopeBaselineTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, true);

    private static List<LspDiagnostic> Analyze(string source, IEnumerable<string>? baseline = null)
    {
        var (diagnostics, tree) = VbDiagnosticsProvider.GetDiagnosticsAndTree(source);
        diagnostics.Should().BeEmpty();
        tree.Should().NotBeNull();
        return VbScopeAnalyzer.GetOptionExplicitDiagnostics(tree!, baseline);
    }

    // #54 records these names as intrinsics. No result values or further library names are inferred.
    [Fact]
    public void Intrinsics_do_not_hide_the_real_undeclared_name()
    {
        var diagnostics = Analyze("Option Explicit\nSub Test()\n    Dim result\n    result = RGB(1, 2, 3) + Sin(1) + Cos(1) + vbpixels\n    result = misspelled\nEnd Sub\n");

        diagnostics.Should().ContainSingle().Which.Message.Should().Contain("'misspelled'");
    }

    [Fact]
    public void Designer_controls_are_known_only_in_their_own_form()
    {
        const string code = "Option Explicit\nSub Test()\n    timer0.Enabled = True\n    Label2.Caption = missing\nEnd Sub\n";
        const string header = "VERSION 5.00\nBegin VB.Form Form1\n   Caption = \"Form1\"\n   Begin VB.Timer Timer0\n      Interval = 100\n   End\n   Begin VB.Label Label2\n      Caption = \"Label2\"\n   End\nEnd\nAttribute VB_Name = \"Form1\"\n";

        Analyze(header + code).Should().ContainSingle().Which.Message.Should().Contain("'missing'");
        Analyze(code).Select(d => d.Message).Should().Contain(d => d.Contains("'timer0'"));
    }

    [Fact]
    public void Project_class_and_module_names_come_from_the_vbp_and_refresh()
    {
        var document = Path.Combine(dir, "Main.bas");
        var project = Path.Combine(dir, "Probe.vbp");
        File.WriteAllText(document, "Attribute VB_Name = \"Main\"\n");
        File.WriteAllText(project, "Module=Main; Main.bas\nClass=Class1; sub\\DifferentFile.cls\nModule=Helpers; Helpers.bas\n");
        var projects = new VbProjectArguments();
        const string code = "Option Explicit\nSub Test()\n    Dim result\n    result = class1.Value\n    result = Helpers.Value\n    result = missing\nEnd Sub\n";

        var context = projects.For(new Uri(document).AbsoluteUri);
        Analyze(code, context.BaselineNames).Should().ContainSingle().Which.Message.Should().Contain("'missing'");
        context.BaselineNames.Should().BeEquivalentTo(["Main", "Class1", "Helpers"]);

        File.WriteAllText(project, "Module=Main; Main.bas\n");
        Analyze(code, projects.ForPath(document).BaselineNames).Should().HaveCount(3);
        Analyze(code).Should().HaveCount(3);
        projects.For("untitled:Project1/Main.bas").BaselineNames.Should().BeEmpty();
        projects.ForPath(Path.Combine(dir, "Unlisted.bas")).BaselineNames.Should().BeEmpty();

        File.WriteAllText(Path.Combine(dir, "Other.vbp"), "Module=Main; Main.bas\nClass=OtherClass; Other.cls\n");
        projects.ForPath(document).BaselineNames.Should().BeEmpty("two owning projects are ambiguous");
    }
}
