using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

public class SerializationRoundTripTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    // Use an OS-native temp directory so Path.GetDirectoryName / Path.GetRelativePath behave on every
    // platform. Hardcoded Windows paths (C:\test\...) are not parsed as paths on Linux — '\' is an
    // ordinary filename char there, so GetDirectoryName returns "" and GetRelativePath throws.
    private static readonly string TestDir = Path.Combine(Path.GetTempPath(), "hexide-test");

    private static ProjectDefinition MakeProject(string name = "TestProject")
    {
        var p = new ProjectDefinition(VBProjectType.EXE, name);
        p.AbsolutePath = Path.Combine(TestDir, name + ".vbp");
        return p;
    }

    private static ModuleDefinition AddModule(ProjectDefinition project, string name, ModuleKind kind)
    {
        var m = new ModuleDefinition(project, name, kind);
        m.AbsolutePath = Path.Combine(TestDir, name + kind switch
        {
            ModuleKind.ClassModule  => ".cls",
            ModuleKind.UserControl  => ".ctl",
            ModuleKind.PropertyPage => ".pag",
            _                       => ".bas"
        });
        project.AddModule(m);
        return m;
    }

    private static (string vbp, SerializedProject deserialized) RoundTripProject(ProjectDefinition project)
    {
        var serializer   = new ProjectSerializer();
        var deserializer = new ProjectDeserializer();
        var vbp = serializer.Serialize(project, project.AbsolutePath!);
        var deserialized = deserializer.Deserialize(vbp, NullSink.Instance);
        return (vbp, deserialized);
    }

    private class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string error) { }
    }

    // ── ProjectSerializer: key names ─────────────────────────────────────────

    [Fact]
    public void ProjectSerializer_StandardModule_WritesModuleKey()
    {
        var p = MakeProject();
        AddModule(p, "Module1", ModuleKind.StandardModule);
        var (vbp, _) = RoundTripProject(p);
        vbp.Should().Contain("Module=Module1;");
    }

    [Fact]
    public void ProjectSerializer_ClassModule_WritesClassKey()
    {
        var p = MakeProject();
        AddModule(p, "Class1", ModuleKind.ClassModule);
        var (vbp, _) = RoundTripProject(p);
        vbp.Should().Contain("Class=Class1;");
    }

    [Fact]
    public void ProjectSerializer_UserControl_WritesUserControlKey()
    {
        var p = MakeProject();
        AddModule(p, "UserControl1", ModuleKind.UserControl);
        var (vbp, _) = RoundTripProject(p);
        vbp.Should().Contain("UserControl=UserControl1;");
        vbp.Should().NotContain("Module=UserControl1;");
    }

    [Fact]
    public void ProjectSerializer_PropertyPage_WritesPropertyPageKey()
    {
        var p = MakeProject();
        AddModule(p, "PropPage1", ModuleKind.PropertyPage);
        var (vbp, _) = RoundTripProject(p);
        vbp.Should().Contain("PropertyPage=PropPage1;");
        vbp.Should().NotContain("Module=PropPage1;");
    }

    // ── ProjectDeserializer: kind round-trips ─────────────────────────────────

    [Fact]
    public void ProjectSerializer_UserControl_RoundTripsKind()
    {
        var p = MakeProject();
        AddModule(p, "UserControl1", ModuleKind.UserControl);
        var (_, deserialized) = RoundTripProject(p);
        var (name, _, kind) = deserialized.RelativeModulePaths.Single();
        kind.Should().Be(ModuleKind.UserControl);
        name.Should().Be("UserControl1");
    }

    [Fact]
    public void ProjectSerializer_PropertyPage_RoundTripsKind()
    {
        var p = MakeProject();
        AddModule(p, "PropPage1", ModuleKind.PropertyPage);
        var (_, deserialized) = RoundTripProject(p);
        var (_, _, kind) = deserialized.RelativeModulePaths.Single();
        kind.Should().Be(ModuleKind.PropertyPage);
    }

    [Fact]
    public void ProjectSerializer_MixedModuleKinds_AllRoundTrip()
    {
        var p = MakeProject();
        AddModule(p, "Module1",      ModuleKind.StandardModule);
        AddModule(p, "Class1",       ModuleKind.ClassModule);
        AddModule(p, "UserControl1", ModuleKind.UserControl);
        AddModule(p, "PropPage1",    ModuleKind.PropertyPage);

        var (_, deserialized) = RoundTripProject(p);

        deserialized.RelativeModulePaths.Should().HaveCount(4);
        deserialized.RelativeModulePaths.Should().Contain(x => x.Name == "Module1"      && x.Kind == ModuleKind.StandardModule);
        deserialized.RelativeModulePaths.Should().Contain(x => x.Name == "Class1"       && x.Kind == ModuleKind.ClassModule);
        deserialized.RelativeModulePaths.Should().Contain(x => x.Name == "UserControl1" && x.Kind == ModuleKind.UserControl);
        deserialized.RelativeModulePaths.Should().Contain(x => x.Name == "PropPage1"    && x.Kind == ModuleKind.PropertyPage);
    }

    // ── FormSerializer: UserControl .ctl output ───────────────────────────────

    [Fact]
    public void FormSerializer_UserControl_ProducesUserControlHeader()
    {
        var p       = MakeProject();
        var formPart = new FormDefinition(p, FormComponentClass.Instance, "UserControl1");
        formPart.UpdateRootTypeName("VB.UserControl");

        var serializer = new FormSerializer();
        var (ctl, _) = serializer.Serialize(formPart, "UserControl code here", "UserControl1.ctl");

        ctl.Should().Contain("Begin VB.UserControl UserControl1");
        ctl.Should().Contain("UserControl code here");
    }

    [Fact]
    public void FormSerializer_UserControl_UsesExplicitCodeNotFormPartCode()
    {
        var p       = MakeProject();
        var formPart = new FormDefinition(p, FormComponentClass.Instance, "UserControl1");
        formPart.UpdateRootTypeName("VB.UserControl");
        formPart.UpdateCode("old FormPart code");

        var serializer = new FormSerializer();
        var (ctl, _) = serializer.Serialize(formPart, "fresh module code", "UserControl1.ctl");

        ctl.Should().Contain("fresh module code");
        ctl.Should().NotContain("old FormPart code");
    }

    // ── FormSerializer + FormDeserializer: full .ctl round-trip ──────────────

    [Fact]
    public void FormSerializer_UserControl_RoundTrips_RootTypeName()
    {
        var p       = MakeProject();
        var formPart = new FormDefinition(p, FormComponentClass.Instance, "UserControl1");
        formPart.UpdateRootTypeName("VB.UserControl");

        var serializer   = new FormSerializer();
        var deserializer = new FormDeserializer();

        var (ctl, _) = serializer.Serialize(formPart, "Attribute VB_Name = \"UserControl1\"", "UserControl1.ctl");
        var result = deserializer.Deserialize(p, ctl, NullSink.Instance);

        result.Should().NotBeNull();
        result!.RootVBTypeName.Should().Be("VB.UserControl");
    }

    [Fact]
    public void FormSerializer_UserControl_RoundTrips_ChildControl()
    {
        var p       = MakeProject();
        var formPart = new FormDefinition(p, FormComponentClass.Instance, "UserControl1");
        formPart.UpdateRootTypeName("VB.UserControl");

        var label = new ComponentInstance(LabelComponentClass.Instance, "Label1")
            .SetProperty(VBProperties.CaptionProperty, "Hello");
        formPart.UpdateComponents([formPart.Components[0], label]);

        var serializer   = new FormSerializer();
        var deserializer = new FormDeserializer();

        var (ctl, _) = serializer.Serialize(formPart, "Attribute VB_Name = \"UserControl1\"", "UserControl1.ctl");
        var result = deserializer.Deserialize(p, ctl, NullSink.Instance);

        result.Should().NotBeNull();
        result!.Components.Should().HaveCount(2);
        var deserializedLabel = result.Components.FirstOrDefault(c => c.BaseClass is LabelComponentClass);
        deserializedLabel.Should().NotBeNull();
        deserializedLabel!.GetPropertyOrDefault(VBProperties.CaptionProperty).Should().Be("Hello");
    }

    [Fact]
    public void FormSerializer_Timer_OmitsWidthHeightVisible()
    {
        // A VB6 Timer is invisible — emitting Width/Height/Visible makes vb6.exe reject the .frm
        // ("property name Width in Timer0 is invalid"). It keeps Left/Top (the designer icon position).
        var p = MakeProject();
        var form = new FormDefinition(p, FormComponentClass.Instance, "Form1");
        var timer = new ComponentInstance(TimerComponentClass.Instance, "Timer0")
            .SetProperty(VBProperties.LeftProperty, 300.0)
            .SetProperty(VBProperties.TopProperty, 300.0)
            .SetProperty(VBProperties.WidthProperty, 480.0)
            .SetProperty(VBProperties.HeightProperty, 480.0);
        form.UpdateComponents([form.Components[0], timer]);

        var (frm, _) = new FormSerializer().Serialize(form, "Form1.frm");

        var begin = frm.IndexOf("Begin VB.Timer");
        begin.Should().BeGreaterThan(-1);
        var timerBlock = frm[begin..frm.IndexOf("End", begin)];
        timerBlock.Should().Contain("Left");
        timerBlock.Should().NotContain("Width");
        timerBlock.Should().NotContain("Height");
        timerBlock.Should().NotContain("Visible");
    }

    // ── Unknown preservation: .vbp ────────────────────────────────────────────

    private static ProjectDefinition ProjectFromSerialized(SerializedProject sp)
    {
        var p = new ProjectDefinition(sp.ProjectType, sp.Name ?? "TestProject");
        p.AbsolutePath = Path.Combine(TestDir, (sp.Name ?? "TestProject") + ".vbp");
        // Mirror the real loader (ProjectService) so full open->save round-trip tests are faithful.
        foreach (var r in sp.References)
            p.AddReference(r);
        p.UnknownPreSectionLines.AddRange(sp.UnknownPreSectionLines);
        p.PreservedItemLines.AddRange(sp.PreservedItemLines);
        p.StartupFormName = sp.StartupFormName;
        p.ExtensionTail = sp.ExtensionTail;
        return p;
    }

    [Fact]
    public void ProjectDeserializer_UnknownScalarKey_IsCaptured()
    {
        var vbp = "Name=TestProject\r\nType=EXE\r\nSomeUnknownKey=SomeValue\r\nStartup=Form1\r\n";
        var deserialized = new ProjectDeserializer().Deserialize(vbp, NullSink.Instance);
        deserialized.UnknownPreSectionLines.Should().ContainSingle();
        deserialized.UnknownPreSectionLines[0].RawLine.Should().Be("SomeUnknownKey=SomeValue");
    }

    [Fact]
    public void ProjectSerializer_UnknownScalarKey_IsEmittedInOutput()
    {
        var vbp = "Name=TestProject\r\nType=EXE\r\nSomeUnknownKey=SomeValue\r\nStartup=Form1\r\n";
        var sp = new ProjectDeserializer().Deserialize(vbp, NullSink.Instance);
        var project = ProjectFromSerialized(sp);
        var output = new ProjectSerializer().Serialize(project, project.AbsolutePath!);
        output.Should().Contain("SomeUnknownKey=SomeValue");
    }

    [Fact]
    public void ProjectSerializer_UnknownScalarKey_PositionPreservedRelativeToKnownKeys()
    {
        // Unknown appears after Name+Type (position 2) but before Startup
        var vbp = "Name=TestProject\r\nType=EXE\r\nSomeUnknownKey=SomeValue\r\nStartup=Form1\r\n";
        var sp = new ProjectDeserializer().Deserialize(vbp, NullSink.Instance);
        var project = ProjectFromSerialized(sp);
        project.StartupForm = null; // ensure Startup key is written
        var output = new ProjectSerializer().Serialize(project, project.AbsolutePath!);

        var typeIdx    = output.IndexOf("Type=EXE");
        var unknownIdx = output.IndexOf("SomeUnknownKey=SomeValue");
        typeIdx.Should().BeGreaterThan(-1);
        unknownIdx.Should().BeGreaterThan(typeIdx);
    }

    [Fact]
    public void ProjectDeserializer_ExtensionSection_IsCapturedAsVerbatimTail()
    {
        var vbp = "Name=TestProject\r\nType=EXE\r\n[CodeSMART]\r\nSomeAddinSetting=Value\r\n";
        var deserialized = new ProjectDeserializer().Deserialize(vbp, NullSink.Instance);
        deserialized.ExtensionTail.Should().Contain("[CodeSMART]");
        deserialized.ExtensionTail.Should().Contain("SomeAddinSetting=Value");
    }

    [Fact]
    public void ProjectSerializer_ExtensionSection_IsAppendedLastVerbatim()
    {
        var vbp = "Name=TestProject\r\nType=EXE\r\n[CodeSMART]\r\nSomeAddinSetting=Value\r\n";
        var sp = new ProjectDeserializer().Deserialize(vbp, NullSink.Instance);
        var project = ProjectFromSerialized(sp);
        var output = new ProjectSerializer().Serialize(project, project.AbsolutePath!);

        output.Should().Contain("[CodeSMART]");
        output.Should().Contain("SomeAddinSetting=Value");
        // Extension section must appear after all known keys
        var nameIdx      = output.IndexOf("Name=TestProject");
        var extensionIdx = output.IndexOf("[CodeSMART]");
        extensionIdx.Should().BeGreaterThan(nameIdx);
    }

    [Fact]
    public void ProjectDeserializer_NoUnknowns_ExtensionTailIsNull()
    {
        var vbp = "Name=TestProject\r\nType=EXE\r\n";
        var deserialized = new ProjectDeserializer().Deserialize(vbp, NullSink.Instance);
        deserialized.UnknownPreSectionLines.Should().BeEmpty();
        deserialized.ExtensionTail.Should().BeNullOrEmpty();
    }

    // ── References: VB6 reference line format (*\G{GUID}#Ver#LCID#Path#Name) ───

    [Fact]
    public void ProjectSerializer_Reference_IncludesName()
    {
        // Regression: a name-less reference line ("...#0#") makes vb6.exe /make fail with
        // "could not be loaded" — even though the GUID resolves via the registry. The trailing
        // #Name is required; LibPath may be empty (VB6 resolves the typelib from the GUID).
        var project = MakeProject();
        project.AddReference(new VbReference(
            "{00020430-0000-0000-C000-000000000046}", "2.0", 0, null, "OLE Automation"));

        var (vbp, _) = RoundTripProject(project);

        vbp.Should().Contain("Reference=*\\G{00020430-0000-0000-C000-000000000046}#2.0#0##OLE Automation");
    }

    [Fact]
    public void ProjectSerializer_Reference_RoundTripsNameAndPath()
    {
        var project = MakeProject();
        project.AddReference(new VbReference(
            "{11111111-0000-0000-0000-000000000001}", "1.5", 0, @"C:\libs\foo.tlb", "Foo Library"));

        var (_, deserialized) = RoundTripProject(project);

        var r = deserialized.References.Single();
        r.Guid.Should().Be("{11111111-0000-0000-0000-000000000001}");
        r.Version.Should().Be("1.5");
        r.LibPath.Should().Be(@"C:\libs\foo.tlb");
        r.Name.Should().Be("Foo Library");
    }

    [Fact]
    public void ProjectDeserializer_Reference_WithPathAndName_ParsesBoth()
    {
        var vbp = "Name=TestProject\r\nType=EXE\r\n" +
            "Reference=*\\G{00020430-0000-0000-C000-000000000046}#2.0#0#C:\\Windows\\SysWOW64\\stdole2.tlb#OLE Automation\r\n";
        var deserialized = new ProjectDeserializer().Deserialize(vbp, NullSink.Instance);
        var r = deserialized.References.Single();
        r.LibPath.Should().Be(@"C:\Windows\SysWOW64\stdole2.tlb");
        r.Name.Should().Be("OLE Automation");
    }

    // ── Dependency preservation: missing deps / other machines must not corrupt the .vbp ──

    [Fact]
    public void ProjectRoundTrip_UnresolvableReference_IsPreserved()
    {
        // A reference to a dependency not installed on this machine (or a non-Windows host) must
        // survive open->save unchanged: it is carried as data and never resolved at load time.
        const string refLine =
            "Reference=*\\G{DEADBEEF-0000-0000-0000-000000000001}#1.0#0#C:\\nope\\missing.tlb#Missing Library";
        var vbp = "Name=TestProject\r\nType=EXE\r\n" + refLine + "\r\n";
        var sp = new ProjectDeserializer().Deserialize(vbp, NullSink.Instance);
        var project = ProjectFromSerialized(sp);

        var output = new ProjectSerializer().Serialize(project, project.AbsolutePath!);

        output.Should().Contain(refLine);
    }

    [Fact]
    public void ProjectRoundTrip_ObjectComponentLine_IsPreservedVerbatim()
    {
        // ActiveX component (OCX) lines (Object=...) are not modelled; they must be carried verbatim
        // as unknowns so a project depending on an uninstalled OCX is not corrupted on save.
        const string objectLine = "Object={831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0; MSCOMCTL.OCX";
        var vbp = "Name=TestProject\r\nType=EXE\r\n" + objectLine + "\r\nStartup=Form1\r\n";
        var sp = new ProjectDeserializer().Deserialize(vbp, NullSink.Instance);
        var project = ProjectFromSerialized(sp);

        var output = new ProjectSerializer().Serialize(project, project.AbsolutePath!);

        output.Should().Contain(objectLine);
    }

    [Fact]
    public void ProjectRoundTrip_UnparseableReferenceLine_IsPreservedNotDropped()
    {
        // A Reference line in an unexpected format must be preserved verbatim, never silently dropped.
        const string weird = "Reference=#unexpected-format-no-guid#";
        var vbp = "Name=TestProject\r\nType=EXE\r\n" + weird + "\r\nStartup=Form1\r\n";
        var sp = new ProjectDeserializer().Deserialize(vbp, NullSink.Instance);
        var project = ProjectFromSerialized(sp);

        var output = new ProjectSerializer().Serialize(project, project.AbsolutePath!);

        output.Should().Contain(weird);
    }

    [Fact]
    public void ProjectRoundTrip_UserDocument_SurvivesAndStillWarns()
    {
        // ActiveX Documents (.dob) are unsupported — the warning intent must still be recorded — but the
        // line must survive the round-trip rather than being silently dropped.
        const string udLine = "UserDocument=Document1; src\\Document1.dob";
        var vbp = "Name=TestProject\r\nType=EXE\r\n" + udLine + "\r\nStartup=Form1\r\n";
        var sp = new ProjectDeserializer().Deserialize(vbp, NullSink.Instance);

        sp.SkippedUserDocumentPaths.Should().ContainSingle(); // unsupported-warning intent preserved

        var project = ProjectFromSerialized(sp);
        var output = new ProjectSerializer().Serialize(project, project.AbsolutePath!);
        output.Should().Contain(udLine);                      // ...and the entry survives
    }

    [Fact]
    public void ProjectSerializer_PreservedItemLines_AreEmittedVerbatim()
    {
        // The loader records missing-file forms (and the deserializer records UserDocuments) as preserved
        // item lines; the serializer must re-emit them so missing nodes are never dropped on save.
        var project = MakeProject();
        project.PreservedItemLines.Add("Form=MissingForm.frm");
        project.PreservedItemLines.Add("UserDocument=Doc1; Doc1.dob");

        var output = new ProjectSerializer().Serialize(project, project.AbsolutePath!);

        output.Should().Contain("Form=MissingForm.frm");
        output.Should().Contain("UserDocument=Doc1; Doc1.dob");
    }

    [Fact]
    public void ProjectSerializer_StartupName_SurvivesWhenStartupFormMissing()
    {
        // If the startup form's .frm was missing (no FormDefinition created), Startup= must still
        // round-trip via the retained StartupFormName.
        var project = MakeProject();
        project.StartupForm.Should().BeNull();
        project.StartupFormName = "MissingForm";

        var output = new ProjectSerializer().Serialize(project, project.AbsolutePath!);

        output.Should().Contain("Startup=MissingForm");
    }

    [Fact]
    public void ProjectSerializer_UnknownLines_InterleaveByPositionEvenIfAppendedOutOfOrder()
    {
        // PositionHint interleaving must be robust to unknowns appended out of order (e.g. by the loader):
        // nothing lost, and lower hints emitted before higher ones.
        var project = MakeProject();
        project.UnknownPreSectionLines.Add((5, "LateKey=late"));    // higher hint added first
        project.UnknownPreSectionLines.Add((1, "EarlyKey=early"));  // lower hint added second

        var output = new ProjectSerializer().Serialize(project, project.AbsolutePath!);

        output.Should().Contain("EarlyKey=early");
        output.Should().Contain("LateKey=late");
        output.IndexOf("EarlyKey=early").Should().BeLessThan(output.IndexOf("LateKey=late"));
    }

    // ── Unknown preservation: .frm component properties ───────────────────────

    private const string FormWithUnknownProperty = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           UnknownProp     =   42
           Height          =   3600
        End
        Attribute VB_Name = "Form1"
        """;

    [Fact]
    public void FormDeserializer_UnknownScalarProperty_IsCapturedOnComponentInstance()
    {
        var p = MakeProject();
        var form = new FormDeserializer().Deserialize(p, FormWithUnknownProperty, NullSink.Instance);
        form.Should().NotBeNull();
        var formComp = form!.Components.Single(c => c.BaseClass is FormComponentClass);
        formComp.UnknownRawPropertyLines.Should().ContainSingle(l => l.Contains("UnknownProp"));
    }

    [Fact]
    public void FormSerializer_UnknownScalarProperty_IsEmittedInOutput()
    {
        var p = MakeProject();
        var form = new FormDeserializer().Deserialize(p, FormWithUnknownProperty, NullSink.Instance)!;
        var (frm, _) = new FormSerializer().Serialize(form, "Form1.frm");
        frm.Should().Contain("UnknownProp");
    }

    private const string FormWithUnknownBlockProperty = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           BeginProperty UnknownBlock
              InnerKey     =   "InnerValue"
           EndProperty
        End
        Attribute VB_Name = "Form1"
        """;

    [Fact]
    public void FormDeserializer_UnknownBlockProperty_IsCapturedOnComponentInstance()
    {
        var p = MakeProject();
        var form = new FormDeserializer().Deserialize(p, FormWithUnknownBlockProperty, NullSink.Instance);
        form.Should().NotBeNull();
        var formComp = form!.Components.Single(c => c.BaseClass is FormComponentClass);
        formComp.UnknownRawPropertyLines.Should().Contain(l => l.Contains("BeginProperty UnknownBlock"));
        formComp.UnknownRawPropertyLines.Should().Contain(l => l.Contains("InnerKey"));
        formComp.UnknownRawPropertyLines.Should().Contain(l => l.Contains("EndProperty"));
    }

    [Fact]
    public void FormSerializer_UnknownBlockProperty_IsEmittedInOutput()
    {
        var p = MakeProject();
        var form = new FormDeserializer().Deserialize(p, FormWithUnknownBlockProperty, NullSink.Instance)!;
        var (frm, _) = new FormSerializer().Serialize(form, "Form1.frm");
        frm.Should().Contain("BeginProperty UnknownBlock");
        frm.Should().Contain("InnerKey");
        frm.Should().Contain("EndProperty");
    }

    // ── Unknown preservation: .frm unknown component subtrees ─────────────────

    private const string FormWithUnknownSubtree = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           Begin VB.OLEContainer OLE1
              OcxProp      =   123
           End
        End
        Attribute VB_Name = "Form1"
        """;

    [Fact]
    public void FormDeserializer_UnknownSubtree_IsCapturedOnFormDefinition()
    {
        var p = MakeProject();
        var form = new FormDeserializer().Deserialize(p, FormWithUnknownSubtree, NullSink.Instance);
        form.Should().NotBeNull();
        form!.UnknownChildSubtreeTexts.Should().ContainSingle();
        form.UnknownChildSubtreeTexts[0].Should().Contain("VB.OLEContainer");
        form.UnknownChildSubtreeTexts[0].Should().Contain("OcxProp");
    }

    [Fact]
    public void FormSerializer_UnknownSubtree_IsEmittedInsideFormBlock()
    {
        var p = MakeProject();
        var form = new FormDeserializer().Deserialize(p, FormWithUnknownSubtree, NullSink.Instance)!;
        var (frm, _) = new FormSerializer().Serialize(form, "Form1.frm");
        frm.Should().Contain("VB.OLEContainer");
        frm.Should().Contain("OcxProp");
        // Must appear inside the outer Begin...End block
        var beginFormIdx    = frm.IndexOf("Begin VB.Form");
        var oleIdx          = frm.IndexOf("VB.OLEContainer");
        var outerEndIdx     = frm.LastIndexOf("\r\nEnd\r\n");
        oleIdx.Should().BeGreaterThan(beginFormIdx);
        oleIdx.Should().BeLessThan(outerEndIdx);
    }

    // ── Unknown preservation: binary-referencing properties are excluded ───────

    private const string FormWithFrxReference = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           Picture         =   "Form1.frx":0000
        End
        Attribute VB_Name = "Form1"
        """;

    [Fact]
    public void FormDeserializer_BinaryReferencingUnknown_IsNotPreserved()
    {
        // Picture is a known byte[] property — it gets handled by the binary path, not unknowns.
        // An *unknown* property referencing .frx would be dropped (binary phase deferred).
        // This test verifies no crash and no binary ref in UnknownRawPropertyLines.
        var p = MakeProject();
        var form = new FormDeserializer().Deserialize(p, FormWithFrxReference, NullSink.Instance);
        form.Should().NotBeNull();
        var formComp = form!.Components.Single(c => c.BaseClass is FormComponentClass);
        formComp.UnknownRawPropertyLines.Should().NotContain(l => l.Contains(".frx"));
    }

    // ── Unknown preservation: .vbg group files ────────────────────────────────

    [Fact]
    public void GroupDeserializer_UnknownLine_IsCaptured()
    {
        var vbg = "VBGROUP 5.00\r\nProject=Project1.vbp\r\nSomeExtensionKey=SomeValue\r\n";
        var sg = new GroupDeserializer().Deserialize(vbg);
        sg.UnknownLines.Should().ContainSingle(l => l.Contains("SomeExtensionKey=SomeValue"));
    }

    [Fact]
    public void GroupSerializer_UnknownLines_AreAppendedToOutput()
    {
        var vbg = "VBGROUP 5.00\r\nProject=Project1.vbp\r\nSomeExtensionKey=SomeValue\r\n";
        var sg = new GroupDeserializer().Deserialize(vbg);

        var projects = new List<ProjectDefinition>();
        var output = new GroupSerializer().Serialize(@"C:\test\Group1.vbg", projects, null, sg.UnknownLines);

        output.Should().Contain("SomeExtensionKey=SomeValue");
    }

    // ── Subdirectory member paths (project-explorer spec Phase 1) ────────────
    // Platform-neutral absolute paths: Path.GetRelativePath is platform-semantic, and CI runs
    // on Linux, so these tests build paths from a temp root instead of C:\.

    private static ProjectDefinition MakeProjectAt(string root, string name = "TestProject")
    {
        var p = new ProjectDefinition(VBProjectType.EXE, name);
        p.AbsolutePath = System.IO.Path.Combine(root, name + ".vbp");
        return p;
    }

    private static string TestRoot() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hexide-vbp-tests");

    [Fact]
    public void ProjectSerializer_FormInSubdirectory_WritesRelativePath()
    {
        var root = TestRoot();
        var p = MakeProjectAt(root);
        var form = new FormDefinition(p, FormComponentClass.Instance, "Main");
        form.AbsolutePath = System.IO.Path.Combine(root, "Forms", "Main.frm");
        p.AddForm(form);

        var (vbp, _) = RoundTripProject(p);

        vbp.Should().Contain($"Form={System.IO.Path.Combine("Forms", "Main.frm")}");
    }

    [Fact]
    public void ProjectSerializer_ModuleInNestedSubdirectory_WritesRelativePath()
    {
        var root = TestRoot();
        var p = MakeProjectAt(root);
        var m = new ModuleDefinition(p, "Helpers", ModuleKind.StandardModule);
        m.AbsolutePath = System.IO.Path.Combine(root, "Modules", "ui", "Helpers.bas");
        p.AddModule(m);

        var (vbp, _) = RoundTripProject(p);

        vbp.Should().Contain($"Module=Helpers; {System.IO.Path.Combine("Modules", "ui", "Helpers.bas")}");
    }

    [Fact]
    public void RoundTrip_SubdirectoryPaths_PreservedVerbatim()
    {
        var root = TestRoot();
        var p = MakeProjectAt(root);
        var form = new FormDefinition(p, FormComponentClass.Instance, "Main");
        form.AbsolutePath = System.IO.Path.Combine(root, "Forms", "Main.frm");
        p.AddForm(form);
        var m = new ModuleDefinition(p, "Util", ModuleKind.ClassModule);
        m.AbsolutePath = System.IO.Path.Combine(root, "src", "Util.cls");
        p.AddModule(m);

        var (_, deserialized) = RoundTripProject(p);

        deserialized.RelativeFormPaths.Should().ContainSingle()
            .Which.Should().Be(System.IO.Path.Combine("Forms", "Main.frm"));
        deserialized.RelativeModulePaths.Should().ContainSingle()
            .Which.Path.Should().Be(System.IO.Path.Combine("src", "Util.cls"));
    }

    [Fact]
    public void ProjectDeserializer_SubdirectoryAndTraversalPaths_KeptVerbatim()
    {
        // Entry paths are stored exactly as written — no normalization on read. (VB6 only ever
        // wrote backslashes; resave normalizes to the platform separator, which is acceptable.)
        var vbp = "Type=Exe\r\nForm=Forms\\Main.frm\r\nForm=Forms/ui/About.frm\r\nModule=Shared; ..\\Common\\Shared.bas\r\n";

        var sp = new ProjectDeserializer().Deserialize(vbp, NullSink.Instance);

        sp.RelativeFormPaths.Should().ContainInOrder(@"Forms\Main.frm", "Forms/ui/About.frm");
        sp.RelativeModulePaths.Should().ContainSingle()
            .Which.Path.Should().Be(@"..\Common\Shared.bas");
    }
}
