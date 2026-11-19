using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class BracketedMemberTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("record![_Field]")]
    [InlineData("record![Field]")]
    public void BracketedBangMemberParsesAsDictionaryAccess(string expression)
    {
        var interpreter = new BasicInterpreter(new MockStdLib(debug, written, dialogs), context, rootEnv, "");
        var call = interpreter.ParseValueStmt(expression).Should().BeOfType<VB6Parser.VsICSContext>().Subject;
        call.implicitCallStmt_InStmt().iCS_S_VariableOrProcedureCall().dictionaryCallStmt()
            .ambiguousIdentifier().GetText().Should().Be(expression.Split('!')[1]);
    }

    [Theory]
    [InlineData("Dim [_x] As Long")]
    [InlineData("Dim x As Project.[_x]")]
    public async Task UnderscoreLedBracketedDeclarationStillFailsToParse(string declaration)
    {
        Func<Task> run = () => Run(declaration);
        await run.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*Offending symbol: _x");
    }
}
