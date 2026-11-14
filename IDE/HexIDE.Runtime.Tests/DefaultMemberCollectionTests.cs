using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class DefaultMemberCollectionTests : BaseVBTestFixture
{
    // #174: collect the marked name and kind, without binding or executing it. Attribute placement is measured in
    // docs/vb6-fidelity-oracle/attribute-lines-inside-a-procedure.md and recorded in issue #174's corrected brief.
    [Theory]
    [InlineData("Public Function Value() As Long\nAttribute Value.VB_UserMemId = 0\nValue = 42\nEnd Function\n", PrePass.DefaultMemberKind.Function)]
    [InlineData("Public Property Get Value() As Long\nAttribute Value.VB_UserMemId = 0\nValue = 42\nEnd Property\n", PrePass.DefaultMemberKind.Property)]
    [InlineData("Public Property Let Value(ByVal n As Long)\nAttribute Value.VB_UserMemId = 0\nEnd Property\nPublic Property Get Value() As Long\nValue = 42\nEnd Property\n", PrePass.DefaultMemberKind.Property)]
    [InlineData("Public Property Set Value(ByVal n As Object)\nAttribute Value.VB_UserMemId = 0\nEnd Property\nPublic Property Get Value() As Object\nEnd Property\n", PrePass.DefaultMemberKind.Property)]
    [InlineData("Public Value As Long\nAttribute Value.VB_VarUserMemId = 0\n", PrePass.DefaultMemberKind.Variable)]
    // Preserve the existing detection of a marked Sub; this pins collection, not an unmeasured scalar result.
    [InlineData("Public Sub Value()\nAttribute Value.VB_UserMemId = 0\nEnd Sub\n", PrePass.DefaultMemberKind.Sub)]
    public void Collects_the_default_members_name_and_kind(string source, PrePass.DefaultMemberKind kind)
    {
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, "",
            classModules: [("Widget", source)]);
        vb.Modules.TryGet("Widget", out var widget).Should().BeTrue();

        widget!.PrePass.DeclaresDefaultMember.Should().BeTrue();
        widget.PrePass.DefaultMember.Should().Be(new PrePass.DefaultMemberInfo("Value", kind));
    }
}
