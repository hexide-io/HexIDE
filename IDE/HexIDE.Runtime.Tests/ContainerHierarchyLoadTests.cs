using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.Components;
using static HexIDE.Runtime.Components.VBProperties;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Guards the load half of issue #84 — a .frm expresses a container's contents as nested Begin blocks, and
/// HexIDE recorded nothing of that, so a save re-parented every contained control onto the form while it
/// kept its container-relative coordinates.
///
/// The link is a typed pair on ComponentInstance rather than an entry in the property bag: the designer
/// clipboard replays the property bag wholesale through SetUntypedProperty, so a containment list kept
/// there would hand a pasted Frame the original's children.
///
/// These assert the recorded tree only. ContainerHierarchySaveTests asserts what comes back out, and the
/// refusal gate stays SHUT throughout this phase — the file round-trips, but the designer and the runtime
/// still place children at face value, so opening it would turn misrendered read-only forms into
/// misrendered editable ones.
/// </summary>
public class ContainerHierarchyLoadTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = new();
        public void LogError(string error) => Errors.Add(error);
    }

    private static FormDefinition Load(string source) =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;

    private static string NameOf(ComponentInstance c) => c.GetPropertyOrDefault(NameProperty) ?? "";

    private static ComponentInstance Find(FormDefinition form, string name) =>
        form.Components.First(c => NameOf(c) == name);

    /// <summary>
    /// One fixture carrying four of the five shapes at once: a control inside a Frame, that Frame inside a
    /// PictureBox, an unmodelled child (VB.Line — there is no LineComponentClass) sitting among modelled
    /// siblings, and a control on the form for contrast. The PictureBox carries Scale* so the container's
    /// own scale is checked too — that is what gives a VB.Line child its units.
    /// </summary>
    internal const string NestedForm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   ClientHeight    =   3000\r\n" +
        "   ClientLeft      =   120\r\n" +
        "   ClientTop       =   120\r\n" +
        "   ClientWidth     =   4000\r\n" +
        "   ScaleHeight     =   3000\r\n" +
        "   ScaleWidth      =   4000\r\n" +
        "   Begin VB.PictureBox picOuter \r\n" +
        "      Height          =   2000\r\n" +
        "      Left            =   120\r\n" +
        "      ScaleHeight     =   1940\r\n" +
        "      ScaleMode       =   0  'User\r\n" +
        "      ScaleWidth      =   2940\r\n" +
        "      Top             =   120\r\n" +
        "      Width           =   3000\r\n" +
        "      Begin VB.Frame fraInner \r\n" +
        "         Caption         =   \"Inner\"\r\n" +
        "         Height          =   1000\r\n" +
        "         Left            =   60\r\n" +
        "         Top             =   60\r\n" +
        "         Width           =   1500\r\n" +
        "         Begin VB.CommandButton cmdGo \r\n" +
        "            Caption         =   \"Go\"\r\n" +
        "            Height          =   375\r\n" +
        "            Left            =   120\r\n" +
        "            Top             =   240\r\n" +
        "            Width           =   1215\r\n" +
        "         End\r\n" +
        "      End\r\n" +
        "      Begin VB.Line Line1 \r\n" +
        "         X1              =   10\r\n" +
        "         X2              =   200\r\n" +
        "         Y1              =   10\r\n" +
        "         Y2              =   200\r\n" +
        "      End\r\n" +
        "   End\r\n" +
        "   Begin VB.CommandButton cmdClose \r\n" +
        "      Caption         =   \"Close\"\r\n" +
        "      Height          =   375\r\n" +
        "      Left            =   2400\r\n" +
        "      Top             =   2400\r\n" +
        "      Width           =   1215\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    /// <summary>
    /// A container that is itself a control-array element. Not a curiosity: ODBC Log In.frm ships a
    /// Frame fraStep3 with Index = 0, and Treeview Listview Splitter.frm puts a two-element lblTitle array
    /// entirely inside one picTitles. Containment is keyed by object reference throughout for exactly this
    /// reason — Options Dialog.frm has four sibling controls all called picOptions.
    /// </summary>
    internal const string ControlArrayContainerForm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Begin VB.Frame fraStep \r\n" +
        "      Index           =   0\r\n" +
        "      Begin VB.TextBox txtOne \r\n" +
        "      End\r\n" +
        "   End\r\n" +
        "   Begin VB.Frame fraStep \r\n" +
        "      Index           =   1\r\n" +
        "      Begin VB.TextBox txtTwo \r\n" +
        "      End\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    [Fact]
    public void AControlInsideAFrame_RecordsTheContainmentLink()
    {
        var form = Load(NestedForm);

        var frame = Find(form, "fraInner");
        var button = Find(form, "cmdGo");

        button.Container.Should().BeSameAs(frame);
        frame.ContainedControls.Select(NameOf).Should().Equal("cmdGo");
    }

    [Fact]
    public void AFrameInsideAPictureBox_RecordsBothLevels()
    {
        var form = Load(NestedForm);

        var picture = Find(form, "picOuter");
        var frame = Find(form, "fraInner");

        frame.Container.Should().BeSameAs(picture);
        picture.ContainedControls.Select(NameOf).Should().Equal("fraInner");
        picture.Container.Should().BeSameAs(form.Components[0], "the form is a container too, and is Components[0]");
    }

    [Fact]
    public void AControlOnTheForm_IsContainedByTheForm()
    {
        var form = Load(NestedForm);

        // The form is a container in VB6 like any other, so "on the form" is a containment link and not
        // the absence of one. The writer relies on the distinction: a null Container means nothing has
        // recorded where this control lives, which is what a designer-built form still looks like.
        Find(form, "cmdClose").Container.Should().BeSameAs(form.Components[0]);
        form.Components[0].ContainedControls.Select(NameOf).Should().Equal("picOuter", "cmdClose");
    }

    [Fact]
    public void TheFlatComponentList_KeepsItsPreOrderDocumentOrder()
    {
        var form = Load(NestedForm);

        // About twenty production call sites read Components, and none of them should have to learn the
        // tree exists. Blob collection in particular walks this list, so dropping nested controls from it
        // would delete a nested control's Picture from the .frm with no diagnostic at all.
        form.Components.Select(NameOf).Should().Equal(
            "Form1", "picOuter", "fraInner", "cmdGo", "cmdClose");
    }

    [Fact]
    public void AnUnmodelledChild_IsRecordedOnItsContainerAtItsOrdinal()
    {
        var form = Load(NestedForm);

        var picture = Find(form, "picOuter");

        // Line1 is picOuter's second child, after fraInner. Recording the ordinal is what lets the writer
        // put it back between the right two siblings instead of at the end of the form, where it would be
        // re-parented AND re-ordered — position among siblings is z-order.
        picture.PreservedChildSubtrees.Should().ContainSingle();
        picture.PreservedChildSubtrees[0].Ordinal.Should().Be(1);
        picture.PreservedChildSubtrees[0].Text.Should().Contain("VB.Line Line1");

        form.Components[0].PreservedChildSubtrees.Should().BeEmpty(
            "the block was read from inside picOuter, and re-emitting it at form level is the re-parenting bug");
    }

    [Fact]
    public void TheFormLevelViewOfPreservedSubtrees_StillSeesEveryBlock()
    {
        var form = Load(NestedForm);

        // FormDefinition.UnknownChildSubtreeTexts is now derived from the containers rather than stored.
        // Callers outside the serializer only ever wanted the flat view, and it still means what it meant.
        form.UnknownChildSubtreeTexts.Should().ContainSingle();
        form.UnknownChildSubtreeTexts[0].Should().Contain("VB.Line Line1");
    }

    [Fact]
    public void TheFlatViewOfPreservedSubtrees_IsInDocumentOrder_NotContainerOrder()
    {
        // Two unmodelled blocks: one inside a Frame, one after that Frame on the form. Reading them back
        // per container walks the form's list first and would report them the wrong way round, which is a
        // silent reordering of exactly the thing that is only being kept because it is verbatim.
        //
        // VB.Line, not VB.Image. This used VB.Image until Image was modelled, at which point the fixture
        // stopped containing anything unmodelled and the test asserted document order over an empty list —
        // it failed loudly, which is the good outcome, but the lesson is that a fixture standing in for
        // "unmodelled" has a shelf life. VB.Line is the sibling test's choice for the same reason.
        const string frm =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Begin VB.Frame Frame1 \r\n" +
            "      Begin VB.Line lineInside \r\n" +
            "      End\r\n" +
            "   End\r\n" +
            "   Begin VB.Line lineOutside \r\n" +
            "   End\r\n" +
            "End\r\nAttribute VB_Name = \"Form1\"\r\n";

        var form = Load(frm);

        form.UnknownChildSubtreeTexts.Should().HaveCount(2);
        form.UnknownChildSubtreeTexts[0].Should().Contain("lineInside");
        form.UnknownChildSubtreeTexts[1].Should().Contain("lineOutside");
    }

    [Fact]
    public void AControlNestedUnderARegisteredComponentClass_RecordsNoLink()
    {
        // A class an add-in (or a project's own UserControl) registered is modelled enough to instantiate,
        // but HexIDE has no way to host arbitrary children inside a control it did not build. So it is not
        // a container, and a component nested under one keeps the form read-only.
        //
        // This is the case most likely to be got wrong, because "UserControl" names two unrelated things
        // here: the designer ROOT of a .ctl, which is a container, and a UserControl PLACED on a form,
        // which is not.
        const string frm =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Begin MyProject.MyControl ctlHost \r\n" +
            "      Begin VB.CommandButton Command1 \r\n" +
            "      End\r\n" +
            "   End\r\n" +
            "End\r\nAttribute VB_Name = \"Form1\"\r\n";

        var extra = new Dictionary<string, ComponentBaseClass>
        {
            ["MyProject.MyControl"] = PlaceholderComponentClass.ForType("MyProject.MyControl")
        };
        var form = new FormDeserializer()
            .Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), frm, new Sink(), extraComponents: extra)!;

        Find(form, "Command1").Container.Should().BeNull();
        Find(form, "ctlHost").ContainedControls.Should().BeEmpty();
        form.CanSaveFaithfully.Should().BeFalse();
    }

    [Fact]
    public void AComponentCannotBePlacedInsideItsOwnContents()
    {
        var form = Load(NestedForm);

        var picture = Find(form, "picOuter");
        var frame = Find(form, "fraInner");

        // The writer's recursion has no visited set of its own worth relying on: a cycle whose every member
        // is claimed by a parent disappears from the output silently, and one below an unclaimed root
        // recurses until the stack ends — a StackOverflowException .NET cannot catch. The mutator is where
        // that is stopped.
        var placeParentInsideItsChild = () => picture.SetContainer(frame);

        placeParentInsideItsChild.Should().Throw<InvalidOperationException>();
        frame.ContainedControls.Should().NotContain(picture);
    }

    [Fact]
    public void AControlHasExactlyOneContainer()
    {
        var form = Load(NestedForm);

        var picture = Find(form, "picOuter");
        var button = Find(form, "cmdGo");
        var frame = Find(form, "fraInner");

        button.SetContainer(picture);

        button.Container.Should().BeSameAs(picture);
        frame.ContainedControls.Should().BeEmpty("re-parenting has to detach as well as attach");
        picture.ContainedControls.Select(NameOf).Should().Equal("fraInner", "cmdGo");
    }

    [Fact]
    public void AContainersScaleProperties_ArePreserved()
    {
        var form = Load(NestedForm);

        // ScaleMode was already preserved verbatim while ScaleHeight/ScaleWidth were dropped as
        // "form-level metadata", so a save wrote a container declaring a user-defined scale and no scale
        // to go with it. Those names are special only on the designer root.
        var raw = string.Join("\n", Find(form, "picOuter").UnknownRawPropertyLines);
        raw.Should().Contain("ScaleHeight").And.Contain("ScaleWidth").And.Contain("ScaleMode");
    }

    [Fact]
    public void TheFormsOwnScaleProperties_AreStillNotPreservedAsRawLines()
    {
        var form = Load(NestedForm);

        // The writer regenerates the root's Scale* from its own Width/Height, so preserving them here
        // would emit each one twice.
        string.Join("\n", form.Components[0].UnknownRawPropertyLines).Should().NotContain("ScaleHeight");
    }

    [Fact]
    public void AControlNestedUnderANonContainer_RecordsNoLink()
    {
        // The .frm format permits writing this and VB6 loads it without complaint, so it is corrupt input
        // rather than an exotic container. Leaving the link unrecorded is deliberate: it is what keeps the
        // depth counter seeing the nesting, so the refusal gate still fires on it after Phase 7.
        const string frm =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Begin VB.ListBox List1 \r\n" +
            "      Begin VB.TextBox Text1 \r\n" +
            "      End\r\n" +
            "   End\r\n" +
            "End\r\nAttribute VB_Name = \"Form1\"\r\n";

        var form = Load(frm);

        Find(form, "Text1").Container.Should().BeNull();
        Find(form, "List1").ContainedControls.Should().BeEmpty();
        form.MaxUnreproducibleNestingDepth.Should().Be(3);
    }

    [Fact]
    public void AContainerThatIsAControlArrayElement_HoldsItsOwnChildren()
    {
        var form = Load(ControlArrayContainerForm);

        var frames = form.Components.Where(c => NameOf(c) == "fraStep").ToList();
        frames.Should().HaveCount(2, "a control array shares one name across every element");

        frames[0].ContainedControls.Select(NameOf).Should().Equal("txtOne");
        frames[1].ContainedControls.Select(NameOf).Should().Equal("txtTwo");
        Find(form, "txtTwo").Container.Should().BeSameAs(frames[1]);
    }

    [Fact]
    public void AMenu_IsNeverRecordedAsContainedByTheForm()
    {
        const string frm =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Begin VB.Menu mnuFile \r\n" +
            "      Begin VB.Menu mnuFileNew \r\n" +
            "      End\r\n" +
            "   End\r\n" +
            "End\r\nAttribute VB_Name = \"Form1\"\r\n";

        var form = Load(frm);

        // A menu is not drawn inside anything. Its tree is SubItems, and putting a top-level menu into the
        // form's ContainedControls would force every later walk — the writer's, the runtime's canvas
        // placement, the designer's origin walk — to special-case it straight back out.
        Find(form, "mnuFile").Container.Should().BeNull();
        form.Components[0].ContainedControls.Should().BeEmpty();
    }

    [Fact]
    public void ContainmentIsNoLongerCountedAsUnreproducible()
    {
        var form = Load(NestedForm);

        // Successor to ContainmentIsStillCountedAsUnreproducible_UntilTheDesignerAgrees. Four Begin levels
        // deep, and every layer now agrees where a child belongs: the loader records the containment, the
        // writer nests it back, the runtime hosts it on its container's own canvas, and the designer draws it
        // at the container's origin. That agreement was the condition for opening the gate.
        form.MaxUnreproducibleNestingDepth.Should().BeLessThanOrEqualTo(2);
        form.CanSaveFaithfully.Should().BeTrue();
    }

    [Fact]
    public void AControlNestedUnderANonContainer_StillHoldsTheFormReadOnly()
    {
        // The gate's remaining job. Nothing records a link for this, nothing can host it, and a save would
        // re-parent it onto the form with its container-relative coordinates intact.
        const string frm =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Begin VB.ListBox List1 \r\n" +
            "      Begin VB.TextBox Text1 \r\n" +
            "      End\r\n" +
            "   End\r\n" +
            "End\r\nAttribute VB_Name = \"Form1\"\r\n";

        var form = Load(frm);

        form.MaxUnreproducibleNestingDepth.Should().Be(3);
        form.CanSaveFaithfully.Should().BeFalse();
    }
}
