using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

public class UserControlComponentTests
{
    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "MyProject");

    private class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private static FormDefinition Deserialize(string source,
        IReadOnlyDictionary<string, ComponentBaseClass>? extra = null)
    {
        var d = new FormDeserializer();
        return d.Deserialize(Project, source, NullSink.Instance, extraComponents: extra)!;
    }

    // ── UserControlComponentClass unit ───────────────────────────────────────

    private static FormDefinition MakeEmptyFormPart() =>
        Deserialize("""
            VERSION 5.00
            Begin VB.UserControl MyCtrl
            End
            Attribute VB_Name = "MyCtrl"
            """);

    [Fact]
    public void ForModule_Name_ReturnsShortName()
    {
        var formPart = MakeEmptyFormPart();
        var registry = new Dictionary<string, ComponentBaseClass>(StringComparer.OrdinalIgnoreCase);
        var sut = UserControlComponentClass.ForModule("MyProject.MyCtrl", formPart, registry);
        sut.Name.Should().Be("MyCtrl");
    }

    [Fact]
    public void ForModule_Name_NoDot_ReturnsWholeName()
    {
        var formPart = MakeEmptyFormPart();
        var registry = new Dictionary<string, ComponentBaseClass>(StringComparer.OrdinalIgnoreCase);
        var sut = UserControlComponentClass.ForModule("MyCtrl", formPart, registry);
        sut.Name.Should().Be("MyCtrl");
    }

    [Fact]
    public void ForModule_VBTypeName_ReturnsFullName()
    {
        var formPart = MakeEmptyFormPart();
        var registry = new Dictionary<string, ComponentBaseClass>(StringComparer.OrdinalIgnoreCase);
        var sut = UserControlComponentClass.ForModule("MyProject.MyCtrl", formPart, registry);
        sut.VBTypeName.Should().Be("MyProject.MyCtrl");
    }

    [Fact]
    public void ForModule_HasNoExtendedProperties()
    {
        var formPart = MakeEmptyFormPart();
        var registry = new Dictionary<string, ComponentBaseClass>(StringComparer.OrdinalIgnoreCase);
        var sut = UserControlComponentClass.ForModule("MyProject.MyCtrl", formPart, registry);
        sut.Properties.Should().NotContain(p => p.Name == "_ExtentX");
        sut.Properties.Should().NotContain(p => p.Name == "CustomProp");
    }

    // ── FormDeserializer integration ─────────────────────────────────────────

    private const string CtlSource = """
        VERSION 5.00
        Begin VB.UserControl MyCtrl
           Begin VB.Label Label1
              Caption         =   "Hello"
              Left            =   120
              Top             =   120
              Width           =   1215
              Height          =   255
           End
        End
        Attribute VB_Name = "MyCtrl"
        """;

    private const string FrmWithUserControl = """
        VERSION 5.00
        Begin VB.Form Form1
           Left            =   0
           Top             =   0
           Width           =   4800
           Height          =   3600
           Begin MyProject.MyCtrl MyCtrl1
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
    public void Deserialize_WithUserControlRegistry_ComponentIsAdded()
    {
        var formPart = Deserialize(CtlSource);
        var registry = new Dictionary<string, ComponentBaseClass>(StringComparer.OrdinalIgnoreCase)
        {
            ["MyProject.MyCtrl"] = UserControlComponentClass.ForModule("MyProject.MyCtrl", formPart, new Dictionary<string, ComponentBaseClass>())
        };

        var form = Deserialize(FrmWithUserControl, registry);

        form.UnknownChildSubtreeTexts.Should().BeEmpty();
        form.Components.Should().HaveCount(2); // root Form + MyCtrl1
    }

    [Fact]
    public void Deserialize_WithUserControlRegistry_BaseClassIsUserControlComponentClass()
    {
        var formPart = Deserialize(CtlSource);
        var registry = new Dictionary<string, ComponentBaseClass>(StringComparer.OrdinalIgnoreCase)
        {
            ["MyProject.MyCtrl"] = UserControlComponentClass.ForModule("MyProject.MyCtrl", formPart, new Dictionary<string, ComponentBaseClass>())
        };

        var form = Deserialize(FrmWithUserControl, registry);

        var ctrl = form.Components.First(c => c.BaseClass.VBTypeName == "MyProject.MyCtrl");
        ctrl.BaseClass.Should().BeOfType<UserControlComponentClass>();
    }

    [Fact]
    public void Deserialize_WithUserControlRegistry_BasePropertiesApplied()
    {
        var formPart = Deserialize(CtlSource);
        var registry = new Dictionary<string, ComponentBaseClass>(StringComparer.OrdinalIgnoreCase)
        {
            ["MyProject.MyCtrl"] = UserControlComponentClass.ForModule("MyProject.MyCtrl", formPart, new Dictionary<string, ComponentBaseClass>())
        };

        var form = Deserialize(FrmWithUserControl, registry);

        var ctrl = form.Components.First(c => c.BaseClass.VBTypeName == "MyProject.MyCtrl");
        ctrl.GetPropertyOrDefault(VBProperties.NameProperty).Should().Be("MyCtrl1");
        ctrl.GetPropertyOrDefault(VBProperties.LeftProperty).Should().BeApproximately(120.0 / 15, 0.01);
        ctrl.GetPropertyOrDefault(VBProperties.TopProperty).Should().BeApproximately(240.0 / 15, 0.01);
        ctrl.GetPropertyOrDefault(VBProperties.WidthProperty).Should().BeApproximately(2415.0 / 15, 0.01);
        ctrl.GetPropertyOrDefault(VBProperties.HeightProperty).Should().BeApproximately(1215.0 / 15, 0.01);
    }

    [Fact]
    public void Deserialize_WithUserControlRegistry_UnknownPropertiesPreservedForRoundTrip()
    {
        var formPart = Deserialize(CtlSource);
        var registry = new Dictionary<string, ComponentBaseClass>(StringComparer.OrdinalIgnoreCase)
        {
            ["MyProject.MyCtrl"] = UserControlComponentClass.ForModule("MyProject.MyCtrl", formPart, new Dictionary<string, ComponentBaseClass>())
        };

        var form = Deserialize(FrmWithUserControl, registry);

        var ctrl = form.Components.First(c => c.BaseClass.VBTypeName == "MyProject.MyCtrl");
        var unknowns = string.Join(" ", ctrl.UnknownRawPropertyLines);
        unknowns.Should().Contain("_ExtentX");
        unknowns.Should().Contain("_ExtentY");
    }

    [Fact]
    public void Deserialize_UserControlFormPart_ContainsChildLabel()
    {
        var formPart = Deserialize(CtlSource);

        // Root UC component + Label1
        formPart.Components.Should().HaveCount(2);
        formPart.Components.Any(c => c.BaseClass is LabelComponentClass).Should().BeTrue();
    }
}
