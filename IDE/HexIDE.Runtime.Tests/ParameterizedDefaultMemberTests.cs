using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class ParameterizedDefaultMemberTests : BaseVBTestFixture
{
    // vb6.exe SP6, native and p-code: #4606's supervisor comment (2026-11-18), and
    // docs/vb6-fidelity-oracle/with-new-default-instances-and-set-through-members.md (Bag/Self).
    private const string Bag = """
        Private store As New Collection
        Public Property Get Item(ByVal k As Variant) As Variant
        Attribute Item.VB_UserMemId = 0
            Item = store(CStr(k))
        End Property
        Public Property Let Item(ByVal k As Variant, ByVal v As Variant)
            On Error Resume Next
            store.Remove CStr(k)
            On Error GoTo 0
            store.Add v, CStr(k)
        End Property
        Public Property Get Self() As Bag
            Set Self = Me
        End Property

        """;
    private const string Holder = """
        Private names As Bag
        Public Sub Init()
            Set names = New Bag
        End Sub
        Public Sub PutIt(ByVal k As String, ByVal v As String)
            names(k) = v
        End Sub
        Public Function GetIt(ByVal k As String) As String
            GetIt = names(k)
        End Function
        Public Property Get State() As Bag
            Set State = names
        End Property

        """;
    private const string MakeBag = """
        Function MakeBag() As Bag
            Set MakeBag = New Bag
            MakeBag.Item("x") = "m"
        End Function

        """;
    private Task Probe(string code) => RunClasses(code + "\n", ("Bag", Bag), ("Holder", Holder),
        ("FBag", "Public Function Item(ByVal k As Variant) As Variant\nAttribute Item.VB_UserMemId = 0\nItem = \"f\" & k\nEnd Function\n"),
        ("TBag", "Public Property Get Item(ByVal a As Long, ByVal b As Long) As Long\nAttribute Item.VB_UserMemId = 0\nItem = a * 10 + b\nEnd Property\n"));

    private const string Record = "Private Type TT\n    d As Bag\nEnd Type\nDim t As TT\n";

    // Measured native and p-code in https://github.com/hexide-io/HexIDE/issues/4725.
    [Theory]
    [InlineData("t.d(\"k\") = \"v\"\nDebug.Print t.d(\"k\")", "v")]
    [InlineData("t.d(\"k\") = \"v\"\nDebug.Print TypeName(t.d(\"k\"))", "String")]
    [InlineData("t.d(\"A1\") = False\nt.d(\"A1\") = True\nDebug.Print CStr(t.d(\"A1\")) & \" \" & TypeName(t.d(\"A1\"))", "True Boolean")]
    [InlineData("t.d.Item(\"k\") = \"v\"\nDebug.Print t.d(\"k\")", "v")]
    public async Task Type_field_default_member(string code, string expected)
    {
        await Probe(Record + "Set t.d = New Bag\n" + code);
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("t.d(\"k\") = 1")]
    [InlineData("Dim x As Variant\nx = t.d(\"k\")")]
    public async Task Type_field_typed_Nothing_raises_91(string statement)
    {
        var run = () => Probe(Record + statement);
        var error = (await run.Should().ThrowAsync<VBRunTimeException>()).Which;
        error.Error.ErrNo.Should().Be(91);
    }

    // vb6.exe SP6 native/p-code measurements: hexide-io/HexIDE#4925, case 7.
    [Theory]
    [InlineData("t.o(1) = 9")]
    [InlineData("Dim x As Variant\nx = t.o(1)")]
    public async Task Type_field_As_Object_Nothing_raises_91(string statement)
    {
        var run = () => Probe("Private Type TO\n    o As Object\nEnd Type\nDim t As TO\n" + statement);
        (await run.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(91);
    }

    [Theory]
    [InlineData("Dim b As New Bag\nb(\"x\") = \"v\"\nDebug.Print b(\"x\") & \" \" & b.Self(\"x\")", "v v")]
    [InlineData("Dim v As Variant\nSet v = New Bag\nv(\"x\") = \"v\"\nDebug.Print v(\"x\")", "v")]
    [InlineData("Dim o As Object\nSet o = New Bag\no(\"x\") = \"o\"\nDebug.Print o(\"x\")", "o")]
    [InlineData("Dim h As New Holder\nh.Init\nh.PutIt \"k\", \"hv\"\nDebug.Print h.GetIt(\"k\")", "hv")]
    [InlineData("Dim h As New Holder\nh.Init\nh.PutIt \"k\", \"hv\"\nDebug.Print h.State(\"k\")", "hv")]
    [InlineData("Dim h As New Holder\nh.Init\nh.State(\"k2\") = \"w\"\nDebug.Print h.GetIt(\"k2\")", "w")]
    [InlineData("Dim f As New FBag\nDebug.Print f(\"q\")", "fq")]
    [InlineData("Dim t As New TBag\nDebug.Print CStr(t(1, 2))", "12")]
    [InlineData("Dim b As New Bag, s As String\nb(\"x\") = \"bv\"\ns = b(\"x\") & \"!\"\nDebug.Print s", "bv!")]
    [InlineData(MakeBag + "Debug.Print MakeBag()(\"x\")", "m")]
    [InlineData(MakeBag + "Debug.Print MakeBag(\"x\")", "m")]
    [InlineData("Dim v As Variant, b As New Bag\nSet v = b\nb.Item(\"x\") = \"v\"\nDebug.Print TypeName(v(\"x\")) & \" \" & TypeName(b(\"x\"))", "String String")]
    [InlineData("Dim arr(1 To 2) As Bag\nSet arr(1) = New Bag\narr(1)(\"x\") = \"a\"\nDebug.Print arr(1)(\"x\")", "a")]
    [InlineData("Dim arr(1 To 2) As Bag\nSet arr(1) = New Bag\narr(1)(\"x\") = \"a\"\nDebug.Print arr(1).Item(\"x\")", "a")]
    public async Task Measured_calls_read_and_assign_the_default_member(string code, string expected)
    {
        await Probe(code);
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("Debug.Print n(\"x\")")]
    [InlineData("n(\"x\") = \"n\"")]
    public async Task A_typed_Nothing_raises_91(string statement)
    {
        var run = () => Probe("Dim n As Bag\n" + statement);
        var error = (await run.Should().ThrowAsync<VBRunTimeException>()).Which;
        error.Error.ErrNo.Should().Be(91);
    }

    [Theory]
    [InlineData("Debug.Print b.Item(\"x\")")]
    [InlineData("Debug.Print b.Self.Item(\"x\")")]
    public async Task Explicit_members_still_work(string statement)
    {
        await Probe("Dim b As New Bag\nb.Item(\"x\") = \"v\"\n" + statement);
        AssertDebugLog([new Vb6Value("v")]);
    }

    // Measured on vb6.exe SP6, native and p-code: #4747's supervisor comment,
    // recorded by oracle PR #4779. Unmeasured cells are deliberately absent.
    private static string DefaultClass(string kind) => kind switch
    {
        "CN" => "Public Function F(ByVal i As Long)\nEnd Function\n",
        "CS" => "Public Sub Value(ByVal i As Long)\nAttribute Value.VB_UserMemId = 0\nDebug.Print \"Sub \" & i\nEnd Sub\n",
        "CV" => DefaultField("Variant", "Value = Array(10, 20, 30)"),
        "CVS" => DefaultField("Variant", "Value = 7"),
        "CVE" => DefaultField("Variant", ""),
        "CVT" => DefaultField("Long", "Value = 7"),
        _ => DefaultProcedure(kind)
    };

    private static string DefaultField(string type, string initialize) =>
        $"Public Value As {type}\nAttribute Value.VB_VarUserMemId = 0\nPrivate Sub Class_Initialize()\n{initialize}\nEnd Sub\n";

    private static string DefaultProcedure(string kind)
    {
        var procedure = kind.StartsWith("CG", StringComparison.Ordinal) ? "Property Get" : "Function";
        var end = procedure == "Property Get" ? "Property" : "Function";
        var trace = kind.EndsWith('2') ? $"Debug.Print \"{(end == "Property" ? "Get" : "Function")} \" & i\n" : "";
        return $"Public {procedure} Value(ByVal i As Long) As Variant\nAttribute Value.VB_UserMemId = 0\n{trace}Value = i * 10\nEnd {end}\n";
    }

    private Task DefaultProbe(string kind, string binding, string statement) => RunClasses(
        $"Dim e As {binding}, x As Variant\nSet e = New {kind}\nOn Error Resume Next\n{statement}\nIf Err.Number <> 0 Then Debug.Print \"err \" & Err.Number\n",
        (kind, DefaultClass(kind)));

    [Theory]
    [InlineData("CN", "CN", "x = e(1)", "err 438")]
    [InlineData("CN", "CN", "e(1) = 5", "err 438")]
    [InlineData("CN", "Object", "x = e(1)", "err 438")]
    [InlineData("CN", "Object", "e(1) = 5", "err 438")]
    [InlineData("CN", "Variant", "x = e(1)", "err 438")]
    [InlineData("CN", "Variant", "e(1) = 5", "err 438")]
    [InlineData("CS", "Object", "x = e(1)\nDebug.Print TypeName(x)", "Sub 1|Empty")]
    [InlineData("CS", "Object", "e(1) = 5", "Sub 1|err 451")]
    [InlineData("CS", "Variant", "x = e(1)\nDebug.Print TypeName(x)", "Sub 1|Empty")]
    [InlineData("CS", "Variant", "e(1) = 5", "err 450")]
    [InlineData("CV", "CV", "x = e(1)\nDebug.Print CStr(x) & \" \" & TypeName(x)", "20 Integer")]
    [InlineData("CV", "CV", "e(1) = 5\nDebug.Print CStr(e.Value(0)) & \" \" & e.Value(1) & \" \" & e.Value(2)", "10 20 30")]
    [InlineData("CV", "CV", "x = e(5)", "err 9")]
    [InlineData("CV", "CV", "e(5) = 1", "err 9")]
    [InlineData("CV", "Object", "x = e(1)", "err 451")]
    [InlineData("CV", "Variant", "x = e(1)", "err 451")]
    [InlineData("CV", "Object", "e(1) = 5", "err 451")]
    [InlineData("CV", "Variant", "e(1) = 5", "err 451")]
    [InlineData("CVS", "CVS", "x = e(1)", "err 13")]
    [InlineData("CVS", "Object", "x = e(1)", "err 451")]
    [InlineData("CVS", "Variant", "x = e(1)", "err 451")]
    [InlineData("CVS", "CVS", "e(1) = 5", "err 13")]
    [InlineData("CVS", "Object", "e(1) = 5", "err 451")]
    [InlineData("CVS", "Variant", "e(1) = 5", "err 451")]
    [InlineData("CVE", "CVE", "x = e(1)", "err 13")]
    [InlineData("CVE", "Object", "x = e(1)", "err 451")]
    [InlineData("CVE", "Variant", "x = e(1)", "err 451")]
    [InlineData("CVE", "CVE", "e(1) = 5", "err 13")]
    [InlineData("CVT", "Object", "x = e(1)", "err 451")]
    [InlineData("CVT", "Variant", "x = e(1)", "err 451")]
    [InlineData("CVT", "Object", "e(1) = 5", "err 451")]
    [InlineData("CVT", "Variant", "e(1) = 5", "err 451")]
    [InlineData("CG", "CG", "e(1) = 5", "err 424")]
    [InlineData("CG", "Object", "e(1) = 5", "err 451")]
    [InlineData("CG", "Variant", "e(1) = 5", "err 451")]
    [InlineData("CF", "CF", "e(1) = 5", "err 424")]
    [InlineData("CF", "Object", "e(1) = 5", "err 451")]
    [InlineData("CF", "Variant", "e(1) = 5", "err 450")]
    [InlineData("CG2", "CG2", "e(1) = 5", "Get 1|err 424")]
    [InlineData("CG2", "Object", "e(1) = 5", "Get 1|err 451")]
    [InlineData("CG2", "Variant", "e(1) = 5", "Get 1|err 451")]
    [InlineData("CF2", "CF2", "e(1) = 5", "Function 1|err 424")]
    [InlineData("CF2", "Object", "e(1) = 5", "Function 1|err 451")]
    [InlineData("CF2", "Variant", "e(1) = 5", "err 450")]
    public async Task Measured_argument_errors_and_side_effects(string kind, string binding, string statement, string expected)
    {
        await DefaultProbe(kind, binding, statement);
        AssertDebugLog(expected.Split('|').Select(s => new Vb6Value(s)).ToList());
    }

    [Theory]
    [InlineData("CS", "x = e(1)", "Expected Function or variable")]
    [InlineData("CS", "e(1) = 5", "Expected Function or variable")]
    [InlineData("CVT", "x = e(1)", "Wrong number of arguments or invalid property assignment")]
    [InlineData("CVT", "e(1) = 5", "Can't assign to read-only property")]
    public async Task Early_bound_invalid_members_give_compile_errors(string kind, string statement, string message)
    {
        var run = () => DefaultProbe(kind, kind, statement);
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.ReplaceLineEndings()
            .Should().Be($"Compile error:\n\n{message}".ReplaceLineEndings());
    }

    [Theory]
    [InlineData("CV", "Object", "x = e.Value(1)", "err 451")]
    [InlineData("CG2", "CG2", "x = e(2)\nDebug.Print CStr(x) & \" \" & TypeName(x)", "Get 2|20 Long")]
    public async Task Already_measured_reads_remain_regression_guards(string kind, string binding, string statement, string expected)
    {
        await DefaultProbe(kind, binding, statement);
        AssertDebugLog(expected.Split('|').Select(s => new Vb6Value(s)).ToList());
    }

    [Fact]
    public async Task Let_through_an_object_returning_Get_remains_unmeasured()
    {
        var run = () => RunClasses("Dim c As New C\nc(1) = 5\n", ("C", """
            Public Property Get Value(ByVal i As Long) As C
            Attribute Value.VB_UserMemId = 0
                Set Value = Me
            End Property

            """));
        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#4906*");
    }

    // vb6.exe 6.00.9782, native and p-code: #4906 round 2 supervisor measurements
    // (oracle PR #5082). Only the cells recorded there are pinned here.
    private const string ThingDefault = "Private v As Variant\nPrivate Sub Class_Initialize()\nv = 4\nEnd Sub\nPublic Property Get Value()\nAttribute Value.VB_UserMemId = 0\nDebug.Print \"Thing.Get\"\nValue = v\nEnd Property\nPublic Property Let Value(ByVal n)\nDebug.Print \"Thing.Let \" & n\nv = n\nEnd Property\nPublic Property Get Stored()\nStored = v\nEnd Property\n";

    private Task ObjectDefaultProbe(string kind, string binding, string statement) => RunClasses(
        $"Dim c As {binding}, x As Variant\nSet c = New {kind}\nx = \"keep\"\nOn Error Resume Next\n{statement}\nDebug.Print \"err \" & Err.Number\n"
        + (statement == "c(1) = 5" && kind is "Co" or "CoF" or "CoO" or "CoV" ? "Debug.Print \"stored \" & c.State\n" : ""),
        (kind, CollectionDefault(kind)),
        ("Thing", ThingDefault),
        ("ThingG", "Public Property Get Value()\nAttribute Value.VB_UserMemId = 0\nDebug.Print \"ThingG.Get\"\nValue = 7\nEnd Property\n"),
        ("ThingN", "Public Sub F()\nEnd Sub\n"),
        ("ThingA", "Public Property Get Item(ByVal k)\nAttribute Item.VB_UserMemId = 0\nDebug.Print \"ThingA.Get\"\nItem = 3\nEnd Property\nPublic Property Let Item(ByVal k, ByVal v)\nDebug.Print \"ThingA.Let\"\nEnd Property\n"),
        ("ThingF", "Public Function Value()\nAttribute Value.VB_UserMemId = 0\nValue = 7\nEnd Function\n"));

    private static string CollectionDefault(string kind)
    {
        if (kind is "LetOnly" or "SetOnly")
            return $"Public Property {(kind == "LetOnly" ? "Let" : "Set")} Item(ByVal i As Long, ByVal v{(kind == "SetOnly" ? " As Object" : "")})\nAttribute Item.VB_UserMemId = 0\nDebug.Print \"Let \" & i & \" \" & v\nEnd Property\n";
        var target = kind switch { "CoG" or "CoOG" => "ThingG", "CoNd" => "ThingN", "CoA" => "ThingA", "CoRF" => "ThingF", _ => "Thing" };
        var declared = kind switch { "CoO" or "CoOG" => "Object", "CoV" => "Variant", _ => target };
        var procedure = kind == "CoF" ? "Function" : "Property Get";
        var end = kind == "CoF" ? "Function" : "Property";
        return $"Private t As {target}\nPrivate Sub Class_Initialize()\n{(kind == "CoZ" ? "" : $"Set t = New {target}")}\nEnd Sub\nPublic {procedure} Item(ByVal i As Long) As {declared}\nAttribute Item.VB_UserMemId = 0\nDebug.Print \"Item \" & i\nSet Item = t\nEnd {end}\n"
            + (target == "Thing" ? "Public Property Get State()\nState = t.Stored\nEnd Property\n" : "");
    }

    [Theory]
    [InlineData("Co", "Co", "c(1) = 5", "Item 1|Thing.Let 5|err 0|stored 5")]
    [InlineData("Co", "Object", "c(1) = 5", "Item 1|Thing.Let 5|err 0|stored 5")]
    [InlineData("Co", "Variant", "c(1) = 5", "Item 1|Thing.Let 5|err 0|stored 5")]
    [InlineData("CoF", "CoF", "c(1) = 5", "Item 1|Thing.Let 5|err 0|stored 5")]
    [InlineData("CoF", "Object", "c(1) = 5", "Item 1|Thing.Let 5|err 0|stored 5")]
    [InlineData("CoF", "Variant", "c(1) = 5", "err 450|stored 4")]
    [InlineData("CoO", "CoO", "c(1) = 5", "Item 1|Thing.Let 5|err 0|stored 5")]
    [InlineData("CoO", "Object", "c(1) = 5", "Item 1|Thing.Let 5|err 0|stored 5")]
    [InlineData("CoO", "Variant", "c(1) = 5", "Item 1|Thing.Let 5|err 0|stored 5")]
    [InlineData("CoV", "CoV", "c(1) = 5", "Item 1|Thing.Let 5|err 0|stored 5")]
    [InlineData("CoV", "Object", "c(1) = 5", "Item 1|Thing.Let 5|err 0|stored 5")]
    [InlineData("CoV", "Variant", "c(1) = 5", "Item 1|Thing.Let 5|err 0|stored 5")]
    [InlineData("CoG", "Object", "c(1) = 5", "Item 1|ThingG.Get|err 451")]
    [InlineData("CoG", "Variant", "c(1) = 5", "Item 1|ThingG.Get|err 451")]
    [InlineData("CoNd", "CoNd", "c(1) = 5", "Item 1|err 438")]
    [InlineData("CoNd", "Object", "c(1) = 5", "Item 1|Item 1|err 438")]
    [InlineData("CoNd", "Variant", "c(1) = 5", "Item 1|err 438")]
    [InlineData("CoA", "Object", "c(1) = 5", "Item 1|err 449")]
    [InlineData("CoA", "Variant", "c(1) = 5", "Item 1|err 449")]
    [InlineData("CoZ", "CoZ", "c(1) = 5", "Item 1|err 91")]
    [InlineData("CoZ", "Object", "c(1) = 5", "Item 1|Item 1|err 438")]
    [InlineData("CoZ", "Variant", "c(1) = 5", "Item 1|err 438")]
    [InlineData("LetOnly", "Object", "x = c(1)", "err 450")]
    [InlineData("LetOnly", "Variant", "x = c(1)", "err 450")]
    [InlineData("LetOnly", "LetOnly", "c(1) = 5", "Let 1 5|err 0")]
    [InlineData("LetOnly", "Object", "c(1) = 5", "Let 1 5|err 0")]
    [InlineData("LetOnly", "Variant", "c(1) = 5", "Let 1 5|err 0")]
    [InlineData("SetOnly", "Object", "x = c(1)", "err 450")]
    [InlineData("SetOnly", "Variant", "x = c(1)", "err 450")]
    [InlineData("SetOnly", "Object", "c(1) = 5", "err 450")]
    [InlineData("SetOnly", "Variant", "c(1) = 5", "err 450")]
    public async Task Object_returning_default_assignments(string kind, string binding, string statement, string expected)
    {
        await ObjectDefaultProbe(kind, binding, statement);
        AssertDebugLog(expected.Split('|').Select(s => new Vb6Value(s)).ToList());
    }

    [Theory]
    [InlineData("CoG", "c(1) = 5", "Invalid use of property")]
    [InlineData("CoA", "c(1) = 5", "Argument not optional")]
    [InlineData("LetOnly", "x = c(1)", "Invalid use of property")]
    [InlineData("SetOnly", "x = c(1)", "Invalid use of property")]
    [InlineData("SetOnly", "c(1) = 5", "Invalid use of property")]
    public async Task Invalid_object_defaults_fail_before_calling_Item(string kind, string statement, string message)
    {
        var run = () => ObjectDefaultProbe(kind, kind, statement);
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.ReplaceLineEndings()
            .Should().Be($"Compile error:\n\n{message}".ReplaceLineEndings());
        AssertDebugLog([]);
    }

    [Theory]
    [InlineData("CoOG", "A Let through a default member's Object or Variant result is unmeasured (#4906)")]
    [InlineData("CoRF", "Let through an object-returning default member is unmeasured (#4906)")]
    public async Task Unmeasured_returned_defaults_still_refuse(string kind, string message)
    {
        var run = () => ObjectDefaultProbe(kind, kind, "c(1) = 5");
        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage(message);
    }

    [Theory]
    [InlineData("CoZ", "CoZ", "Item 1|Empty |err 91")]
    [InlineData("CoZ", "Object", "Item 1|Empty |err 91")]
    [InlineData("CoZ", "Variant", "Item 1|Empty |err 91")]
    [InlineData("CoNd", "CoNd", "Item 1|Empty |err 438")]
    [InlineData("CoNd", "Object", "Item 1|Empty |err 438")]
    [InlineData("CoNd", "Variant", "Item 1|Empty |err 438")]
    [InlineData("Co", "Co", "Item 1|Thing.Get|Integer 4|err 0")]
    [InlineData("Co", "Object", "Item 1|Thing.Get|Integer 4|err 0")]
    [InlineData("Co", "Variant", "Item 1|Thing.Get|Integer 4|err 0")]
    [InlineData("CoG", "CoG", "Item 1|ThingG.Get|7|err 0")]
    [InlineData("CoG", "Object", "Item 1|ThingG.Get|7|err 0")]
    [InlineData("CoG", "Variant", "Item 1|ThingG.Get|7|err 0")]
    public async Task Default_result_Let_values_and_failed_targets(string kind, string binding, string expected)
    {
        await ObjectDefaultProbe(kind, binding, "x = c(1)\nDebug.Print "
            + (kind == "CoG" ? "CStr(x)" : "TypeName(x) & \" \" & CStr(x)"));
        AssertDebugLog(expected.Split('|').Select(s => new Vb6Value(s)).ToList());
    }

    [Theory]
    [InlineData("Object", "x", "Variant")]
    [InlineData("Thing", "x", "Variant")]
    [InlineData("Variant", "x", "Variant")]
    [InlineData("Thing", "s", "String")]
    public async Task Nothing_variable_Let_raises_91_and_keeps_target(string binding, string target, string targetType)
    {
        await RunClasses($"Dim o As {binding}, {target} As {targetType}\nSet o = Nothing\n{target} = \"keep\"\nOn Error Resume Next\n{target} = o\nDebug.Print CStr(Err.Number) & \" \" & {target}\n", ("Thing", ThingDefault));
        AssertDebugLog([new Vb6Value("91 keep")]);
    }

    [Theory]
    [InlineData("Dim col As New Collection\ncol.Add New ThingN\nx = col(1)")]
    [InlineData("Dim arr(1 To 1) As Object\nSet arr(1) = New ThingN\nx = arr(1)")]
    public async Task Non_class_default_temporaries_keep_today_s_target(string statement)
    {
        await RunClasses("Dim x As Variant\nx = \"keep\"\nOn Error Resume Next\n" + statement + "\nDebug.Print CStr(Err.Number) & \" \" & x\n",
            ("ThingN", "Public Sub F()\nEnd Sub\n"));
        AssertDebugLog([new Vb6Value("438 keep")]);
    }
}
