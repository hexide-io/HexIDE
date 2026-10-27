using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured on vb6.exe SP6, native and p-code alike: #178's whole-UDT copy and array probes.
public class UdtArrayTests : BaseVBTestFixture
{
    private const string Types = "Type Inner\ns As String\nEnd Type\nType T\ns As String\nfx(1 To 3) As Long\ndy() As Long\nn As Long\nchild As Inner\nEnd Type\n";

    // Keep the record-file boundary: enabling runtime elements must not silently enable an unmeasured byte layout.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Record_serialization_of_UDT_arrays_remains_refused(bool read)
    {
        var array = new VBArray(Vb6Value.ValueType.UserDefinedType, [(0, 0)]);
        array.SetValue([0], Vb6Value.NewUdt(new VbUdt("T", new() { ["n"] = new Vb6Value(1L) },
            new Dictionary<string, Vb6Value.ValueType> { ["n"] = Vb6Value.ValueType.Long }, new HashSet<string>())));
        var writer = new VbRecordFormat.Writer();
        Action act = () =>
        {
            if (read) VbRecordFormat.ReadElementsInto(new UnreadSource(), array);
            else VbRecordFormat.WriteArray(writer, array, descriptor: false);
        };
        act.Should().Throw<NotImplementedException>().WithMessage("*array of a user-defined Type*#178*");
        writer.ToArray().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Record_serialization_of_array_fields_remains_refused(bool read)
    {
        var array = new Vb6Value(new Vb6Value.ValueType(Vb6Value.ValueType.Long, true), [(0, 0)]);
        var bag = new VbUdt("T", new() { ["a"] = array });
        var writer = new VbRecordFormat.Writer();
        Action act = () =>
        {
            if (read) VbRecordFormat.ReadUdtInto(new UnreadSource(), bag);
            else VbRecordFormat.WriteUdt(writer, bag);
        };
        act.Should().Throw<NotImplementedException>().WithMessage("*array field*#178*");
        writer.ToArray().Should().BeEmpty();
    }

    private sealed class UnreadSource : IRecordSource
    {
        public byte[] Take(int count) => throw new InvalidOperationException("An unsupported layout must not read bytes");
    }

    [Fact]
    public async Task ForEach_over_records_is_a_compile_refusal()
    {
        var act = async () => await Run("Type T\ns As String\nEnd Type\nDim a(0 To 3) As T, v\nFor Each v In a\nNext\n");
        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should()
            .Contain("For Each may not be used on array of user-defined type or fixed-length strings");
    }

    [Theory]
    [InlineData("3", false)]
    [InlineData("", false)]
    [InlineData("3", true)]
    [InlineData("", true)]
    public async Task Fixed_and_dynamic_declarations_allocate_real_records(string bounds, bool local)
    {
        var body = $"Dim a({bounds}) As T\n"
            + (bounds.Length == 0 ? "ReDim a(0 To 3)\n" : "")
            + "a(1).s = \"one\"\nDebug.Print a(1).s\nDebug.Print a(0).s\n";
        await Run("Type T\ns As String\nEnd Type\n"
            + (local ? "Sub Test()\n" + body + "End Sub\nTest\n" : body));
        AssertDebugLog(["one", ""]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Elements_start_with_independent_default_fields(bool local)
    {
        var body = "Dim a(0 To 3) As T\na(1).s = \"one\"\na(1).fx(1) = 7\n"
            + "Debug.Print a(0).s\nDebug.Print a(0).fx(1)\nDebug.Print a(1).s\nDebug.Print a(1).fx(1)\n"
            + "On Error Resume Next\nDebug.Print UBound(a(0).dy)\nDebug.Print Err.Number\n";
        await Run(Types + (local ? "Sub Test()\n" + body + "End Sub\nTest\n" : body));
        AssertDebugLog(["", new Vb6Value(0L), "one", new Vb6Value(7L), new Vb6Value(9L)]);
    }

    [Fact]
    public async Task Whole_element_stores_and_reads_copy_every_field()
    {
        await Run(Types + """
            Dim a(0 To 3) As T, u As T, w As T
            u.s = "old"
            u.fx(1) = 7
            ReDim u.dy(0 To 1)
            u.dy(0) = 8
            u.child.s = "nested"
            a(1) = u
            u.s = "new"
            u.fx(1) = 9
            ReDim u.dy(0 To 3)
            Erase u.dy
            u.child.s = "new nested"
            w = a(1)
            a(2) = a(1)
            a(1).fx(1) = 10
            ReDim Preserve a(1).dy(0 To 3)
            a(1).dy(0) = 11
            Debug.Print w.s
            Debug.Print w.fx(1)
            Debug.Print w.dy(0)
            Debug.Print UBound(w.dy)
            Debug.Print w.child.s
            Debug.Print a(2).fx(1)
            Debug.Print a(2).dy(0)
            """);
        AssertDebugLog(["old", new Vb6Value(7L), new Vb6Value(8L), new Vb6Value(1L), "nested", new Vb6Value(7L), new Vb6Value(8L)]);
    }

    [Fact]
    public async Task Preserve_keeps_old_elements_and_initialises_new_ones_and_plain_Redim_resets()
    {
        await Run(Types + """
            Dim a() As T
            ReDim a(0 To 3)
            a(1).s = "keep"
            ReDim a(1).dy(0 To 1)
            a(1).dy(0) = 8
            ReDim Preserve a(0 To 5)
            Debug.Print a(1).s
            Debug.Print a(1).dy(0)
            Debug.Print a(5).s
            Debug.Print a(5).fx(1)
            ReDim Preserve a(0 To 1)
            Debug.Print a(1).dy(0)
            ReDim a(0 To 3)
            Debug.Print a(1).s
            Debug.Print a(1).fx(1)
            Erase a
            On Error Resume Next
            Debug.Print UBound(a)
            Debug.Print Err.Number
            """);
        AssertDebugLog(["keep", new Vb6Value(8L), "", new Vb6Value(0L), new Vb6Value(8L), "", new Vb6Value(0L), new Vb6Value(9L)]);
    }

    [Fact]
    public async Task Erase_fixed_array_resets_every_field()
    {
        await Run(Types + """
            Dim a(0 To 3) As T
            a(1).s = "old"
            a(1).fx(1) = 7
            ReDim a(1).dy(0 To 1)
            a(1).child.s = "nested"
            Erase a
            Debug.Print UBound(a)
            Debug.Print a(1).s
            Debug.Print a(1).fx(1)
            Debug.Print a(1).child.s
            On Error Resume Next
            Debug.Print UBound(a(1).dy)
            Debug.Print Err.Number
            """);
        AssertDebugLog([new Vb6Value(3L), "", new Vb6Value(0L), "", new Vb6Value(9L)]);
    }

    [Fact]
    public async Task ByRef_element_writes_through_and_locks_its_array()
    {
        await Run(Types + """
            Dim a() As T
            Sub Modify(x As T)
                x.s = "changed"
                ReDim x.dy(0 To 1)
                x.dy(0) = 8
                Debug.Print a(1).s
                Debug.Print a(1).dy(0)
                On Error Resume Next
                ReDim a(0 To 5)
                Debug.Print Err.Number
                Err.Clear
                ReDim Preserve a(0 To 5)
                Debug.Print Err.Number
                Err.Clear
                Erase a
                Debug.Print Err.Number
            End Sub
            ReDim a(0 To 3)
            Modify a(1)
            Debug.Print a(1).s
            Debug.Print a(1).dy(0)
            Debug.Print UBound(a)
            """);
        AssertDebugLog(["changed", new Vb6Value(8L), new Vb6Value(10L), new Vb6Value(10L), new Vb6Value(10L), "changed", new Vb6Value(8L), new Vb6Value(3L)]);
    }

    [Fact]
    public async Task Function_return_and_module_store_copy_the_record()
    {
        await Run(Types + """
            Dim g As T, a(0 To 3) As T, u As T
            Function MakeT() As T
                MakeT.s = "result"
                MakeT.fx(1) = 7
            End Function
            a(0) = MakeT()
            u.s = "old"
            ReDim u.dy(0 To 1)
            u.dy(0) = 8
            g = u
            u.dy(0) = 9
            Debug.Print a(0).s
            Debug.Print a(0).fx(1)
            Debug.Print g.dy(0)
            """);
        AssertDebugLog(["result", new Vb6Value(7L), new Vb6Value(8L)]);
    }

    [Fact]
    public async Task Array_fields_copy_only_their_type_and_Byte_fields_also_take_String_bytes()
    {
        await Run("""
            Type Holder
                s() As String
                b() As Byte
                n As Long
            End Type
            Dim u As Holder, v, longs(1) As Long
            v = Split("p q")
            u.s = v
            v(0) = "changed"
            Debug.Print u.s(0)
            Debug.Print u.s(1)
            v = "ab"
            u.b = v
            Debug.Print LBound(u.b)
            Debug.Print UBound(u.b)
            Debug.Print u.b(0)
            Debug.Print u.b(1)
            Debug.Print u.b(2)
            Debug.Print u.b(3)
            On Error Resume Next
            v = Empty
            u.s = v
            Debug.Print Err.Number
            Err.Clear
            v = longs
            u.s = v
            Debug.Print Err.Number
            Err.Clear
            v = "ab"
            u.s = v
            Debug.Print Err.Number
            Err.Clear
            Set v = Nothing
            u.s = v
            Debug.Print Err.Number
            Debug.Print u.s(0)
            Err.Clear
            v = Empty
            u.b = v
            Debug.Print Err.Number
            Err.Clear
            v = Split("p q")
            u.b = v
            Debug.Print Err.Number
            Err.Clear
            v = longs
            u.b = v
            Debug.Print Err.Number
            Err.Clear
            Set v = Nothing
            u.b = v
            Debug.Print Err.Number
            Debug.Print u.b(0)
            """);
        AssertDebugLog(["p", "q", new Vb6Value(0L), new Vb6Value(3L), new Vb6Value((byte)97), new Vb6Value((byte)0),
            new Vb6Value((byte)98), new Vb6Value((byte)0), new Vb6Value(13L), new Vb6Value(13L), new Vb6Value(13L), new Vb6Value(13L), "p",
            new Vb6Value(13L), new Vb6Value(13L), new Vb6Value(13L), new Vb6Value(13L), new Vb6Value((byte)97)]);
    }

    [Fact]
    public async Task Scalar_literal_into_array_field_is_refused()
    {
        var act = async () => await Run("Type Holder\ns() As String\nEnd Type\nDim u As Holder\nu.s = 5\n");
        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Can't assign to array");
    }
}
