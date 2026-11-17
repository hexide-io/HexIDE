using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured on vb6.exe SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/4145.
public class MeasuredIntrinsicNamesTests : BaseVBTestFixture, IDisposable
{
    private readonly string path = Path.GetTempFileName();
    public void Dispose() => File.Delete(path);

    [Theory]
    [InlineData("ChrB", "CharCode", 1)]
    [InlineData("IRR", "ValueArray,Guess", 1)]
    [InlineData("MIRR", "ValueArray,FinanceRate,ReinvestRate", 3)]
    public async Task Refused_intrinsics_still_arrange_the_measured_names(string name, string names, int required)
    {
        var parameters = names.Split(',');
        var args = parameters.Select((n, i) => new CallArg(new Vb6Value(i), null, Name: n)).Reverse().ToArray();
        var arranged = await VB6BuiltIns.ArrangeIntrinsicArguments(name, args);
        arranged.Should().Equal(Enumerable.Range(0, parameters.Length).Select(i => new Vb6Value(i)));
        VB6BuiltIns.IntrinsicsTakingNamedArguments[name].Required.Should().Be(required);
    }

    [Theory]
    [InlineData("AscB(String:=\"ab\")", "AscB(\"ab\")")]
    [InlineData("CurDir(Drive:=\"\")", "CurDir(\"\")")]
    [InlineData("LeftB(Length:=2, String:=\"abcd\")", "LeftB(\"abcd\",2)")]
    [InlineData("RightB(Length:=2, String:=\"abcd\")", "RightB(\"abcd\",2)")]
    [InlineData("MidB(Length:=2, Start:=1, String:=\"abcd\")", "MidB(\"abcd\",1,2)")]
    [InlineData("Dir(Attributes:=0, PathName:=path)", "Dir(path,0)")]
    [InlineData("FileAttr(ReturnType:=1, FileNumber:=1)", "FileAttr(1,1)")]
    [InlineData("FileDateTime(PathName:=path)", "FileDateTime(path)")]
    [InlineData("FileLen(PathName:=path)", "FileLen(path)")]
    [InlineData("GetAttr(PathName:=path)", "GetAttr(path)")]
    [InlineData("FormatCurrency(GroupDigits:=0, UseParensForNegativeNumbers:=0, IncludeLeadingDigit:=-1, NumDigitsAfterDecimal:=2, Expression:=12.5)", "FormatCurrency(12.5,2,-1,0,0)")]
    [InlineData("FormatNumber(GroupDigits:=0, UseParensForNegativeNumbers:=0, IncludeLeadingDigit:=-1, NumDigitsAfterDecimal:=2, Expression:=12.5)", "FormatNumber(12.5,2,-1,0,0)")]
    [InlineData("FormatPercent(GroupDigits:=0, UseParensForNegativeNumbers:=0, IncludeLeadingDigit:=-1, NumDigitsAfterDecimal:=2, Expression:=0.5)", "FormatPercent(0.5,2,-1,0,0)")]
    [InlineData("FormatDateTime(NamedFormat:=2, Expression:=#2026-10-28#)", "FormatDateTime(#2026-10-28#,2)")]
    [InlineData("Partition(Interval:=10, Stop:=100, Start:=0, Number:=12)", "Partition(12,0,100,10)")]
    [InlineData("DDB(Factor:=1.5, Period:=2, Life:=10, Salvage:=300, Cost:=2400)", "DDB(2400,300,10,2,1.5)")]
    [InlineData("DDB(2400, 300, Life:=10, period:=1)", "DDB(2400,300,10,1)")]
    [InlineData("SLN(Life:=5, Salvage:=10, Cost:=100)", "SLN(100,10,5)")]
    [InlineData("SYD(Period:=1, Life:=5, Salvage:=10, Cost:=100)", "SYD(100,10,5,1)")]
    [InlineData("FV(Pmt:=-100, NPer:=12, Rate:=0.01)", "FV(0.01,12,-100)")]
    [InlineData("PV(Due:=0, FV:=0, Pmt:=-100, NPer:=12, Rate:=0.01)", "PV(0.01,12,-100,0,0)")]
    [InlineData("Pmt(Due:=0, FV:=0, PV:=1000, NPer:=12, Rate:=0.01)", "Pmt(0.01,12,1000,0,0)")]
    [InlineData("IPmt(Due:=0, FV:=0, PV:=1000, NPer:=12, Per:=1, Rate:=0.01)", "IPmt(0.01,1,12,1000,0,0)")]
    [InlineData("PPmt(Due:=0, FV:=0, PV:=1000, NPer:=12, Per:=1, Rate:=0.01)", "PPmt(0.01,1,12,1000,0,0)")]
    [InlineData("NPer(Due:=0, FV:=0, PV:=1000, Pmt:=-100, Rate:=0.01)", "NPer(0.01,-100,1000,0,0)")]
    [InlineData("Rate(Guess:=0.1, Due:=0, FV:=0, PV:=1000, Pmt:=-100, NPer:=12)", "Rate(12,-100,1000,0,0,0.1)")]
    [InlineData("NPV(ValueArray:=values, Rate:=0.1)", "NPV(0.1,values)")]
    [InlineData("StrPtr(Ptr:=text)", "StrPtr(text)")]
    [InlineData("VarPtr(Ptr:=text)", "VarPtr(text)")]
    [InlineData("ObjPtr(Ptr:=obj)", "ObjPtr(obj)")]
    public async Task Named_call_equals_its_positional_twin(string named, string positional)
    {
        await Run($$"""
            ChDir "{{Path.GetDirectoryName(path)}}"
            Dim path As String
            path = "{{path}}"
            Dim text As String
            text = "ab"
            Dim obj As Object
            Set obj = Nothing
            Dim values As Variant
            ReDim values(2) As Double
            values(0) = -100
            values(1) = 60
            values(2) = 60
            Open path For Output As #1
            Debug.Print {{named}}
            Debug.Print {{positional}}
            Close
            """);
        debug.Should().HaveCount(2);
        debug[0].Should().Be(debug[1]);
    }

    [Theory]
    [InlineData("LeftB(Foo:=1, Length:=2)", "Named argument not found")]
    [InlineData("ChrB(Code:=65)", "Named argument not found")]
    [InlineData("CurDir(Path:=\"\")", "Named argument not found")]
    [InlineData("Dir(Path:=\"*\")", "Named argument not found")]
    [InlineData("GetAttr(Path:=\"x\")", "Named argument not found")]
    [InlineData("StrPtr(Value:=5)", "Named argument not found")]
    [InlineData("VarPtr(Value:=5)", "Named argument not found")]
    [InlineData("StrPtr(x:=\"a\")", "Named argument not found")]
    [InlineData("FV(Rate:=0.1,NPer:=12,Pmt:=-100,Type:=1)", "Named argument not found")]
    [InlineData("NPV(Rate:=0.1,Values:=values)", "Named argument not found")]
    [InlineData("LeftB(Length:=2)", "Argument not optional")]
    [InlineData("MidB(String:=\"ab\",Length:=2)", "Argument not optional")]
    [InlineData("FV(Rate:=0.1,NPer:=12)", "Argument not optional")]
    [InlineData("MIRR(ValueArray:=values,FinanceRate:=0.1)", "Argument not optional")]
    [InlineData("Partition(Number:=1,Start:=0,Stop:=10)", "Argument not optional")]
    [InlineData("SLN(Cost:=100,Salvage:=10)", "Argument not optional")]
    [InlineData("DDB(Cost:=2400,Salvage:=300,Life:=10)", "Argument not optional")]
    [InlineData("DDB(Cost:=2400,Salvage:=300,Life:=10,Per:=1)", "Named argument not found")]
    [InlineData("InStrB(Start:=1,String1:=\"ab\",String2:=\"a\")", "Syntax error")]
    [InlineData("InStrB(String1:=\"ab\",String2:=\"a\")", "Syntax error")]
    [InlineData("CallByName(Object:=Nothing,ProcName:=\"x\",CallType:=1)", "Argument in ParamArray may not be named")]
    [InlineData("CallByName(Nothing, ProcName:=\"Item\", CallType:=1, Args:=\"ka\")", "Argument in ParamArray may not be named")]
    [InlineData("IMEStatus(x:=1)", "Wrong number of arguments or invalid property assignment")]
    public async Task Named_call_reports_measured_compile_error(string expression, string message)
    {
        Func<Task> run = () => Run("Debug.Print " + expression);
        await run.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*" + message);
    }
}
