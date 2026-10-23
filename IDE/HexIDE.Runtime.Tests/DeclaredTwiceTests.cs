using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A name declared twice: at module level "Ambiguous name detected", naming the second declaration (#1622), and in one
/// procedure "Duplicate declaration in current scope" (#1499). Measured on vb6.exe, native and p-code alike, one /make
/// project per case, with the probes in docs/vb6-fidelity-oracle.md, "A name declared twice". Each program here is one
/// of them, and each expected line is what vb6.exe logged or the compile error it gave. HexIDE ran each refused one,
/// keeping the first declaration.
/// </summary>
public class DeclaredTwiceTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    private Task RunProgram(Program p) =>
        p.Classes.Length > 0 ? RunClasses(p.Module1, p.Classes) : p.Modules.Length > 0 ? RunModules(p.Module1, p.Modules) : Run(p.Module1);

    [Theory]
    [InlineData("class-property-pair")]
    [InlineData("m-dim-enumname")]
    [InlineData("m-dim-type")]
    [InlineData("m-enum-enum")]
    [InlineData("p-dimarr-redim")]
    [InlineData("p-dim-label")]
    [InlineData("p-dim-module-shadow")]
    [InlineData("p-dim-other-sub")]
    [InlineData("p-dim-sub-name")]
    [InlineData("x-public-public")]
    [InlineData("x-public-public-own")]
    [InlineData("x-public-public-third")]
    [InlineData("x-public-sub-sub")]
    [InlineData("m-type-member-dim")]
    [InlineData("x-public-dim-sub")]
    [InlineData("x-public-sub-own")]
    [InlineData("x-two-public-consts-unused")]
    [InlineData("class-event-dim")]
    [InlineData("class-event-sub")]
    [InlineData("class-let-set")]
    [InlineData("m-enum-name-const")]
    [InlineData("m-enum-name-sub")]
    [InlineData("m-type-name-sub")]
    [InlineData("p-sub-dim-own-name")]
    public async Task A_program_vb6_exe_builds_prints_its_lines(string name)
    {
        var program = Programs[name];

        await RunProgram(program);

        AssertDebugLog(Lines(program.Expected!));
    }

    [Theory]
    [InlineData("class-dim-dim")]
    [InlineData("class-dim-sub")]
    [InlineData("class-property-dim")]
    [InlineData("class-property-get-get")]
    [InlineData("m-const-enum")]
    [InlineData("m-declare-dim")]
    [InlineData("m-dim-case")]
    [InlineData("m-dim-dim-types")]
    [InlineData("m-dim-function")]
    [InlineData("m-dim-L")]
    [InlineData("m-dim-sub-main")]
    [InlineData("m-dim-sub")]
    [InlineData("m-enum-enum-name")]
    [InlineData("m-enum-enum-used")]
    [InlineData("m-enum-twice-member")]
    [InlineData("m-private-public")]
    [InlineData("m-publicconst-private")]
    [InlineData("m-sub-function")]
    [InlineData("m-sub-sub")]
    [InlineData("m-type-type")]
    [InlineData("p-const-dim")]
    [InlineData("p-dim-case")]
    [InlineData("p-dim-const")]
    [InlineData("p-dim-dim")]
    [InlineData("p-dim-dim-types")]
    [InlineData("p-dim-pct")]
    [InlineData("p-dim-static")]
    [InlineData("p-function-dim")]
    [InlineData("p-if-else")]
    [InlineData("p-one-statement")]
    [InlineData("p-param-const")]
    [InlineData("p-param-dim")]
    [InlineData("p-param-param")]
    [InlineData("p-static-static")]
    [InlineData("x-two-publics-read")]
    [InlineData("m-const-const-same")]
    [InlineData("m-dim-dim")]
    [InlineData("m-dim-enum-member")]
    [InlineData("m-enum-member-sub")]
    [InlineData("m-enum-type-same")]
    [InlineData("m-one-statement")]
    [InlineData("x-public-sub-call")]
    [InlineData("class-get-dim-inside")]
    [InlineData("class-get-sub")]
    [InlineData("class-let-let")]
    [InlineData("class-let-param-dim")]
    [InlineData("m-declare-declare")]
    [InlineData("m-declare-sub")]
    [InlineData("p-function-const-own-name")]
    [InlineData("p-function-param-own-name")]
    [InlineData("p-param-static")]
    [InlineData("p-static-const")]
    public async Task A_program_vb6_exe_refuses_is_refused_with_its_message(string name)
    {
        var program = Programs[name];
        var run = () => RunProgram(program);

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*" + program.Refusal + "*");
    }

    private sealed record Program(string? Expected, string? Refusal, string Module1, (string Name, string Code)[] Modules,
        (string Name, string Code)[] Classes);

    private static readonly Dictionary<string, Program> Programs = new()
    {
        ["class-dim-dim"] = new(null, "Ambiguous name detected: K", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim o As New Class1
                L "ran"
            End Sub

            Main
            """""", [], [("Class1", """"""
                Dim K
                Dim K
                """""")]),
        ["class-dim-sub"] = new(null, "Ambiguous name detected: K", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim o As New Class1
                L "ran"
            End Sub

            Main
            """""", [], [("Class1", """"""
                Dim K

                Sub K()
                End Sub
                """""")]),
        ["class-property-dim"] = new(null, "Ambiguous name detected: K", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim o As New Class1
                L "ran"
            End Sub

            Main
            """""", [], [("Class1", """"""
                Dim K

                Property Get K() As Long
                End Property
                """""")]),
        ["class-property-get-get"] = new(null, "Ambiguous name detected: K", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim o As New Class1
                L "ran"
            End Sub

            Main
            """""", [], [("Class1", """"""
                Property Get K() As Long
                End Property

                Property Get K() As Long
                End Property
                """""")]),
        ["class-property-pair"] = new("""
            K: 3
            """, null, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim o As New Class1
                o.K = 3
                L "K: " & o.K
            End Sub

            Main
            """""", [], [("Class1", """"""
                Dim m As Long

                Property Get K() As Long
                    K = m
                End Property

                Property Let K(ByVal v As Long)
                    m = v
                End Property
                """""")]),
        ["m-const-enum"] = new(null, "Ambiguous name detected: K", """"""
            Const K = 1
            Enum E
                K
            End Enum

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "K: " & K
            End Sub

            Main
            """""", [], []),
        ["m-declare-dim"] = new(null, "Ambiguous name detected: K", """"""
            Private Declare Function K Lib "kernel32" Alias "GetTickCount" () As Long
            Dim K

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-dim-case"] = new(null, "Ambiguous name detected: k", """"""
            Dim K
            Dim k

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-dim-dim-types"] = new(null, "Ambiguous name detected: K", """"""
            Dim K As Long
            Dim K As String

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-dim-enumname"] = new("""
            ran
            """, null, """"""
            Dim E
            Enum E
                A
            End Enum

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-dim-function"] = new(null, "Ambiguous name detected: K", """"""
            Dim K

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Function K()
            End Function

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-dim-L"] = new(null, "Ambiguous name detected: L", """"""
            Dim L

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-dim-sub-main"] = new(null, "Ambiguous name detected: Main", """"""
            Dim Main

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-dim-sub"] = new(null, "Ambiguous name detected: K", """"""
            Dim K

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub K()
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-dim-type"] = new("""
            ran
            """, null, """"""
            Dim T
            Type T
                X As Long
            End Type

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-enum-enum"] = new("""
            ran
            """, null, """"""
            Enum E
                A
            End Enum
            Enum F
                A = 5
            End Enum

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-enum-enum-name"] = new(null, "Ambiguous name detected: E", """"""
            Enum E
                A
            End Enum
            Enum E
                B
            End Enum

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-enum-enum-used"] = new(null, "Ambiguous name detected: A", """"""
            Enum E
                A
            End Enum
            Enum F
                A = 5
            End Enum

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "A: " & A
            End Sub

            Main
            """""", [], []),
        ["m-enum-twice-member"] = new(null, "Ambiguous name detected: A", """"""
            Enum E
                A
                A
            End Enum

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-private-public"] = new(null, "Ambiguous name detected: K", """"""
            Private K As Long
            Public K As Long

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-publicconst-private"] = new(null, "Ambiguous name detected: K", """"""
            Public Const K = 1
            Private K As Long

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-sub-function"] = new(null, "Ambiguous name detected: K", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub K()
            End Sub

            Function K()
            End Function

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-sub-sub"] = new(null, "Ambiguous name detected: K", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub K()
            End Sub

            Sub K()
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-type-type"] = new(null, "Ambiguous name detected: T", """"""
            Type T
                X As Long
            End Type
            Type T
                Y As Long
            End Type

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-const-dim"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Const n = 1
                Dim n
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-dimarr-redim"] = new("""
            UBound: 3
            """, null, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim n() As Long
                ReDim n(3)
                L "UBound: " & UBound(n)
            End Sub

            Main
            """""", [], []),
        ["p-dim-case"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim n
                Dim N
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-dim-const"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim n
                Const n = 1
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-dim-dim"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim n As Long
                Dim n As Long
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-dim-dim-types"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim n As Long
                Dim n As String
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-dim-label"] = new("""
            ran
            """, null, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim n
            n:
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-dim-module-shadow"] = new("""
            n: Long 5
            """, null, """"""
            Dim n As String

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim n As Long
                n = 5
                L "n: " & TypeName(n) & " " & n
            End Sub

            Main
            """""", [], []),
        ["p-dim-other-sub"] = new("""
            ran
            """, null, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub S()
            End Sub

            Sub Main()
                Dim S
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-dim-pct"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim n%
                Dim n
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-dim-static"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim n As Long
                Static n As Long
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-dim-sub-name"] = new("""
            ran
            """, null, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim Main
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-function-dim"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Function F()
                Dim F
                F = 3
            End Function

            Sub Main()
                L "F: " & F()
            End Sub

            Main
            """""", [], []),
        ["p-if-else"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                If True Then
                    Dim n As Long
                Else
                    Dim n As String
                End If
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-one-statement"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim n, n
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-param-const"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub S(n)
                Const n = 2
            End Sub

            Sub Main()
                S 1
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-param-dim"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub S(n)
                Dim n
            End Sub

            Sub Main()
                S 1
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-param-param"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub S(n, n)
            End Sub

            Sub Main()
                S 1, 2
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-static-static"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Static n As Long
                Static n As Long
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["x-public-public"] = new("""
            ran
            """, null, """"""
            Public K As Long

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [("Module2", """"""
                Public K As Long
                """""")], []),
        ["x-public-public-own"] = new("""
            K: 4 Module2.K: 0
            """, null, """"""
            Public K As Long

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                K = 4
                L "K: " & K & " Module2.K: " & Module2.K
            End Sub

            Main
            """""", [("Module2", """"""
                Public K As Long
                """""")], []),
        ["x-public-public-third"] = new("""
            K: 7
            """, null, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "K: " & Module3Read()
            End Sub

            Main
            """""", [("Module2", """"""
                Public K As Long
                """"""), ("Module3", """"""
                Public K As Long

                Public Function Module3Read() As Long
                    K = 7
                    Module3Read = K
                End Function
                """""")], []),
        ["x-public-sub-sub"] = new("""
            ran
            """, null, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [("Module2", """"""
                Public Sub K()
                End Sub
                """"""), ("Module3", """"""
                Public Sub K()
                End Sub
                """""")], []),
        ["x-two-publics-read"] = new(null, "Ambiguous name detected: K", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "K: " & K
            End Sub

            Main
            """""", [("Module2", """"""
                Public K As Long
                """"""), ("Module3", """"""
                Public K As Long
                """""")], []),
        ["m-const-const-same"] = new(null, "Ambiguous name detected: K", """"""
            Const K = 1
            Const K = 1

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-dim-dim"] = new(null, "Ambiguous name detected: K", """"""
            Dim K
            Dim K

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-dim-enum-member"] = new(null, "Ambiguous name detected: A", """"""
            Dim A
            Enum E
                A
            End Enum

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-enum-member-sub"] = new(null, "Ambiguous name detected: A", """"""
            Enum E
                A
            End Enum

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub A()
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-enum-type-same"] = new(null, "Ambiguous name detected: E", """"""
            Enum E
                A
            End Enum
            Type E
                X As Long
            End Type

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-one-statement"] = new(null, "Ambiguous name detected: K", """"""
            Dim K, K

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-type-member-dim"] = new("""
            ran
            """, null, """"""
            Type T
                X As Long
            End Type
            Dim X

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["x-public-dim-sub"] = new("""
            ran
            """, null, """"""
            Public K As Long

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [("Module2", """"""
                Public Sub K()
                End Sub
                """""")], []),
        ["x-public-sub-call"] = new(null, "Ambiguous name detected: K", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                K
                L "ran"
            End Sub

            Main
            """""", [("Module2", """"""
                Public Sub K()
                End Sub
                """"""), ("Module3", """"""
                Public Sub K()
                End Sub
                """""")], []),
        ["x-public-sub-own"] = new("""
            own K
            ran
            """, null, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Private Sub K()
                L "own K"
            End Sub

            Sub Main()
                K
                L "ran"
            End Sub

            Main
            """""", [("Module2", """"""
                Public Sub K()
                End Sub
                """""")], []),
        ["x-two-public-consts-unused"] = new("""
            ran
            """, null, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [("Module2", """"""
                Public Const K = 1
                """"""), ("Module3", """"""
                Public Const K = 2
                """""")], []),
        ["class-event-dim"] = new("""
            ran
            """, null, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim o As New Class1
                L "ran"
            End Sub

            Main
            """""", [], [("Class1", """"""
                Event K()
                Dim K
                """""")]),
        ["class-event-sub"] = new("""
            ran
            """, null, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim o As New Class1
                L "ran"
            End Sub

            Main
            """""", [], [("Class1", """"""
                Event K()

                Sub K()
                End Sub
                """""")]),
        ["class-get-dim-inside"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim o As New Class1
                L "K: " & o.K
            End Sub

            Main
            """""", [], [("Class1", """"""
                Property Get K() As Long
                    Dim K
                End Property
                """""")]),
        ["class-get-sub"] = new(null, "Ambiguous name detected: K", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim o As New Class1
                L "ran"
            End Sub

            Main
            """""", [], [("Class1", """"""
                Property Get K() As Long
                End Property

                Sub K()
                End Sub
                """""")]),
        ["class-let-let"] = new(null, "Ambiguous name detected: K", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim o As New Class1
                L "ran"
            End Sub

            Main
            """""", [], [("Class1", """"""
                Property Let K(ByVal v As Long)
                End Property

                Property Let K(ByVal v As Long)
                End Property
                """""")]),
        ["class-let-param-dim"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim o As New Class1
                o.K = 1
                L "ran"
            End Sub

            Main
            """""", [], [("Class1", """"""
                Property Let K(ByVal v As Long)
                    Dim v
                End Property
                """""")]),
        ["class-let-set"] = new("""
            ran
            """, null, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim o As New Class1
                L "ran"
            End Sub

            Main
            """""", [], [("Class1", """"""
                Property Let K(ByVal v As Variant)
                End Property

                Property Set K(ByVal v As Object)
                End Property
                """""")]),
        ["m-declare-declare"] = new(null, "Ambiguous name detected: K", """"""
            Private Declare Function K Lib "kernel32" Alias "GetTickCount" () As Long
            Private Declare Function K Lib "kernel32" Alias "GetTickCount" () As Long

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-declare-sub"] = new(null, "Ambiguous name detected: K", """"""
            Private Declare Function K Lib "kernel32" Alias "GetTickCount" () As Long

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub K()
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-enum-name-const"] = new("""
            ran
            """, null, """"""
            Const E = 1
            Enum E
                A
            End Enum

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-enum-name-sub"] = new("""
            ran
            """, null, """"""
            Enum E
                A
            End Enum

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub E()
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["m-type-name-sub"] = new("""
            ran
            """, null, """"""
            Type T
                X As Long
            End Type

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub T()
            End Sub

            Sub Main()
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-function-const-own-name"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Function F()
                Const F = 1
            End Function

            Sub Main()
                L "F: " & F()
            End Sub

            Main
            """""", [], []),
        ["p-function-param-own-name"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Function F(F)
            End Function

            Sub Main()
                L "F: " & F(1)
            End Sub

            Main
            """""", [], []),
        ["p-param-static"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub S(n)
                Static n
            End Sub

            Sub Main()
                S 1
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-static-const"] = new(null, "Duplicate declaration in current scope", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Static n
                Const n = 1
                L "ran"
            End Sub

            Main
            """""", [], []),
        ["p-sub-dim-own-name"] = new("""
            ran
            """, null, """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub S()
                Dim S
            End Sub

            Sub Main()
                S
                L "ran"
            End Sub

            Main
            """""", [], []),
    };
}
