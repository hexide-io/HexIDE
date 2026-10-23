using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Indexing a call's result, as in <c>Split(s)(0)</c> (#1476). Each program is the probe vb6.exe ran, native and p-code
/// agreeing, with its <c>L</c> printing where the probe's appended to a log, and each transcript is that log (the
/// oracle's "Indexing a call's result").
/// </summary>
public class ChainedIndexTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    // The first probe: Split, Array, a Function's Variant or typed array, an array of arrays, a Collection, a method, out of
    // range, a scalar, and writing into an array of arrays.
    [Fact]
    public async Task A_calls_result_is_indexed_as_vb6_exe_indexes_it()
    {
        await RunClasses("""""
            Private Function V3() As Variant
                V3 = Array(10, 20, 30)
            End Function
            Private Function T3() As Long()
                Dim a(2) As Long
                a(0) = 100: a(1) = 200: a(2) = 300
                T3 = a
            End Function
            Private Function Jag() As Variant
                Jag = Array(Array(1, 2), Array(3, Array(4, 5)))
            End Function
            Private Function Col() As Collection
                Dim c As New Collection
                c.Add "alpha", "a": c.Add "beta", "b"
                Set Col = c
            End Function
            Private Function NoArgs() As Variant
                NoArgs = Array(5, 6)
            End Function
            Private Function WithArg(ByVal n As Integer) As Variant
                WithArg = Array(n, n * 2, n * 3)
            End Function
            Private Function Scalar() As Integer
                Scalar = 42
            End Function
            Private Function VScalar() As Variant
                VScalar = 42
            End Function
            Private Function VStr() As Variant
                VStr = "abc"
            End Function
            Sub Main()
                On Error Resume Next
                Dim s As String, r, a, k As K1, v
                s = "x y z"
                r = Empty: r = Split(s)(0): L "Split(s)(0) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = Split(s)(2): L "Split(s)(2) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = Split(s)(3): L "Split(s)(3) = [" & r & "] err " & Err.Number: Err.Clear
                r = Empty: r = Split(s, " ")(1): L "Split(s, "" "")(1) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = Array(1, 2)(1): L "Array(1, 2)(1) = " & r & " " & TypeName(r) & " err " & Err.Number: Err.Clear
                r = Empty: r = V3()(2): L "V3()(2) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = V3()(-1): L "V3()(-1) = [" & r & "] err " & Err.Number: Err.Clear
                r = Empty: r = T3()(1): L "T3()(1) = " & r & " " & TypeName(r) & " err " & Err.Number: Err.Clear
                r = Empty: r = Jag()(1)(0): L "Jag()(1)(0) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = Jag()(1)(1)(1): L "Jag()(1)(1)(1) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = WithArg(4)(2): L "WithArg(4)(2) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = NoArgs()(1): L "NoArgs()(1) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = NoArgs(1): L "NoArgs(1) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = Col()(2): L "Col()(2) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = Col()("a"): L "Col()(""a"") = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = VScalar()(0): L "VScalar()(0) = [" & r & "] err " & Err.Number: Err.Clear
                r = Empty: r = VStr()(0): L "VStr()(0) = [" & r & "] err " & Err.Number: Err.Clear
                r = Empty: r = Split(s)(0, 1): L "Split(s)(0, 1) = [" & r & "] err " & Err.Number: Err.Clear
                r = Empty: r = Split(s)("1"): L "Split(s)(""1"") = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = Split(s)(1.5): L "Split(s)(1.5) = " & r & " err " & Err.Number: Err.Clear
                Set k = New K1
                r = Empty: r = k.Items()(1): L "k.Items()(1) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = k.Items(2): L "k.Items(2) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = k.Typed()(2): L "k.Typed()(2) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = k.Bag()(2): L "k.Bag()(2) = " & r & " err " & Err.Number: Err.Clear
                ' Writing through a chain of indexes into an array of arrays.
                a = Array(Array(1, 2), Array(3, 4))
                a(1)(0) = 9: L "a(1)(0) = 9 -> a(1)(0) " & a(1)(0) & " err " & Err.Number: Err.Clear
                r = Empty: r = a(0)(1): L "a(0)(1) = " & r & " err " & Err.Number: Err.Clear
                v = a(1): v(1) = 99: L "v = a(1): v(1) = 99 -> a(1)(1) " & a(1)(1) & " err " & Err.Number: Err.Clear
                ' The order: the call, then its index.
                L "Split(Trace(""p q""))(Idx()) = " & Split(Trace("p q"))(Idx())
                L "end"
            End Sub
            Private Function Trace(ByVal t As String) As String
                L "Trace"
                Trace = t
            End Function
            Private Function Idx() As Integer
                L "Idx"
                Idx = 1
            End Function
            Private Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"",
            ("K1", """""
                Public Function Items() As Variant
                    Items = Array("p", "q", "r")
                End Function
                Public Function Typed() As Integer()
                    Dim a(2) As Integer
                    a(0) = 7: a(1) = 8: a(2) = 9
                    Typed = a
                End Function
                Public Function Bag() As Collection
                    Dim c As New Collection
                    c.Add "first": c.Add "second"
                    Set Bag = c
                End Function

                """""));

        AssertDebugLog(Lines("""""
            Split(s)(0) = x err 0
            Split(s)(2) = z err 0
            Split(s)(3) = [] err 9
            Split(s, " ")(1) = y err 0
            Array(1, 2)(1) = 2 Integer err 0
            V3()(2) = 30 err 0
            V3()(-1) = [] err 9
            T3()(1) = 200 Long err 0
            Jag()(1)(0) = 3 err 0
            Jag()(1)(1)(1) = 5 err 0
            WithArg(4)(2) = 12 err 0
            NoArgs()(1) = 6 err 0
            NoArgs(1) = 6 err 0
            Col()(2) = beta err 0
            Col()("a") = alpha err 0
            VScalar()(0) = [] err 13
            VStr()(0) = [] err 13
            Split(s)(0, 1) = [] err 9
            Split(s)("1") = y err 0
            Split(s)(1.5) = z err 0
            k.Items()(1) = q err 0
            k.Items(2) = r err 0
            k.Typed()(2) = 9 err 0
            k.Bag()(2) = second err 0
            a(1)(0) = 9 -> a(1)(0) 9 err 0
            a(0)(1) = 2 err 0
            v = a(1): v(1) = 99 -> a(1)(1) 4 err 0
            Idx
            Trace
            Split(Trace("p q"))(Idx()) = q
            end
            """""));
    }

    // The second probe: an index on a parameterless Function, method or Property Get is its result's, where an Optional
    // parameter takes it as an argument. Out of range is 9, and a scalar 13.
    [Fact]
    public async Task A_parameterless_call_takes_its_index_as_its_results()
    {
        await RunClasses("""""
            Private Function T(ByVal tag As String, ByVal value As Variant) As Variant
                L tag
                T = value
            End Function
            Private Function Jag(ByVal tag As String) As Variant
                L tag
                Jag = Array(Array(1, 2), Array(3, Array(4, 5)))
            End Function
            Private Function NoArgs() As Variant
                L "NoArgs"
                NoArgs = Array(5, 6)
            End Function
            Private Function Opt(Optional ByVal n As Integer = 0) As Variant
                Opt = Array(n, n + 1)
            End Function
            Private Function TwoArgs(ByVal a As Integer, ByVal b As Integer) As Variant
                TwoArgs = Array(a, b, a + b)
            End Function
            Sub Main()
                On Error Resume Next
                Dim r, a, k As K1
                r = Empty: r = Jag("call")(T("i1", 1))(T("i2", 1))(T("i3", 0)): L "three groups = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = TwoArgs(T("a1", 1), T("a2", 2))(T("i", 2)): L "TwoArgs(a1, a2)(i) = " & r & " err " & Err.Number: Err.Clear
                a = Array(Array(1, 2), Array(3, 4))
                a(T("w1", 1))(T("w2", 0)) = T("rhs", 9): L "a(w1)(w2) = rhs -> " & a(1)(0) & " err " & Err.Number: Err.Clear
                r = Empty: r = NoArgs(1)(0): L "NoArgs(1)(0) = [" & r & "] err " & Err.Number: Err.Clear
                r = Empty: r = NoArgs(5): L "NoArgs(5) = [" & r & "] err " & Err.Number: Err.Clear
                r = Empty: r = NoArgs(): L "NoArgs() = " & TypeName(r) & " err " & Err.Number: Err.Clear
                NoArgs()(1) = 7: L "NoArgs()(1) = 7 err " & Err.Number: Err.Clear
                r = Empty: r = Opt(1): L "Opt(1) = " & TypeName(r) & " " & r(0) & " err " & Err.Number: Err.Clear
                r = Empty: r = Opt(1)(1): L "Opt(1)(1) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = Opt()(1): L "Opt()(1) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = TwoArgs(1, 2)(2): L "TwoArgs(1, 2)(2) = " & r & " err " & Err.Number: Err.Clear
                Set k = New K1
                r = Empty: r = k.Bag(1): L "k.Bag(1) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = k.Prop(1): L "k.Prop(1) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = k.Prop()(0): L "k.Prop()(0) = " & r & " err " & Err.Number: Err.Clear
                r = Empty: r = k.Echo("m n")(1): L "k.Echo(""m n"")(1) = " & r & " err " & Err.Number: Err.Clear
                L "end"
            End Sub
            Private Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"",
            ("K1", """""
                Public Function Bag() As Collection
                    Dim c As New Collection
                    c.Add "first": c.Add "second"
                    Set Bag = c
                End Function
                Public Property Get Prop() As Variant
                    Prop = Array("u", "v")
                End Property
                Public Function Echo(ByVal t As String) As Variant
                    Echo = Split(t)
                End Function

                """""));

        AssertDebugLog(Lines("""""
            i3
            i2
            i1
            call
            three groups = 4 err 0
            a1
            i
            a2
            TwoArgs(a1, a2)(i) = 3 err 0
            w2
            rhs
            w1
            a(w1)(w2) = rhs -> 9 err 0
            NoArgs
            NoArgs(1)(0) = [] err 13
            NoArgs
            NoArgs(5) = [] err 9
            NoArgs
            NoArgs() = Variant() err 0
            NoArgs
            NoArgs()(1) = 7 err 0
            Opt(1) = Variant() 1 err 0
            Opt(1)(1) = 2 err 0
            Opt()(1) = 1 err 0
            TwoArgs(1, 2)(2) = 3 err 0
            k.Bag(1) = first err 0
            k.Prop(1) = v err 0
            k.Prop()(0) = u err 0
            k.Echo("m n")(1) = n err 0
            end
            """""));
    }

    // The third probe: the order in which a call's arguments, its index groups and a Let's value are evaluated. A
    // Function's: all but its last argument, then the index groups from the last, then its last argument. A library
    // function's two arguments come first. A chain's Let: its last group, the value, then the rest from the last.
    [Fact]
    public async Task The_index_groups_are_evaluated_where_vb6_exe_evaluates_them()
    {
        await Run("""""
            Private Function T(ByVal tag As String, ByVal value As Variant) As Variant
                L tag
                T = value
            End Function
            Private Function F1(ByVal a As Variant) As Variant
                L "F1 body"
                F1 = Array(Array(a, 1), Array(2, 3))
            End Function
            Private Function F2(ByVal a As Variant, ByVal b As Variant) As Variant
                L "F2 body"
                F2 = Array(Array(a, b), Array(2, 3))
            End Function
            Private Function F3(ByVal a As Variant, ByVal b As Variant, ByVal c As Variant) As Variant
                L "F3 body"
                F3 = Array(a, b, c)
            End Function
            Private Function G2(ByVal a As Variant, ByVal b As Variant) As Variant
                Dim m(1, 1) As Variant
                L "G2 body"
                m(0, 0) = a: m(0, 1) = b: m(1, 0) = 2: m(1, 1) = 3
                G2 = m
            End Function
            Sub Main()
                On Error Resume Next
                Dim r, a, arr(2, 2) As Integer, b2(1, 1) As Integer
                L "-- F3(a1, a2, a3)": r = F3(T("a1", 1), T("a2", 2), T("a3", 3))
                L "-- F3(a1, a2, a3)(i)": r = F3(T("a1", 1), T("a2", 2), T("a3", 3))(T("i", 0))
                L "-- F2(a1, a2)(i1)(i2)": r = F2(T("a1", 1), T("a2", 2))(T("i1", 0))(T("i2", 0))
                L "-- F1(a1)(i1)(i2)": r = F1(T("a1", 1))(T("i1", 0))(T("i2", 0))
                L "-- G2(a1, a2)(i1, i2)": r = G2(T("a1", 1), T("a2", 2))(T("i1", 0), T("i2", 1))
                L "-- Split(s, d)(i)": r = Split(T("s", "x y"), T("d", " "))(T("i", 1))
                L "-- arr(i1, i2) read": r = arr(T("i1", 0), T("i2", 1))
                L "-- arr(i1, i2) = rhs": arr(T("i1", 0), T("i2", 1)) = T("rhs", 5)
                a = Array(Array(1, Array(2, 3)), 4)
                L "-- a(w1)(w2)(w3) = rhs": a(T("w1", 0))(T("w2", 1))(T("w3", 0)) = T("rhs", 9)
                L "-- a(w1)(w2) = rhs": a(T("w1", 0))(T("w2", 0)) = T("rhs", 8)
                L "-- r = a(i1)(i2)(i3)": r = a(T("i1", 0))(T("i2", 1))(T("i3", 1))
                L "end " & Err.Number
            End Sub
            Private Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"");

        AssertDebugLog(Lines("""""
            -- F3(a1, a2, a3)
            a1
            a2
            a3
            F3 body
            -- F3(a1, a2, a3)(i)
            a1
            a2
            i
            a3
            F3 body
            -- F2(a1, a2)(i1)(i2)
            a1
            i2
            i1
            a2
            F2 body
            -- F1(a1)(i1)(i2)
            i2
            i1
            a1
            F1 body
            -- G2(a1, a2)(i1, i2)
            a1
            i1
            i2
            a2
            G2 body
            -- Split(s, d)(i)
            s
            d
            i
            -- arr(i1, i2) read
            i1
            i2
            -- arr(i1, i2) = rhs
            rhs
            i1
            i2
            -- a(w1)(w2)(w3) = rhs
            w3
            rhs
            w2
            w1
            -- a(w1)(w2) = rhs
            w2
            rhs
            w1
            -- r = a(i1)(i2)(i3)
            i3
            i2
            i1
            end 0
            """""));
    }

    // The shapes vb6.exe refuses to compile, each its own project (measured): indexing a typed scalar, a call's or an
    // element's. HexIDE refuses each where it runs. Len("abc")(0) is VB6's "Syntax error", where this is "Type mismatch".
    [Theory]
    [InlineData("Private Function Scalar() As Integer\n    Scalar = 42\nEnd Function\nDim r\nr = Scalar()(0)")]
    [InlineData("Dim r\nr = Left$(\"abc\", 2)(0)")]
    [InlineData("Dim a(1) As Integer\na(0)(1) = 5")]
    public async Task Indexing_a_typed_scalar_does_not_compile(string program)
    {
        var run = () => Run(program + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Type mismatch*");
    }
}
