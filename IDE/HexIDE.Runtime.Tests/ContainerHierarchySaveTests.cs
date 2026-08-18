using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using static HexIDE.Runtime.Components.VBProperties;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Guards the save half of issue #84. The writer walks the containment tree the loader records, so a
/// control read from inside a Frame comes back out inside that Frame instead of re-parented onto the form
/// still carrying its frame-relative coordinates.
///
/// The shape assertions keep indentation, because indentation is not taste here: VB6's own designer files
/// step three spaces per Begin level and align each End with its Begin, and
/// <see cref="RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure"/> compares against those
/// files directly where they are installed.
///
/// The refusal gate stays SHUT after this — see ContainerHierarchyLoadTests for why. The file round-trips;
/// the designer and the runtime do not yet agree that a child's coordinates are container-relative.
/// </summary>
public class ContainerHierarchySaveTests
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

    private static string NameOf(ComponentInstance c) => c.GetPropertyOrDefault(NameProperty) ?? "";

    /// <summary>The Begin/End skeleton, indentation preserved — the shape, without the properties.</summary>
    internal static List<string> BeginShape(string frm) =>
        frm.Split(["\r\n", "\n"], StringSplitOptions.None)
           .Where(l => l.TrimStart().StartsWith("Begin ") || l.Trim() == "End")
           .Select(l => l.TrimEnd())
           .ToList();

    [Fact]
    public void ANestedForm_IsWrittenNested_NotFlattened()
    {
        var output = Save(Load(ContainerHierarchyLoadTests.NestedForm));

        BeginShape(output).Should().Equal(
            "Begin VB.Form Form1",
            "   Begin VB.PictureBox picOuter",
            "      Begin VB.Frame fraInner",
            "         Begin VB.CommandButton cmdGo",
            "         End",
            "      End",
            "      Begin VB.Line Line1",
            "      End",
            "   End",
            "   Begin VB.CommandButton cmdClose",
            "   End",
            "End");
    }

    [Fact]
    public void AnUnmodelledBlock_IsWrittenInsideItsContainerAtItsOriginalPosition()
    {
        var output = Save(Load(ContainerHierarchyLoadTests.NestedForm));
        var shape = BeginShape(output);

        // Two separate claims, and the second is the one that used to fail silently. The block must land
        // inside picOuter (it was re-emitted just inside the ROOT's closing End before), and it must land
        // AFTER fraInner rather than before it, because position among siblings is z-order.
        var picture = shape.IndexOf("   Begin VB.PictureBox picOuter");
        var frame = shape.IndexOf("      Begin VB.Frame fraInner");
        var line = shape.IndexOf("      Begin VB.Line Line1");

        line.Should().BeGreaterThan(picture).And.BeGreaterThan(frame);
        shape.IndexOf("   Begin VB.CommandButton cmdClose").Should().BeGreaterThan(line);
    }

    [Fact]
    public void APreservedBlock_IsIndentedToItsRealDepth()
    {
        var output = Save(Load(ContainerHierarchyLoadTests.NestedForm));

        // The property lines inside a preserved block are the original file's lines, replayed with the
        // original file's indentation. Only Begin and End are regenerated, so a fixed indent level would
        // put the block's own frame at odds with its contents. Line1 sits at depth 3 — six spaces.
        output.Should().Contain("      Begin VB.Line Line1")
              .And.Contain("         X1              =   10");
    }

    [Fact]
    public void EachControl_IsWrittenExactlyOnce()
    {
        var output = Save(Load(ContainerHierarchyLoadTests.NestedForm));
        var lines = output.Split(["\r\n", "\n"], StringSplitOptions.None);

        foreach (var name in new[] { "picOuter", "fraInner", "cmdGo", "cmdClose", "Line1" })
        {
            var occurrences = lines.Count(l => l.TrimEnd().EndsWith(" " + name)
                                            && l.TrimStart().StartsWith("Begin "));
            occurrences.Should().Be(1,
                $"{name} is reachable both from the flat component list and from its container");
        }
    }

    [Fact]
    public void ANestedForm_SurvivesASecondRoundTrip()
    {
        // Idempotence: whatever the writer emits, the loader must read back to the same shape. This is the
        // property that stops a file drifting a little further on every save.
        var once = Save(Load(ContainerHierarchyLoadTests.NestedForm));
        var twice = Save(Load(once));

        BeginShape(twice).Should().Equal(BeginShape(once));
    }

    [Fact]
    public void AContainerThatIsAControlArrayElement_KeepsItsOwnChildren()
    {
        var output = Save(Load(ContainerHierarchyLoadTests.ControlArrayContainerForm));

        // Four sibling controls share the name picOptions in Options Dialog.frm, so containment is keyed by
        // object reference throughout. A name-keyed link would put both text boxes in the same frame.
        BeginShape(output).Should().Equal(
            "Begin VB.Form Form1",
            "   Begin VB.Frame fraStep",
            "      Begin VB.TextBox txtOne",
            "      End",
            "   End",
            "   Begin VB.Frame fraStep",
            "      Begin VB.TextBox txtTwo",
            "      End",
            "   End",
            "End");
    }

    [Fact]
    public void AContainersScaleProperties_AreWrittenBackOnce()
    {
        var output = Save(Load(ContainerHierarchyLoadTests.NestedForm));

        var lines = output.Split(["\r\n", "\n"], StringSplitOptions.None);

        // Exactly two: the container's, preserved verbatim, and the form's, regenerated from its own size.
        // Two is the number that catches both halves of the trap. One means the container's was dropped
        // again; three means Scale* was made unknown for the form as well, so the writer emitted its own
        // AND replayed the raw line beside it.
        lines.Count(l => l.Contains("ScaleHeight")).Should().Be(2);
        output.Should().Contain("      ScaleHeight     =   1940")
              .And.Contain("      ScaleWidth      =   2940");
    }

    [Fact]
    public void AFormTheDesignerBuilt_StillWritesItsControls()
    {
        // Nothing records a containment link on a designer-built form yet — that is Phase 6. The writer
        // must therefore keep driving its top level from the flat component list rather than from the
        // form's own contents, or a brand-new form saves as a valid .frm with no controls in it at all.
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        var button = new ComponentInstance(CommandButtonComponentClass.Instance, "Command1");
        form.UpdateComponents([.. form.Components, button]);

        button.Container.Should().BeNull();
        BeginShape(Save(form)).Should().Equal(
            "Begin VB.Form Form1",
            "   Begin VB.CommandButton Command1",
            "   End",
            "End");
    }

    /// <summary>
    /// Corrupts the containment tree behind the mutator's back. The mutator cannot produce either shape
    /// below, which is the point — these guard against some future path that bypasses it, and the two
    /// failure modes are different enough that catching one is not catching the other.
    /// </summary>
    private static void ForceContain(ComponentInstance container, ComponentInstance child) =>
        typeof(ComponentInstance)
            .GetField("containedControls", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(container).As<List<ComponentInstance>>()
            .Add(child);

    [Fact]
    public void AControlClaimedByTwoParents_IsRefusedRatherThanWrittenTwice()
    {
        var form = Load(ContainerHierarchyLoadTests.NestedForm);
        var picture = form.Components.First(c => NameOf(c) == "picOuter");
        var button = form.Components.First(c => NameOf(c) == "cmdGo");

        // cmdGo is already inside fraInner, which is inside picOuter. Claiming it a second time makes the
        // writer reach it twice and emit the same control in two places.
        ForceContain(picture, button);

        var save = () => Save(form);

        save.Should().Throw<InvalidOperationException>().WithMessage("*more than once*");
    }

    [Fact]
    public void AControlNoParentEverWrites_IsRefusedRatherThanSilentlyDropped()
    {
        var form = Load(ContainerHierarchyLoadTests.NestedForm);
        var picture = form.Components.First(c => NameOf(c) == "picOuter");
        var frame = form.Components.First(c => NameOf(c) == "fraInner");

        // A cycle does not duplicate anything — it DELETES. picOuter is now claimed by fraInner, so the
        // root loop skips it as somebody else's child, and the only thing that would have written it is
        // inside it. The whole subtree disappears and the file still parses. A visited set alone never
        // sees this, because the offending components are never visited at all.
        ForceContain(frame, picture);

        var save = () => Save(form);

        save.Should().Throw<InvalidOperationException>()
            .WithMessage("*picOuter*").WithMessage("*silently missing*");
    }

    // The six VB6-authored designer files that nest a control inside a container. Hardcoded rather than
    // rediscovered by scanning, so a form losing its nesting is a failure and not a quietly smaller set.
    // One of them lives under Controls\, not Forms\.
    public static TheoryData<string, string> NestedTemplates() => new()
    {
        { "Forms", "Options Dialog.frm" },
        { "Forms", "Tip of the Day.frm" },
        { "Forms", "Splash Screen.frm" },
        { "Forms", "ODBC Log In.frm" },
        { "Forms", "Web Browser.frm" },
        { "Controls", "Treeview Listview Splitter.frm" },
    };

    [Theory]
    [MemberData(nameof(NestedTemplates))]
    public void RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure(string subdirectory, string fileName)
    {
        var path = TemplatePath(subdirectory, fileName);
        if (path is null)
            return;

        var original = Vb6TextFile.ReadAllText(path);
        var output = Save(Load(original));

        // Microsoft wrote the input, so any difference in the Begin skeleton is HexIDE's defect. Only the
        // skeleton: property formatting and property order are separate open round-trip defects, and
        // pinning whole files here would make this test fail for reasons that have nothing to do with #84.
        BeginShape(output).Should().Equal(BeginShape(original));
    }

    /// <summary>
    /// Locates a VB6 template, and — unlike the corpus tests' silent fallback — says so when a Windows
    /// machine that clearly has the toolchain is missing one of the files this test exists to check.
    ///
    /// A skip that cannot be distinguished from a pass is how a corpus assertion quietly stops asserting
    /// anything; CI is Linux and never has VB6, so the loud case has to be Windows. Setting VB6_TEMPLATES
    /// explicitly is taken as consent that the caller knows where their corpus is.
    /// </summary>
    private static string? TemplatePath(string subdirectory, string fileName)
    {
        var configured = Environment.GetEnvironmentVariable("VB6_TEMPLATES");
        var templates = configured ?? @"C:\Program Files (x86)\Microsoft Visual Studio\VB98\Template";
        var path = Path.Join(templates, subdirectory, fileName);
        if (File.Exists(path))
            return path;

        // No VB6 install at all, or a corpus the caller pointed us at deliberately — nothing to check.
        if (configured is not null || !Directory.Exists(templates))
            return null;

        throw new FileNotFoundException(
            $"The VB98 template tree is present at '{templates}' but '{subdirectory}\\{fileName}' is not in "
          + "it. This is one of the six files that carry container nesting, so the check that #84 stays "
          + "fixed cannot run. Point VB6_TEMPLATES at a complete copy to skip this deliberately.", path);
    }
}
