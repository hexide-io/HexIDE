using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6 native and p-code: hexide-io/HexIDE#4925, cases 5–7.
public class UdtFieldArgumentTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("n As Long", "u.n(1) = 5")]
    [InlineData("a(1 To 2) As Long", "u.a(1)(1) = 5")]
    public async Task Unmeasured_scalar_arguments_keep_an_explicit_refusal(string field, string statement)
    {
        var run = () => Run($"Private Type T\n{field}\nEnd Type\nDim u As T\n{statement}");
        (await run.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().EndWith("(#4925)");
    }

    [Theory]
    [InlineData("u.v = CInt(5)")]
    [InlineData("u.v = Null")]
    [InlineData("Set u.v = Nothing")]
    [InlineData("Set u.v = New StdFont")]
    public async Task Unmeasured_Variant_field_values_keep_a_refusal(string setup)
    {
        var run = () => Run($"Private Type T\nv As Variant\nEnd Type\nDim u As T\n{setup}\nu.v(1) = 9");
        await run.Should().ThrowAsync<NotImplementedException>();
    }

    [Theory]
    [InlineData("i As Integer", "u.i(1) = 5")]
    [InlineData("s As String", "u.s(1) = \"x\"")]
    [InlineData("i As Integer", "Debug.Print u.i(1)")]
    [InlineData("s As String", "Debug.Print u.s(1)")]
    public async Task Scalar_field_arguments_require_an_array(string field, string statement)
    {
        var run = () => Run($"Private Type T\n{field}\nEnd Type\nDim u As T\n{statement}");
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().EndWith("Expected array");
    }

    [Theory]
    [InlineData("", "u.v(1) = 9", 13)]
    [InlineData("", "Debug.Print u.v(1)", 13)]
    [InlineData("u.v = \"abc\"", "u.v(1) = 9", 13)]
    [InlineData("Set u.v = New Collection\nu.v.Add 5", "u.v(1) = 9", 438)]
    public async Task Variant_field_arguments_raise_measured_error(string setup, string statement, int error)
    {
        await Run($"Private Type T\nv As Variant\nEnd Type\nDim u As T\n{setup}\nOn Error Resume Next\n{statement}\nDebug.Print Err.Number\n"
            + (setup.Contains("Collection", StringComparison.Ordinal) ? "Debug.Print CStr(u.v(1))" : ""));
        AssertDebugLog(setup.Contains("Collection", StringComparison.Ordinal)
            ? [new Vb6Value((long)error), "5"] : [new Vb6Value((long)error)]);
    }

    [Theory]
    [InlineData("", "u.o(1) = 9", 91)]
    [InlineData("", "Debug.Print u.o(1)", 91)]
    [InlineData("Set u.o = New Collection", "u.o(1) = 9", 451)]
    [InlineData("Set u.o = New StdFont", "u.o(1) = 9", 451)]
    public async Task Object_field_arguments_raise_measured_error(string setup, string statement, int error)
    {
        await Run($"Private Type T\no As Object\nEnd Type\nDim u As T\n{setup}\nOn Error Resume Next\n{statement}\nDebug.Print Err.Number");
        AssertDebugLog([new Vb6Value((long)error)]);
    }

    [Theory]
    [InlineData("v As Variant", "u.v = Array(1, 2, 3)", "u.v(1)")]
    [InlineData("a(1) As Variant", "u.a(0) = Array(1, 2)", "u.a(0)(1)")]
    [InlineData("v As Variant", "u.v = Array(Array(1, 2), 3)", "u.v(0)(1)")]
    public async Task Variant_arrays_support_all_index_groups(string field, string setup, string target)
    {
        await Run($"Private Type T\n{field}\nEnd Type\nDim u As T\n{setup}\nDebug.Print CStr({target})\n{target} = 9\nDebug.Print CStr({target})");
        AssertDebugLog(["2", "9"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task As_New_array_fields_are_lazy_and_recreate_after_Nothing(bool dynamic)
    {
        await Run($"Private Type T\na({(dynamic ? "" : "1")}) As New Collection\nEnd Type\nDim u As T\n"
            + (dynamic ? "ReDim u.a(1)\n" : "") + """
            Debug.Print u.a(0) Is Nothing
            u.a(0).Add 7
            Debug.Print u.a(0).Count
            Debug.Print TypeName(u.a(0))
            Debug.Print u.a(1).Count
            Set u.a(0) = Nothing
            Debug.Print u.a(0) Is Nothing
            Debug.Print u.a(0).Count
            """);
        AssertDebugLog([new Vb6Value(false), new Vb6Value(1L), "Collection", new Vb6Value(0L), new Vb6Value(false), new Vb6Value(0L)]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Record_copy_shares_created_As_New_instances_only(bool created, bool dynamic)
    {
        await Run($"Private Type T\na({(dynamic ? "" : "1")}) As New Collection\nEnd Type\nDim u As T, w As T\n"
            + (dynamic ? "ReDim u.a(1)\n" : "")
            + (created ? "u.a(0).Add 7\n" : "") + "w = u\nDebug.Print w.a(0) Is u.a(0)\nu.a(0).Add 1\nDebug.Print w.a(0).Count");
        AssertDebugLog([new Vb6Value(created), new Vb6Value(created ? 2L : 0L)]);
    }

    [Fact]
    public async Task Dynamic_As_New_field_preserves_elements_and_Erase_frees_it()
    {
        await Run("""
            Private Type T
            a() As New Collection
            End Type
            Dim u As T
            ReDim u.a(1)
            u.a(1).Add 7
            ReDim Preserve u.a(2)
            Debug.Print u.a(1).Count
            Debug.Print u.a(2).Count
            Erase u.a
            On Error Resume Next
            Debug.Print LBound(u.a)
            Debug.Print Err.Number
            """);
        AssertDebugLog([new Vb6Value(1L), new Vb6Value(0L), new Vb6Value(9L)]);
    }

    [Fact]
    public async Task Scalar_As_New_Collection_field_shares_after_use()
    {
        await Run("Private Type T\nc As New Collection\nEnd Type\nDim u As T, w As T\nDebug.Print u.c Is Nothing\nu.c.Add 7\nw = u\nDebug.Print w.c Is u.c");
        AssertDebugLog([new Vb6Value(false), new Vb6Value(true)]);
    }

    [Theory]
    [InlineData("StdFont", false)]
    [InlineData("StdFont", true)]
    [InlineData("TextBox", false)]
    [InlineData("Control", false)]
    public async Task Object_array_fields_start_Nothing_and_dynamic_fields_can_be_erased(string type, bool dynamic)
    {
        await Run($"Private Type T\na({(dynamic ? "" : "1")}) As {type}\nEnd Type\nDim u As T\n"
            + (dynamic ? "On Error Resume Next\nDebug.Print LBound(u.a)\nDebug.Print Err.Number\nErr.Clear\nReDim u.a(1)\n" : "")
            + "Debug.Print u.a(0) Is Nothing\nDebug.Print LBound(u.a) & \" \" & UBound(u.a)\n"
            + (dynamic ? "Erase u.a\nDebug.Print LBound(u.a)\nDebug.Print Err.Number\n" : ""));
        AssertDebugLog(dynamic ? [new Vb6Value(9L), new Vb6Value(true), "0 1", new Vb6Value(9L)] : [new Vb6Value(true), "0 1"]);
    }

    [Fact]
    public async Task Font_array_field_accepts_Font_and_copies_its_reference()
    {
        await Run("Private Type T\na(1) As StdFont\nEnd Type\nDim u As T, w As T\nSet u.a(0) = New StdFont\nu.a(0).Size = 12\nDebug.Print CStr(u.a(0).Size)\nDebug.Print TypeName(u.a(0))\nw = u\nDebug.Print w.a(0) Is u.a(0)");
        AssertDebugLog(["12", "Font", new Vb6Value(true)]);
    }

    [Theory]
    [InlineData("StdFont", "Collection")]
    [InlineData("TextBox", "StdFont")]
    [InlineData("Control", "StdFont")]
    public async Task Object_array_field_checks_incoming_type(string declared, string made)
    {
        await Run($"Private Type T\na(1) As {declared}\nEnd Type\nDim u As T\nOn Error Resume Next\nSet u.a(0) = New {made}\nDebug.Print Err.Number");
        AssertDebugLog([new Vb6Value(13L)]);
    }

    [Fact]
    public async Task Unknown_array_field_type_is_a_compile_error()
    {
        var run = () => Run("Private Type T\na(1) As Foo\nEnd Type\nDim u As T");
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("User-defined type not defined");
    }

    [Theory]
    [InlineData("StdFont")]
    [InlineData("TextBox")]
    [InlineData("Control")]
    public async Task Put_refuses_object_array_fields_as_objects(string type)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        try
        {
            async Task RunPut(string code)
            {
                var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, code);
                try { await vb.Execute(); }
                finally { vb.BuiltIns.Files.CloseAll(); }
            }
            var run = () => RunPut($"Private Type T\na(1) As {type}\nEnd Type\nDim u As T\nOpen \"{path}\" For Binary As #1\nPut #1, 1, u");
            (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().EndWith(VbRecordFormat.ObjectRefused);
        }
        finally { System.IO.File.Delete(path); }
    }
}
