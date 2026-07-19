using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

public class PlaceholderComponentTests
{
    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "MyProject");

    private class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private static FormDefinition Deserialize(string frm,
        IReadOnlyDictionary<string, ComponentBaseClass>? extra = null)
    {
        var d = new FormDeserializer();
        var form = d.Deserialize(Project, frm, NullSink.Instance, extraComponents: extra);
        return form!;
    }

    // ── PlaceholderComponentClass unit ───────────────────────────────────────

    [Fact]
    public void ForType_Name_ReturnsShortName()
    {
        var sut = PlaceholderComponentClass.ForType("MyProject.MyControl");
        sut.Name.Should().Be("MyControl");
    }

    [Fact]
    public void ForType_Name_NoDot_ReturnsWholeName()
    {
        var sut = PlaceholderComponentClass.ForType("MyControl");
        sut.Name.Should().Be("MyControl");
    }

    [Fact]
    public void ForType_VBTypeName_ReturnsFullName()
    {
        var sut = PlaceholderComponentClass.ForType("MyProject.MyControl");
        sut.VBTypeName.Should().Be("MyProject.MyControl");
    }

    [Fact]
    public void ForType_HasNoExtendedProperties()
    {
        var sut = PlaceholderComponentClass.ForType("MyProject.MyControl");
        // Only the base properties inherited from ComponentBaseClass should be present
        sut.Properties.Should().NotContain(p => p.Name == "_ExtentX");
        sut.Properties.Should().NotContain(p => p.Name == "CustomProp");
    }

    // ── FormDeserializer integration ─────────────────────────────────────────

    private const string FrmWithKnownControl = """
        VERSION 5.00
        Begin VB.Form Form1
           Left            =   0
           Top             =   0
           Width           =   4800
           Height          =   3600
           Begin VB.Label Label1
              Caption         =   "Hello"
              Left            =   120
              Top             =   120
              Width           =   1215
              Height          =   255
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private const string FrmWithUserControl = """
        VERSION 5.00
        Begin VB.Form Form1
           Left            =   0
           Top             =   0
           Width           =   4800
           Height          =   3600
           Begin MyProject.MyControl MyCtrl1
              Left            =   120
              Top             =   240
              Width           =   2415
              Height          =   1215
              _ExtentX        =   4233
              _ExtentY        =   2143
           End
        End
        Attribute VB_Name = "Form1"
        """;

    [Fact]
    public void Deserialize_WithoutExtraComponents_UnknownTypePreservedAsRawText()
    {
        var form = Deserialize(FrmWithUserControl);

        form.Components.Should().HaveCount(1); // only the root Form component
        form.UnknownChildSubtreeTexts.Should().HaveCount(1);
        form.UnknownChildSubtreeTexts[0].Should().Contain("MyProject.MyControl");
    }

    [Fact]
    public void Deserialize_WithPlaceholderRegistry_ComponentIsAdded()
    {
        var extra = new Dictionary<string, ComponentBaseClass>(StringComparer.OrdinalIgnoreCase)
        {
            ["MyProject.MyControl"] = PlaceholderComponentClass.ForType("MyProject.MyControl")
        };

        var form = Deserialize(FrmWithUserControl, extra);

        form.UnknownChildSubtreeTexts.Should().BeEmpty();
        form.Components.Should().HaveCount(2); // root Form + MyCtrl1
    }

    [Fact]
    public void Deserialize_WithPlaceholderRegistry_BasePropertiesApplied()
    {
        var extra = new Dictionary<string, ComponentBaseClass>(StringComparer.OrdinalIgnoreCase)
        {
            ["MyProject.MyControl"] = PlaceholderComponentClass.ForType("MyProject.MyControl")
        };

        var form = Deserialize(FrmWithUserControl, extra);

        var ctrl = form.Components.First(c => c.BaseClass.VBTypeName == "MyProject.MyControl");
        ctrl.GetPropertyOrDefault(VBProperties.NameProperty).Should().Be("MyCtrl1");
        ctrl.GetPropertyOrDefault(VBProperties.LeftProperty).Should().BeApproximately(120.0 / 15, 0.01);
        ctrl.GetPropertyOrDefault(VBProperties.TopProperty).Should().BeApproximately(240.0 / 15, 0.01);
        ctrl.GetPropertyOrDefault(VBProperties.WidthProperty).Should().BeApproximately(2415.0 / 15, 0.01);
        ctrl.GetPropertyOrDefault(VBProperties.HeightProperty).Should().BeApproximately(1215.0 / 15, 0.01);
    }

    [Fact]
    public void Deserialize_WithPlaceholderRegistry_UnknownPropertiesPreservedForRoundTrip()
    {
        var extra = new Dictionary<string, ComponentBaseClass>(StringComparer.OrdinalIgnoreCase)
        {
            ["MyProject.MyControl"] = PlaceholderComponentClass.ForType("MyProject.MyControl")
        };

        var form = Deserialize(FrmWithUserControl, extra);

        var ctrl = form.Components.First(c => c.BaseClass.VBTypeName == "MyProject.MyControl");
        var unknowns = string.Join(" ", ctrl.UnknownRawPropertyLines);
        unknowns.Should().Contain("_ExtentX");
        unknowns.Should().Contain("_ExtentY");
    }

    [Fact]
    public void Deserialize_WithPlaceholderRegistry_KnownControlsUnaffected()
    {
        var extra = new Dictionary<string, ComponentBaseClass>(StringComparer.OrdinalIgnoreCase)
        {
            ["MyProject.MyControl"] = PlaceholderComponentClass.ForType("MyProject.MyControl")
        };

        var form = Deserialize(FrmWithKnownControl, extra);

        form.Components.Should().HaveCount(2); // Form + Label1
        form.UnknownChildSubtreeTexts.Should().BeEmpty();
        form.Components.Any(c => c.BaseClass is LabelComponentClass).Should().BeTrue();
    }
}
