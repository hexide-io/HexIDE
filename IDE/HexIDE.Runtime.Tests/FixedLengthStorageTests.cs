using System.IO;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// VB6 SP6 native/p-code measurements in #179's latest supervisor comment, oracle PR #4019.
public class FixedLengthStorageTests : BaseVBTestFixture
{
    [Fact]
    public async Task Type_fields_initialize_coerce_and_copy_independently()
    {
        await Run("Type T\ni As Integer\ns As String * 4\nEnd Type\nDim u As T, v As T\nDebug.Print u.s\nu.s = \"ab\"\nv = u\nv.s = \"rstuvw\"\nDebug.Print u.s\nDebug.Print v.s\nDebug.Print Len(u)");
        AssertDebugLog([new Vb6Value(new string('\0', 4)), new Vb6Value("ab  "), new Vb6Value("rstu"), new Vb6Value(6)]);
    }

    [Theory]
    [InlineData("Dim a(2) As String * 3", "")]
    [InlineData("", "Dim a(2) As String * 3")]
    public async Task Array_elements_initialize_truncate_and_erase(string module, string local)
    {
        await Run($"{module}\nSub Main()\n{local}\nDebug.Print a(0)\na(1) = \"abcdef\"\nDebug.Print a(1)\nErase a\nDebug.Print a(1)\nEnd Sub\nMain");
        AssertDebugLog([new Vb6Value(new string('\0', 3)), new Vb6Value("abc"), new Vb6Value(new string('\0', 3))]);
    }

    [Fact]
    public async Task Dynamic_array_keeps_width_after_ReDim()
    {
        await Run("Dim b() As String * 4\nReDim b(1)\nb(0) = \"z\"\nDebug.Print b(0)\nDebug.Print b(1)");
        AssertDebugLog([new Vb6Value("z   "), new Vb6Value(new string('\0', 4))]);
    }

    [Theory]
    [InlineData("String", "xyzxyzxyz", "xyzxy")]
    [InlineData("Variant", "variantset", "varia")]
    public async Task ByRef_copies_padded_value_in_and_truncated_value_out(string type, string assigned, string expected)
    {
        await Run($"Dim fx As String * 5\nfx = \"ab\"\nTake fx\nDebug.Print fx\nSub Take(s As {type})\nDebug.Print Len(s)\ns = \"{assigned}\"\nEnd Sub");
        AssertDebugLog([new Vb6Value(5L), new Vb6Value(expected)]);
    }

    [Fact]
    public async Task Fixed_string_array_ForEach_still_reports_compile_error()
    {
        var run = () => Run("Dim a(2) As String * 3\nDim v\nFor Each v In a\nNext");
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should()
            .Contain("For Each may not be used on array of user-defined type or fixed-length strings");
    }

    [Theory]
    [InlineData("Dim b() As String * 4\nReDim b(1) As String", "ReDim As on a fixed-length String array is not implemented (#179): its width rules are unmeasured")]
    [InlineData("Dim b() As String * 4\nDim a(1) As String\nb = a", "Assigning a whole fixed-length String array is not implemented (#179): its assignment rules are unmeasured")]
    [InlineData("Dim a(1) As String * 4\nDim v\nv = a", "Copying a whole fixed-length String array is not implemented (#179): its assignment rules are unmeasured")]
    [InlineData("Dim fx As String * 5\nTake fx\nSub Take(n As Long)\nEnd Sub", "ByRef fixed-length String to this parameter type is not implemented (#179)")]
    [InlineData("Dim fx As String * 5\nTake fx\nSub Take(n As Object)\nEnd Sub", "ByRef fixed-length String to this parameter type is not implemented (#179)")]
    [InlineData("Dim fx As String * 5\nTake fx\nSub Take(n As Collection)\nEnd Sub", "ByRef fixed-length String to this parameter type is not implemented (#179)")]
    public async Task Unmeasured_storage_edges_are_explicitly_refused(string program, string expected)
    {
        var run = () => Run(program);
        (await run.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Be(expected);
    }

    [Theory]
    [InlineData("Binary", "Get")]
    [InlineData("Binary", "Put")]
    [InlineData("Random", "Get")]
    [InlineData("Random", "Put")]
    public async Task Fixed_string_array_file_layout_is_explicitly_refused(string mode, string statement)
    {
        var path = Path.Combine(Path.GetTempPath(), "hexide-fixed-array-" + Guid.NewGuid() + ".dat");
        try
        {
            var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv,
                $"Dim a() As String * 4\nReDim a(1)\nOpen \"{path}\" For {mode} As #1 Len = 64\n{statement} #1, 1, a");
            try
            {
                var run = () => vb.Execute();
                (await run.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should()
                    .Be("Get/Put of an array of fixed-length Strings is not implemented (#179): its record layout is unmeasured");
            }
            finally { vb.BuiltIns.Files.CloseAll(); }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("Binary", false)]
    [InlineData("Random", false)]
    [InlineData("Binary", true)]
    [InlineData("Random", true)]
    public async Task File_records_write_raw_fixed_strings_and_read_back(string mode, bool record)
    {
        // docs/vb6-fidelity-oracle/random-and-binary-files-get-put-and-len.md, fixed String/Type rows.
        var path = Path.Combine(Path.GetTempPath(), "hexide-fixed-" + Guid.NewGuid() + ".dat");
        try
        {
            var declarations = record ? "Type T\ni As Integer\ns As String * 3\nEnd Type\nDim fx As T" : "Dim fx As String * 5";
            var assign = record ? "fx.i = 1\nfx.s = \"a\"" : "fx = \"ab\"";
            var clear = record ? "fx.i = 0\nfx.s = \"\"" : "fx = \"\"";
            var print = record ? "Debug.Print fx.i\nDebug.Print fx.s" : "Debug.Print fx";
            await Run($"{declarations}\n{assign}\nOpen \"{path}\" For {mode} As #1 Len = 5\nPut #1, 1, fx\n{clear}\nGet #1, 1, fx\n{print}\nClose #1");
            File.ReadAllBytes(path).Should().Equal(record ? new byte[] { 1, 0, 97, 32, 32 } : new byte[] { 97, 98, 32, 32, 32 });
            AssertDebugLog(record ? [new Vb6Value(1), new Vb6Value("a  ")] : [new Vb6Value("ab   ")]);
        }
        finally { File.Delete(path); }
    }
}
