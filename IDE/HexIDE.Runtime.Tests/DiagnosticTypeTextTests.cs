using System;
using Avalonia.Controls;
using HexIDE.Runtime.Interpreter;
using VT = HexIDE.Runtime.Interpreter.Vb6Value.ValueType;

namespace HexIDE.Runtime.Tests;

// HexIDE diagnostic formatting (#935), not a claim about VB6's unmeasured wording.
public class DiagnosticTypeTextTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("control")]
    [InlineData("class")]
    [InlineData("empty")]
    public async Task MissingMember_IdentifiesMemberWithoutReceiverType(string receiver)
    {
        if (receiver == "control")
            context.AllocVariable(rootEnv, "receiver", new Vb6Value(new Control()));
        var act = async () =>
        {
            if (receiver == "class")
                await RunClasses("Dim receiver As New Sample\nDebug.Print receiver.NoSuchMember", ("Sample", "Public Value As Long"));
            else
                await Run(receiver == "empty" ? "Dim receiver As Variant\nDebug.Print receiver.NoSuchMember" : "Debug.Print receiver.NoSuchMember");
        };
        if (receiver == "empty")
        {
            (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Message.ReplaceLineEndings("\n")
                .Should().Be("Run-time error '424':\n\nObject required");
            return;
        }
        (await act.Should().ThrowAsync<VBMethodOrDataMemberNotFoundException>()).Which
            .Message.ReplaceLineEndings("\n").Should().Be("Compile error:\n\nMethod or data member not found (NoSuchMember)");
    }

    [Fact]
    public async Task NonObjectMethod_ReportsNumberWithoutInternalValue()
    {
        var act = async () => await Run("Dim receiver As Variant\nreceiver.NoSuchCall");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Message.ReplaceLineEndings("\n")
            .Should().Be("Run-time error '424':\n\nObject required");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAssignmentMember_IdentifiesVariableWithoutReceiverType(bool control)
    {
        if (control)
            context.AllocVariable(rootEnv, "receiver", new Vb6Value(new Control()));
        var act = async () => await Run((control ? "" : "Dim receiver As Variant\n") + "receiver.NoSuchMember = 1");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Message.ReplaceLineEndings("\n")
            .Should().Be(control
                ? "Run-time error '461':\n\nMethod or data member not found\n\nVariable receiver doesn't have member NoSuchMember"
                : "Run-time error '424':\n\nObject required");
    }

    public static TheoryData<Vb6Value.ValueTypePrimitive, int> Types
    {
        get
        {
            var data = new TheoryData<Vb6Value.ValueTypePrimitive, int>();
            foreach (var primitive in Enum.GetValues<Vb6Value.ValueTypePrimitive>())
                for (int depth = 0; depth <= 2; depth++)
                    data.Add(primitive, depth);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void ValueType_PrintsInnerNameAndArraySuffix(Vb6Value.ValueTypePrimitive primitive, int depth)
    {
        var type = new VT(primitive, false);
        for (int i = 0; i < depth; i++)
            type = new VT(type, true);
        type.ToString().Should().Be(primitive + string.Concat(System.Linq.Enumerable.Repeat("()", depth)));
        type.ToString().Should().NotContain("Right(").And.NotContain("Left(");
    }
}
