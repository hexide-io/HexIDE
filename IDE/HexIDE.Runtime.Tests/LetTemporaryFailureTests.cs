using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class LetTemporaryFailureTests : BaseVBTestFixture
{
    // Measured: #1485, measured table (native and p-code).
    [Fact]
    public async Task NewCollection()
    {
        await RunClasses("""
                Dim v As Variant
                v = "-"

                On Error Resume Next
                v = New Collection
                Debug.Print TypeName(v)
                Debug.Print Err.Number

            Function MakeC() As Collection
                Set MakeC = New Collection
            End Function
            Function MakeV() As Variant
                Set MakeV = New Collection
            End Function
            Function MakeK() As Class1
                Set MakeK = New Class1
            End Function
            """, ("Class1", ""));
        AssertDebugLog(["Empty", new Vb6Value(450L)]);
    }

    // Measured: #1485, measured table (native and p-code).
    [Fact]
    public async Task CollectionVariable()
    {
        await RunClasses("""
                Dim v As Variant
                v = "-"
                Dim c As New Collection
                On Error Resume Next
                v = c
                Debug.Print TypeName(v)
                Debug.Print Err.Number

            Function MakeC() As Collection
                Set MakeC = New Collection
            End Function
            Function MakeV() As Variant
                Set MakeV = New Collection
            End Function
            Function MakeK() As Class1
                Set MakeK = New Class1
            End Function
            """, ("Class1", ""));
        AssertDebugLog(["String", new Vb6Value(450L)]);
    }

    // Measured: #1485, measured table (native and p-code).
    [Fact]
    public async Task ObjectVariable()
    {
        await RunClasses("""
                Dim v As Variant
                v = "-"
                Dim o As Object
                Set o = New Collection
                On Error Resume Next
                v = o
                Debug.Print TypeName(v)
                Debug.Print Err.Number

            Function MakeC() As Collection
                Set MakeC = New Collection
            End Function
            Function MakeV() As Variant
                Set MakeV = New Collection
            End Function
            Function MakeK() As Class1
                Set MakeK = New Class1
            End Function
            """, ("Class1", ""));
        AssertDebugLog(["String", new Vb6Value(450L)]);
    }

    // Measured: #1485, measured table (native and p-code).
    [Fact]
    public async Task VariantVariable()
    {
        await RunClasses("""
                Dim v As Variant
                v = "-"
                Dim w As Variant
                Set w = New Collection
                On Error Resume Next
                v = w
                Debug.Print TypeName(v)
                Debug.Print Err.Number

            Function MakeC() As Collection
                Set MakeC = New Collection
            End Function
            Function MakeV() As Variant
                Set MakeV = New Collection
            End Function
            Function MakeK() As Class1
                Set MakeK = New Class1
            End Function
            """, ("Class1", ""));
        AssertDebugLog(["String", new Vb6Value(450L)]);
    }

    // Measured: #1485, measured table (native and p-code).
    [Fact]
    public async Task CollectionReturn()
    {
        await RunClasses("""
                Dim v As Variant
                v = "-"

                On Error Resume Next
                v = MakeC()
                Debug.Print TypeName(v)
                Debug.Print Err.Number

            Function MakeC() As Collection
                Set MakeC = New Collection
            End Function
            Function MakeV() As Variant
                Set MakeV = New Collection
            End Function
            Function MakeK() As Class1
                Set MakeK = New Class1
            End Function
            """, ("Class1", ""));
        AssertDebugLog(["Empty", new Vb6Value(450L)]);
    }

    // Measured: #1485, measured table (native and p-code).
    [Fact]
    public async Task VariantReturn()
    {
        await RunClasses("""
                Dim v As Variant
                v = "-"

                On Error Resume Next
                v = MakeV()
                Debug.Print TypeName(v)
                Debug.Print Err.Number

            Function MakeC() As Collection
                Set MakeC = New Collection
            End Function
            Function MakeV() As Variant
                Set MakeV = New Collection
            End Function
            Function MakeK() As Class1
                Set MakeK = New Class1
            End Function
            """, ("Class1", ""));
        AssertDebugLog(["Empty", new Vb6Value(450L)]);
    }

    // Measured: #1485, measured table (native and p-code).
    [Fact]
    public async Task NewClass()
    {
        await RunClasses("""
                Dim v As Variant
                v = "-"

                On Error Resume Next
                v = New Class1
                Debug.Print TypeName(v)
                Debug.Print Err.Number

            Function MakeC() As Collection
                Set MakeC = New Collection
            End Function
            Function MakeV() As Variant
                Set MakeV = New Collection
            End Function
            Function MakeK() As Class1
                Set MakeK = New Class1
            End Function
            """, ("Class1", ""));
        AssertDebugLog(["Empty", new Vb6Value(438L)]);
    }

    // Measured: #1485, measured table (native and p-code).
    [Fact]
    public async Task ClassVariable()
    {
        await RunClasses("""
                Dim v As Variant
                v = "-"
                Dim k As New Class1
                On Error Resume Next
                v = k
                Debug.Print TypeName(v)
                Debug.Print Err.Number

            Function MakeC() As Collection
                Set MakeC = New Collection
            End Function
            Function MakeV() As Variant
                Set MakeV = New Collection
            End Function
            Function MakeK() As Class1
                Set MakeK = New Class1
            End Function
            """, ("Class1", ""));
        AssertDebugLog(["String", new Vb6Value(438L)]);
    }

    // Measured: #1485, measured table (native and p-code).
    [Fact]
    public async Task ClassReturn()
    {
        await RunClasses("""
                Dim v As Variant
                v = "-"

                On Error Resume Next
                v = MakeK()
                Debug.Print TypeName(v)
                Debug.Print Err.Number

            Function MakeC() As Collection
                Set MakeC = New Collection
            End Function
            Function MakeV() As Variant
                Set MakeV = New Collection
            End Function
            Function MakeK() As Class1
                Set MakeK = New Class1
            End Function
            """, ("Class1", ""));
        AssertDebugLog(["Empty", new Vb6Value(438L)]);
    }

    // Measured: #1485, measured table (native and p-code).
    [Fact]
    public async Task IntegerOldValue()
    {
        await RunClasses("""
                Dim v As Variant
                v = "-"

                On Error Resume Next
                v = 5
                v = New Collection
                Debug.Print TypeName(v)
                Debug.Print Err.Number

            Function MakeC() As Collection
                Set MakeC = New Collection
            End Function
            Function MakeV() As Variant
                Set MakeV = New Collection
            End Function
            Function MakeK() As Class1
                Set MakeK = New Class1
            End Function
            """, ("Class1", ""));
        AssertDebugLog(["Empty", new Vb6Value(450L)]);
    }

    // Measured: #1485, measured table (native and p-code).
    [Fact]
    public async Task ArrayElement()
    {
        await RunClasses("""
                Dim v As Variant
                v = "-"
                Dim arr(1) As Variant
                arr(0) = "-"
                On Error Resume Next
                arr(0) = New Collection
                Debug.Print TypeName(arr(0))
                Debug.Print Err.Number

            Function MakeC() As Collection
                Set MakeC = New Collection
            End Function
            Function MakeV() As Variant
                Set MakeV = New Collection
            End Function
            Function MakeK() As Class1
                Set MakeK = New Class1
            End Function
            """, ("Class1", ""));
        AssertDebugLog(["Empty", new Vb6Value(450L)]);
    }

    // Measured: #1485, measured table (native and p-code).
    [Fact]
    public async Task StringDestination()
    {
        await RunClasses("""
                Dim v As Variant
                v = "-"
                Dim s As String
                s = "-"
                On Error Resume Next
                s = MakeV()
                Debug.Print TypeName(s)
                Debug.Print Err.Number
                Debug.Print s
            Function MakeC() As Collection
                Set MakeC = New Collection
            End Function
            Function MakeV() As Variant
                Set MakeV = New Collection
            End Function
            Function MakeK() As Class1
                Set MakeK = New Class1
            End Function
            """, ("Class1", ""));
        AssertDebugLog(["String", new Vb6Value(450L), "-"]);
    }

    // Measured: #878, docs/vb6-fidelity-oracle/an-object-used-as-a-value.md, The in-box objects.
    [Fact]
    public async Task App()
    {
        await RunClasses("""
                Dim v As Variant
                v = "-"

                On Error Resume Next
                v = App
                Debug.Print TypeName(v)
                Debug.Print Err.Number

            Function MakeC() As Collection
                Set MakeC = New Collection
            End Function
            Function MakeV() As Variant
                Set MakeV = New Collection
            End Function
            Function MakeK() As Class1
                Set MakeK = New Class1
            End Function
            """, ("Class1", ""));
        AssertDebugLog(["Empty", new Vb6Value(438L)]);
    }

}
