using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Interpreter.ConditionalCompilation;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Conditional compilation, <c>#If</c> and <c>#Const</c> against the project's arguments and VB6's built-ins (#1112). Each
/// program is a probe vb6.exe built with <c>/make</c>, native and p-code agreeing, with its <c>L</c> printing where the probe's
/// appended to a log, its own modules and its <c>.vbp</c>'s <c>CondComp</c>. Each transcript is that log, and each error
/// vb6.exe's (the oracle's "Conditional compilation").
/// </summary>
public class ConditionalCompilationTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    private async Task RunProgram(string? condComp, string module1, string? module2 = null)
    {
        CompilationArguments.TryParse(condComp, out var arguments).Should().BeTrue();
        Compilation = new CompilationConstants(arguments!);
        if (module2 is null)
            await Run(module1);
        else
            await RunModules(module1, ("Module2", module2));
    }

    // The second batch's main probe: an undefined name, the built-ins, the operators, comparison as text, #Const at module level
    // and in a procedure, the project's arguments, another module, a comment after Then, nesting and #ElseIf.
    [Fact]
    public async Task Every_condition_selects_the_branch_vb6_exe_selected()
    {
        await RunProgram("ARG = 5 : FLAG = 1", """"""
            #Const A = 1
            #Const S = "x"
            #Const B = A + 1
            #Const C = Not A
            #Const D = Win32
            #Const E = Nope
            Sub Main()
            #If Foo Then
                L "Foo: true"
            #Else
                L "Foo: false"
            #End If
            #If Not Foo Then
                L "Not Foo: true"
            #Else
                L "Not Foo: false"
            #End If
            #If Foo = 0 Then
                L "Foo = 0: true"
            #Else
                L "Foo = 0: false"
            #End If
            #If Foo = "" Then
                L "Foo = """": true"
            #Else
                L "Foo = """": false"
            #End If
            #If Win16 Then
                L "Win16: true"
            #Else
                L "Win16: false"
            #End If
            #If Win32 Then
                L "Win32: true"
            #Else
                L "Win32: false"
            #End If
            #If Not Win32 Then
                L "Not Win32: true"
            #Else
                L "Not Win32: false"
            #End If
            #If Win32 = 1 Then
                L "Win32 = 1: true"
            #Else
                L "Win32 = 1: false"
            #End If
            #If Win32 = -1 Then
                L "Win32 = -1: true"
            #Else
                L "Win32 = -1: false"
            #End If
            #If Win32 = True Then
                L "Win32 = True: true"
            #Else
                L "Win32 = True: false"
            #End If
            #If Not Win16 Then
                L "Not Win16: true"
            #Else
                L "Not Win16: false"
            #End If
            #If VBA6 Then
                L "VBA6: true"
            #Else
                L "VBA6: false"
            #End If
            #If VBA7 Then
                L "VBA7: true"
            #Else
                L "VBA7: false"
            #End If
            #If Win64 Then
                L "Win64: true"
            #Else
                L "Win64: false"
            #End If
            #If Mac Then
                L "Mac: true"
            #Else
                L "Mac: false"
            #End If
            #If Not Win64 Then
                L "Not Win64: true"
            #Else
                L "Not Win64: false"
            #End If
            #If 1 + 1 = 2 Then
                L "1 + 1 = 2: true"
            #Else
                L "1 + 1 = 2: false"
            #End If
            #If 5 \ 2 = 2 Then
                L "5 \ 2 = 2: true"
            #Else
                L "5 \ 2 = 2: false"
            #End If
            #If 7 Mod 3 = 1 Then
                L "7 Mod 3 = 1: true"
            #Else
                L "7 Mod 3 = 1: false"
            #End If
            #If 2 ^ 3 = 8 Then
                L "2 ^ 3 = 8: true"
            #Else
                L "2 ^ 3 = 8: false"
            #End If
            #If 7 / 2 = 3.5 Then
                L "7 / 2 = 3.5: true"
            #Else
                L "7 / 2 = 3.5: false"
            #End If
            #If -1 Then
                L "-1: true"
            #Else
                L "-1: false"
            #End If
            #If 0 Then
                L "0: true"
            #Else
                L "0: false"
            #End If
            #If 0.4 Then
                L "0.4: true"
            #Else
                L "0.4: false"
            #End If
            #If 1 And 2 Then
                L "1 And 2: true"
            #Else
                L "1 And 2: false"
            #End If
            #If 1 Or 2 Then
                L "1 Or 2: true"
            #Else
                L "1 Or 2: false"
            #End If
            #If 3 Xor 3 Then
                L "3 Xor 3: true"
            #Else
                L "3 Xor 3: false"
            #End If
            #If Not 0 Then
                L "Not 0: true"
            #Else
                L "Not 0: false"
            #End If
            #If Not 1 Then
                L "Not 1: true"
            #Else
                L "Not 1: false"
            #End If
            #If Not -1 Then
                L "Not -1: true"
            #Else
                L "Not -1: false"
            #End If
            #If True Then
                L "True: true"
            #Else
                L "True: false"
            #End If
            #If False Then
                L "False: true"
            #Else
                L "False: false"
            #End If
            #If True = -1 Then
                L "True = -1: true"
            #Else
                L "True = -1: false"
            #End If
            #If 1 Eqv 1 Then
                L "1 Eqv 1: true"
            #Else
                L "1 Eqv 1: false"
            #End If
            #If 0 Imp 0 Then
                L "0 Imp 0: true"
            #Else
                L "0 Imp 0: false"
            #End If
            #If "a" = "a" Then
                L """a"" = ""a"": true"
            #Else
                L """a"" = ""a"": false"
            #End If
            #If "a" & "b" = "ab" Then
                L """a"" & ""b"" = ""ab"": true"
            #Else
                L """a"" & ""b"" = ""ab"": false"
            #End If
            #If "B" > "a" Then
                L """B"" > ""a"": true"
            #Else
                L """B"" > ""a"": false"
            #End If
            #If "10" < "9" Then
                L """10"" < ""9"": true"
            #Else
                L """10"" < ""9"": false"
            #End If
            #If "a" = "A" Then
                L """a"" = ""A"": true"
            #Else
                L """a"" = ""A"": false"
            #End If
            #If &H10 = 16 Then
                L "&H10 = 16: true"
            #Else
                L "&H10 = 16: false"
            #End If
            #If 1.5 = 1.5 Then
                L "1.5 = 1.5: true"
            #Else
                L "1.5 = 1.5: false"
            #End If
            #If 2 > 1 And 3 > 2 Then
                L "2 > 1 And 3 > 2: true"
            #Else
                L "2 > 1 And 3 > 2: false"
            #End If
            #If 1 <> 2 Then
                L "1 <> 2: true"
            #Else
                L "1 <> 2: false"
            #End If
            #If 2 >= 2 Then
                L "2 >= 2: true"
            #Else
                L "2 >= 2: false"
            #End If
            #If A Then
                L "A: true"
            #Else
                L "A: false"
            #End If
            #If B = 2 Then
                L "B = 2: true"
            #Else
                L "B = 2: false"
            #End If
            #If S = "x" Then
                L "S = ""x"": true"
            #Else
                L "S = ""x"": false"
            #End If
            #If C Then
                L "C: true"
            #Else
                L "C: false"
            #End If
            #If C = -2 Then
                L "C = -2: true"
            #Else
                L "C = -2: false"
            #End If
            #If D = Win32 Then
                L "D = Win32: true"
            #Else
                L "D = Win32: false"
            #End If
            #If E = 0 Then
                L "E = 0: true"
            #Else
                L "E = 0: false"
            #End If
            #If E = "" Then
                L "E = """": true"
            #Else
                L "E = """": false"
            #End If
            #If a = 1 Then
                L "a = 1: true"
            #Else
                L "a = 1: false"
            #End If
            #If ARG = 5 Then
                L "ARG = 5: true"
            #Else
                L "ARG = 5: false"
            #End If
            #If FLAG Then
                L "FLAG: true"
            #Else
                L "FLAG: false"
            #End If
            #If ARG = "5" Then
                L "ARG = ""5"": true"
            #Else
                L "ARG = ""5"": false"
            #End If
            #If LATE Then
                L "LATE before its #Const: true"
            #Else
                L "LATE before its #Const: false"
            #End If
            #Const LATE = 1
            #If LATE Then
                L "LATE after its #Const: true"
            #Else
                L "LATE after its #Const: false"
            #End If
            #Const P = 1
                Other
                Module2Rows
            #If 1 Then ' a comment
                L "comment after Then: compiled"
            #End If
            #If 1 Then
            #If 0 Then
                L "nested: inner true"
            #Else
                L "nested: inner false"
            #End If
            #End If
            #If 0 Then
                L "elseif: first"
            #ElseIf 1 Then
                L "elseif: second"
            #ElseIf 1 Then
                L "elseif: third"
            #Else
                L "elseif: else"
            #End If
                L "end"
            End Sub
            Sub Other()
            #If P Then
                L "P from Main, read in Other: true"
            #Else
                L "P from Main, read in Other: false"
            #End If
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""", """"""
            Public Sub Module2Rows()
            #If A Then
                L "Module2 reads Module1 A: true"
            #Else
                L "Module2 reads Module1 A: false"
            #End If
            #If ARG = 5 Then
                L "Module2 reads ARG: true"
            #Else
                L "Module2 reads ARG: false"
            #End If
            End Sub

            """""");

        AssertDebugLog(Lines(""""""
            Foo: false
            Not Foo: true
            Foo = 0: true
            Foo = "": false
            Win16: false
            Win32: true
            Not Win32: true
            Win32 = 1: true
            Win32 = -1: false
            Win32 = True: false
            Not Win16: true
            VBA6: false
            VBA7: false
            Win64: false
            Mac: false
            Not Win64: true
            1 + 1 = 2: true
            5 \ 2 = 2: true
            7 Mod 3 = 1: true
            2 ^ 3 = 8: true
            7 / 2 = 3.5: true
            -1: true
            0: false
            0.4: true
            1 And 2: false
            1 Or 2: true
            3 Xor 3: false
            Not 0: true
            Not 1: true
            Not -1: false
            True: true
            False: false
            True = -1: true
            1 Eqv 1: true
            0 Imp 0: true
            "a" = "a": true
            "a" & "b" = "ab": true
            "B" > "a": true
            "10" < "9": true
            "a" = "A": true
            &H10 = 16: true
            1.5 = 1.5: true
            2 > 1 And 3 > 2: true
            1 <> 2: true
            2 >= 2: true
            A: true
            B = 2: true
            S = "x": true
            C: true
            C = -2: true
            D = Win32: true
            E = 0: true
            E = "": false
            a = 1: true
            ARG = 5: true
            FLAG: true
            ARG = "5": true
            LATE before its #Const: false
            LATE after its #Const: true
            P from Main, read in Other: true
            Module2 reads Module1 A: false
            Module2 reads ARG: true
            comment after Then: compiled
            nested: inner false
            elseif: second
            end
            """"""));
    }

    // The fourth batch's arithmetic: precedence, banker's rounding in \, Mod's sign, and Not below the comparisons.
    // Its `-2 ^ 2` row is left out: HexIDE reads the minus as part of the number there, as everywhere else (#1073).
    [Fact]
    public async Task An_expression_is_evaluated_as_vb6_exe_evaluated_it()
    {
        await RunProgram(null, """"""
            Sub Main()
            #If True + True = -2 Then
                L "True + True = -2: true"
            #Else
                L "True + True = -2: false"
            #End If
            #If "1" + 1 = 2 Then
                L """1"" + 1 = 2: true"
            #Else
                L """1"" + 1 = 2: false"
            #End If
            #If "1" & 1 = "11" Then
                L """1"" & 1 = ""11"": true"
            #Else
                L """1"" & 1 = ""11"": false"
            #End If
            #If #1/2/2000# > #1/1/2000# Then
                L "#1/2/2000# > #1/1/2000#: true"
            #Else
                L "#1/2/2000# > #1/1/2000#: false"
            #End If
            #If 2 + 3 * 4 = 14 Then
                L "2 + 3 * 4 = 14: true"
            #Else
                L "2 + 3 * 4 = 14: false"
            #End If
            #If 10 - 2 - 3 = 5 Then
                L "10 - 2 - 3 = 5: true"
            #Else
                L "10 - 2 - 3 = 5: false"
            #End If
            #If 7 \ 2 * 2 = 6 Then
                L "7 \ 2 * 2 = 6: true"
            #Else
                L "7 \ 2 * 2 = 6: false"
            #End If
            #If 7 \ 2 * 2 = 1 Then
                L "7 \ 2 * 2 = 1: true"
            #Else
                L "7 \ 2 * 2 = 1: false"
            #End If
            #If 2.5 \ 1 = 2 Then
                L "2.5 \ 1 = 2: true"
            #Else
                L "2.5 \ 1 = 2: false"
            #End If
            #If 3.5 \ 1 = 4 Then
                L "3.5 \ 1 = 4: true"
            #Else
                L "3.5 \ 1 = 4: false"
            #End If
            #If -7 Mod 3 = -1 Then
                L "-7 Mod 3 = -1: true"
            #Else
                L "-7 Mod 3 = -1: false"
            #End If
            #If 1 < 2 = True Then
                L "1 < 2 = True: true"
            #Else
                L "1 < 2 = True: false"
            #End If
            #If Not 1 = 2 Then
                L "Not 1 = 2: true"
            #Else
                L "Not 1 = 2: false"
            #End If
            #If (Not 1) = -2 Then
                L "(Not 1) = -2: true"
            #Else
                L "(Not 1) = -2: false"
            #End If
            #If 1 = 1 = True Then
                L "1 = 1 = True: true"
            #Else
                L "1 = 1 = True: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""");

        AssertDebugLog(Lines(""""""
            True + True = -2: true
            "1" + 1 = 2: true
            "1" & 1 = "11": true
            #1/2/2000# > #1/1/2000#: true
            2 + 3 * 4 = 14: true
            10 - 2 - 3 = 5: true
            7 \ 2 * 2 = 6: false
            7 \ 2 * 2 = 1: true
            2.5 \ 1 = 2: true
            3.5 \ 1 = 4: true
            -7 Mod 3 = -1: true
            1 < 2 = True: true
            Not 1 = 2: true
            (Not 1) = -2: true
            1 = 1 = True: true
            compiled
            """"""));
    }

    public static TheoryData<string> ProgramNames => [.. Programs.Keys];

    // Every other probe that vb6.exe built, one row or shape each.
    [Theory]
    [MemberData(nameof(ProgramNames))]
    public async Task A_program_runs_as_vb6_exe_ran_it(string name)
    {
        var (condComp, module1, module2, expected) = Programs[name];

        await RunProgram(condComp, module1, module2);

        AssertDebugLog(Lines(expected));
    }

    public static TheoryData<string> RefusedNames => [.. Refused.Keys];

    // Every probe vb6.exe refused to build, refused here as the module loads. A null message is a line that is not a
    // directive, which both compile as code and refuse as a syntax error, each in its own words.
    [Theory]
    [MemberData(nameof(RefusedNames))]
    public async Task A_program_vb6_exe_refused_is_refused(string name)
    {
        var (message, module1) = Refused[name];

        var run = () => RunProgram(null, module1);

        var refusal = await run.Should().ThrowAsync<VBCompileErrorException>();
        if (message is not null)
            refusal.WithMessage($"*{message}*");
    }

    // Like in a directive (#1598), measured on vb6.exe, native and p-code alike, in a Binary module and a Text one: it
    // matches as text in both, as a directive's other comparisons do, and a directive orders text as vb6.exe does. Each
    // expected line is what vb6.exe logged.
    [Fact]
    public async Task Like_matches_as_text_in_either_module_and_text_orders_as_vb6_exe_orders_it()
    {
        await RunProgram(null, """"""
            Option Compare Binary

            #Const KLike = "abc" Like "a*"

            #If "a" Like "A" Then
            Public Const B0 As String = "True"
            #Else
            Public Const B0 As String = "False"
            #End If
            #If "M" Like "[a-z]" Then
            Public Const B1 As String = "True"
            #Else
            Public Const B1 As String = "False"
            #End If
            #If "_" Like "[A-z]" Then
            Public Const B2 As String = "True"
            #Else
            Public Const B2 As String = "False"
            #End If
            #If "abc" Like "a*" Then
            Public Const B3 As String = "True"
            #Else
            Public Const B3 As String = "False"
            #End If
            #If 5 Like "#" Then
            Public Const B4 As String = "True"
            #Else
            Public Const B4 As String = "False"
            #End If
            #If "abc" Like "x*" Then
            Public Const B5 As String = "True"
            #Else
            Public Const B5 As String = "False"
            #End If
            #If "" Like "[" Then
            Public Const B6 As String = "True"
            #Else
            Public Const B6 As String = "False"
            #End If
            #If CStr("a" Like "a") = "True" Then
            Public Const B7 As String = "True"
            #Else
            Public Const B7 As String = "False"
            #End If
            #If KLike = True Then
            Public Const B8 As String = "True"
            #Else
            Public Const B8 As String = "False"
            #End If
            #If CStr(KLike) = "True" Then
            Public Const B9 As String = "True"
            #Else
            Public Const B9 As String = "False"
            #End If
            #If KLike = -1 Then
            Public Const B10 As String = "True"
            #Else
            Public Const B10 As String = "False"
            #End If
            #If "_" < "a" Then
            Public Const B11 As String = "True"
            #Else
            Public Const B11 As String = "False"
            #End If
            #If "co-op" > "coop" Then
            Public Const B12 As String = "True"
            #Else
            Public Const B12 As String = "False"
            #End If
            #If "it's" > "its" Then
            Public Const B13 As String = "True"
            #Else
            Public Const B13 As String = "False"
            #End If
            #If "a" < "B" Then
            Public Const B14 As String = "True"
            #Else
            Public Const B14 As String = "False"
            #End If
            #If "abc" = "ABC" Then
            Public Const B15 As String = "True"
            #Else
            Public Const B15 As String = "False"
            #End If

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "binary like-case: " & B0
                L "text like-case: " & Module2.T0
                L "binary like-range-text: " & B1
                L "text like-range-text: " & Module2.T1
                L "binary like-range-binary: " & B2
                L "text like-range-binary: " & Module2.T2
                L "binary like-star: " & B3
                L "text like-star: " & Module2.T3
                L "binary like-digit: " & B4
                L "text like-digit: " & Module2.T4
                L "binary like-false: " & B5
                L "text like-false: " & Module2.T5
                L "binary like-empty-bad-pattern: " & B6
                L "text like-empty-bad-pattern: " & Module2.T6
                L "binary like-cstr: " & B7
                L "text like-cstr: " & Module2.T7
                L "binary like-const-true: " & B8
                L "text like-const-true: " & Module2.T8
                L "binary like-const-cstr: " & B9
                L "text like-const-cstr: " & Module2.T9
                L "binary like-const-minus1: " & B10
                L "text like-const-minus1: " & Module2.T10
                L "binary text-underscore: " & B11
                L "text text-underscore: " & Module2.T11
                L "binary text-hyphen: " & B12
                L "text text-hyphen: " & Module2.T12
                L "binary text-apostrophe: " & B13
                L "text text-apostrophe: " & Module2.T13
                L "binary text-case-order: " & B14
                L "text text-case-order: " & Module2.T14
                L "binary text-equal-case: " & B15
                L "text text-equal-case: " & Module2.T15
            End Sub

            Main
            """""", """"""
            Option Compare Text

            #Const KLike = "abc" Like "a*"

            #If "a" Like "A" Then
            Public Const T0 As String = "True"
            #Else
            Public Const T0 As String = "False"
            #End If
            #If "M" Like "[a-z]" Then
            Public Const T1 As String = "True"
            #Else
            Public Const T1 As String = "False"
            #End If
            #If "_" Like "[A-z]" Then
            Public Const T2 As String = "True"
            #Else
            Public Const T2 As String = "False"
            #End If
            #If "abc" Like "a*" Then
            Public Const T3 As String = "True"
            #Else
            Public Const T3 As String = "False"
            #End If
            #If 5 Like "#" Then
            Public Const T4 As String = "True"
            #Else
            Public Const T4 As String = "False"
            #End If
            #If "abc" Like "x*" Then
            Public Const T5 As String = "True"
            #Else
            Public Const T5 As String = "False"
            #End If
            #If "" Like "[" Then
            Public Const T6 As String = "True"
            #Else
            Public Const T6 As String = "False"
            #End If
            #If CStr("a" Like "a") = "True" Then
            Public Const T7 As String = "True"
            #Else
            Public Const T7 As String = "False"
            #End If
            #If KLike = True Then
            Public Const T8 As String = "True"
            #Else
            Public Const T8 As String = "False"
            #End If
            #If CStr(KLike) = "True" Then
            Public Const T9 As String = "True"
            #Else
            Public Const T9 As String = "False"
            #End If
            #If KLike = -1 Then
            Public Const T10 As String = "True"
            #Else
            Public Const T10 As String = "False"
            #End If
            #If "_" < "a" Then
            Public Const T11 As String = "True"
            #Else
            Public Const T11 As String = "False"
            #End If
            #If "co-op" > "coop" Then
            Public Const T12 As String = "True"
            #Else
            Public Const T12 As String = "False"
            #End If
            #If "it's" > "its" Then
            Public Const T13 As String = "True"
            #Else
            Public Const T13 As String = "False"
            #End If
            #If "a" < "B" Then
            Public Const T14 As String = "True"
            #Else
            Public Const T14 As String = "False"
            #End If
            #If "abc" = "ABC" Then
            Public Const T15 As String = "True"
            #Else
            Public Const T15 As String = "False"
            #End If
            """""");

        AssertDebugLog(Lines("""
            binary like-case: True
            text like-case: True
            binary like-range-text: True
            text like-range-text: True
            binary like-range-binary: False
            text like-range-binary: False
            binary like-star: True
            text like-star: True
            binary like-digit: True
            text like-digit: True
            binary like-false: False
            text like-false: False
            binary like-empty-bad-pattern: False
            text like-empty-bad-pattern: False
            binary like-cstr: True
            text like-cstr: True
            binary like-const-true: True
            text like-const-true: True
            binary like-const-cstr: True
            text like-const-cstr: True
            binary like-const-minus1: True
            text like-const-minus1: True
            binary text-underscore: True
            text text-underscore: True
            binary text-hyphen: True
            text text-hyphen: True
            binary text-apostrophe: True
            text text-apostrophe: True
            binary text-case-order: True
            text text-case-order: True
            binary text-equal-case: True
            text text-equal-case: True
            """));
    }

    // What vb6.exe refuses to build, a Like pattern it cannot read or Null, measured one project each (#1598).
    [Theory]
    [InlineData("c-bad-pattern")]
    [InlineData("c-const-bad-pattern")]
    [InlineData("c-like-null")]
    [InlineData("c-null-like")]
    [InlineData("c-reversed-range")]
    public async Task A_Like_vb6_exe_refuses_is_refused_as_the_module_loads(string name)
    {
        var (message, module1) = LikeRefused[name];

        var run = () => RunProgram(null, module1);

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage($"*{message}*");
    }

    private static readonly Dictionary<string, (string Message, string Module1)> LikeRefused = new()
    {
        ["c-bad-pattern"] = ("Invalid pattern string", """
            #If "a" Like "[" Then
            Public Const R As String = "True"
            #Else
            Public Const R As String = "False"
            #End If

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran: " & R
            End Sub

            Main
            """),
        ["c-const-bad-pattern"] = ("Invalid pattern string", """
            #Const KBad = "a" Like "["

            #If KBad Then
            Public Const R As String = "True"
            #Else
            Public Const R As String = "False"
            #End If

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran: " & R
            End Sub

            Main
            """),
        ["c-like-null"] = ("Invalid use of Null", """
            #If "a" Like Null Then
            Public Const R As String = "True"
            #Else
            Public Const R As String = "False"
            #End If

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran: " & R
            End Sub

            Main
            """),
        ["c-null-like"] = ("Invalid use of Null", """
            #If Null Like "a" Then
            Public Const R As String = "True"
            #Else
            Public Const R As String = "False"
            #End If

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran: " & R
            End Sub

            Main
            """),
        ["c-reversed-range"] = ("Invalid pattern string", """
            #If "b" Like "[z-a]" Then
            Public Const R As String = "True"
            #Else
            Public Const R As String = "False"
            #End If

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                L "ran: " & R
            End Sub

            Main
            """),
    };

    // vb6.exe accepts CVErr in an #If, where CVErr(5) = 5 is Type mismatch (#1112, #2520).
    [Fact]
    public async Task CVErr_compared_with_a_number_is_type_mismatch()
    {
        var run = () => RunProgram(null, """"""
            Sub Main()
            #If CVErr(5) = 5 Then
                L "CVErr(5) = 5: true"
            #Else
                L "CVErr(5) = 5: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Type mismatch");
    }

    private static readonly Dictionary<string, (string? CondComp, string Module1, string? Module2, string Expected)> Programs = new()
    {
        ["1/s-continuation"] = new(null, """"""
            Sub Main()
            #If 1 _
                And 1 Then
                L "continued: true"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            continued: true
            compiled
            """"""),
        ["1/s-date-literal"] = new(null, """"""
            Sub Main()
            #If #1/1/2000# = #1/1/2000# Then
                L "#1/1/2000# = #1/1/2000#: true"
            #Else
                L "#1/1/2000# = #1/1/2000#: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            #1/1/2000# = #1/1/2000#: true
            compiled
            """"""),
        ["1/s-else-space-if"] = new(null, """"""
            Sub Main()
            #If 0 Then
                L "first"
            #Else If 1 Then
                L "else if: second"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            compiled
            """"""),
        ["1/s-empty-keyword"] = new(null, """"""
            Sub Main()
            #If Empty Then
                L "Empty: true"
            #Else
                L "Empty: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Empty: false
            compiled
            """"""),
        ["1/s-function-call"] = new(null, """"""
            Sub Main()
            #If Len("abc") = 3 Then
                L "Len(""abc"") = 3: true"
            #Else
                L "Len(""abc"") = 3: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Len("abc") = 3: true
            compiled
            """"""),
        ["1/s-inactive-invalid"] = new(null, """"""
            #If 0 Then
            this is not valid vb ((
            #End If
            Sub Main()
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            compiled
            """"""),
        ["1/s-inactive-invalid-proc"] = new(null, """"""
            Sub Main()
            #If 0 Then
                x = = 1
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            compiled
            """"""),
        ["1/s-redefine-arg"] = new("ARG = 5", """"""
            #Const ARG = 7
            Sub Main()
            #If ARG = 7 Then
                L "ARG after #Const ARG = 7: true"
            #Else
                L "ARG after #Const ARG = 7: false"
            #End If
            #If ARG = 5 Then
                L "ARG still 5: true"
            #Else
                L "ARG still 5: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            ARG after #Const ARG = 7: true
            ARG still 5: false
            compiled
            """"""),
        ["1/s-redefine-builtin"] = new(null, """"""
            #Const Win32 = 0
            Sub Main()
            #If Win32 Then
                L "Win32 after #Const Win32 = 0: true"
            #Else
                L "Win32 after #Const Win32 = 0: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Win32 after #Const Win32 = 0: false
            compiled
            """"""),
        ["1/s-string-vs-number"] = new(null, """"""
            Sub Main()
            #If "5" = 5 Then
                L """5"" = 5: true"
            #Else
                L """5"" = 5: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            "5" = 5: true
            compiled
            """"""),
        ["1/s-switched-header"] = new(null, """"""
            #If 1 Then
            Private Sub F(ByVal x As Integer)
            #Else
            Private Sub F()
            #End If
                L "F body"
            End Sub
            Sub Main()
                F 1
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            F body
            compiled
            """"""),
        ["2/cc-max"] = new("M = 32767", """"""
            Sub Main()
            #If M <> 0 Then
                L "M = 32767: M <> 0: true"
            #Else
                L "M = 32767: M <> 0: false"
            #End If
            #If M = -1 Then
                L "M = 32767: M = -1: true"
            #Else
                L "M = 32767: M = -1: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            M = 32767: M <> 0: true
            M = 32767: M = -1: false
            compiled
            """"""),
        ["2/cc-min"] = new("N = -32768", """"""
            Sub Main()
            #If N <> 0 Then
                L "N = -32768: N <> 0: true"
            #Else
                L "N = -32768: N <> 0: false"
            #End If
            #If N = -1 Then
                L "N = -32768: N = -1: true"
            #Else
                L "N = -32768: N = -1: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            N = -32768: N <> 0: true
            N = -32768: N = -1: false
            compiled
            """"""),
        ["2/cc-neg"] = new("NEG = -1", """"""
            Sub Main()
            #If NEG <> 0 Then
                L "NEG = -1: NEG <> 0: true"
            #Else
                L "NEG = -1: NEG <> 0: false"
            #End If
            #If NEG = -1 Then
                L "NEG = -1: NEG = -1: true"
            #Else
                L "NEG = -1: NEG = -1: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            NEG = -1: NEG <> 0: true
            NEG = -1: NEG = -1: true
            compiled
            """"""),
        ["2/cc-nospace"] = new("A=1:B=2", """"""
            Sub Main()
            #If A <> 0 Then
                L "A=1:B=2: A <> 0: true"
            #Else
                L "A=1:B=2: A <> 0: false"
            #End If
            #If A = -1 Then
                L "A=1:B=2: A = -1: true"
            #Else
                L "A=1:B=2: A = -1: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            A=1:B=2: A <> 0: true
            A=1:B=2: A = -1: false
            compiled
            """"""),
        ["2/cc-zero"] = new("ZERO = 0", """"""
            Sub Main()
            #If ZERO <> 0 Then
                L "ZERO = 0: ZERO <> 0: true"
            #Else
                L "ZERO = 0: ZERO <> 0: false"
            #End If
            #If ZERO = -1 Then
                L "ZERO = 0: ZERO = -1: true"
            #Else
                L "ZERO = 0: ZERO = -1: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            ZERO = 0: ZERO <> 0: false
            ZERO = 0: ZERO = -1: false
            compiled
            """"""),
        ["2/s-else-space-if-2"] = new(null, """"""
            Sub Main()
            #If 0 Then
                L "first(2)"
            #Else If 1 Then
                L "second(2)"
            #Else
                L "else(2)"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            else(2)
            compiled
            """"""),
        ["2/s-else-then-text"] = new(null, """"""
            Sub Main()
            #If 0 Then
                L "first(3)"
            #Else whatever
                L "else(3)"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            compiled
            """"""),
        ["2/s-fn-abs"] = new(null, """"""
            Sub Main()
            #If Abs(-1) = 1 Then
                L "Abs(-1) = 1: true"
            #Else
                L "Abs(-1) = 1: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Abs(-1) = 1: true
            compiled
            """"""),
        ["2/s-fn-cbool"] = new(null, """"""
            Sub Main()
            #If CBool(Win32) Then
                L "CBool(Win32): true"
            #Else
                L "CBool(Win32): false"
            #End If
            #If Not CBool(Win16) Then
                L "Not CBool(Win16): true"
            #Else
                L "Not CBool(Win16): false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            CBool(Win32): true
            Not CBool(Win16): true
            compiled
            """"""),
        ["2/s-fn-cint"] = new(null, """"""
            Sub Main()
            #If CInt("5") = 5 Then
                L "CInt(""5"") = 5: true"
            #Else
                L "CInt(""5"") = 5: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            CInt("5") = 5: true
            compiled
            """"""),
        ["2/s-fn-const"] = new(null, """"""
            Const KK = 3
            Sub Main()
            #If KK = 3 Then
                L "KK = 3: true"
            #Else
                L "KK = 3: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            KK = 3: false
            compiled
            """"""),
        ["2/s-inactive-decl-after-sub"] = new(null, """"""
            Sub Main()
                L "compiled"
            End Sub
            #If 0 Then
            Private Declare Function GetTickCount Lib "kernel32" () As Long
            #End If
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            compiled
            """"""),
        ["2/s-inactive-dim-after-sub"] = new(null, """"""
            Sub Main()
                L "compiled"
            End Sub
            #If 0 Then
            Dim zz As Integer
            #End If
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            compiled
            """"""),
        ["2/s-inactive-garbage-after-sub"] = new(null, """"""
            Sub Main()
                L "compiled"
            End Sub
            #If 0 Then
            this is not valid vb ((
            #End If
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            compiled
            """"""),
        ["2/s-inactive-ptrsafe"] = new(null, """"""
            #If VBA7 Then
            Private Declare PtrSafe Function GetTickCount Lib "kernel32" () As Long
            #Else
            Private Declare Function GetTickCount Lib "kernel32" () As Long
            #End If
            Sub Main()
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            compiled
            """"""),
        ["2/s-variable-const"] = new(null, """"""
            Dim v As Integer
            #Const K = v
            Sub Main()
            #If K = 0 Then
                L "K = 0: true"
            #Else
                L "K = 0: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            K = 0: true
            compiled
            """"""),
        ["3/cc-across"] = new("Q = 4", """"""
            Sub Main()
            #If Q = 4 Then
                L "Q in Module1: true"
            #Else
                L "Q in Module1: false"
            #End If
                Other2
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            """"""
            #Const Q = 9
            Public Sub Other2()
            #If Q = 9 Then
                L "Q in Module2 after its own #Const: true"
            #Else
                L "Q in Module2 after its own #Const: false"
            #End If
            End Sub

            """""",
            """"""
            Q in Module1: true
            Q in Module2 after its own #Const: true
            compiled
            """"""),
        ["3/cc-lower"] = new("x = 3", """"""
            Sub Main()
            #If X Then
                L "x = 3: X: true"
            #Else
                L "x = 3: X: false"
            #End If
            #If X = 1 Then
                L "x = 3: X = 1: true"
            #Else
                L "x = 3: X = 1: false"
            #End If
            #If Win32 Then
                L "x = 3: Win32: true"
            #Else
                L "x = 3: Win32: false"
            #End If
            #If VBA7 Then
                L "x = 3: VBA7: true"
            #Else
                L "x = 3: VBA7: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            x = 3: X: true
            x = 3: X = 1: false
            x = 3: Win32: true
            x = 3: VBA7: false
            compiled
            """"""),
        ["3/cc-plus"] = new("X = +1", """"""
            Sub Main()
            #If X Then
                L "X = +1: X: true"
            #Else
                L "X = +1: X: false"
            #End If
            #If X = 1 Then
                L "X = +1: X = 1: true"
            #Else
                L "X = +1: X = 1: false"
            #End If
            #If Win32 Then
                L "X = +1: Win32: true"
            #Else
                L "X = +1: Win32: false"
            #End If
            #If VBA7 Then
                L "X = +1: VBA7: true"
            #Else
                L "X = +1: VBA7: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            X = +1: X: true
            X = +1: X = 1: true
            X = +1: Win32: true
            X = +1: VBA7: false
            compiled
            """"""),
        ["3/cc-vba7"] = new("VBA7 = 1", """"""
            Sub Main()
            #If X Then
                L "VBA7 = 1: X: true"
            #Else
                L "VBA7 = 1: X: false"
            #End If
            #If X = 1 Then
                L "VBA7 = 1: X = 1: true"
            #Else
                L "VBA7 = 1: X = 1: false"
            #End If
            #If Win32 Then
                L "VBA7 = 1: Win32: true"
            #Else
                L "VBA7 = 1: Win32: false"
            #End If
            #If VBA7 Then
                L "VBA7 = 1: VBA7: true"
            #Else
                L "VBA7 = 1: VBA7: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            VBA7 = 1: X: false
            VBA7 = 1: X = 1: false
            VBA7 = 1: Win32: true
            VBA7 = 1: VBA7: true
            compiled
            """"""),
        ["3/cc-win32"] = new("Win32 = 0", """"""
            Sub Main()
            #If X Then
                L "Win32 = 0: X: true"
            #Else
                L "Win32 = 0: X: false"
            #End If
            #If X = 1 Then
                L "Win32 = 0: X = 1: true"
            #Else
                L "Win32 = 0: X = 1: false"
            #End If
            #If Win32 Then
                L "Win32 = 0: Win32: true"
            #Else
                L "Win32 = 0: Win32: false"
            #End If
            #If VBA7 Then
                L "Win32 = 0: VBA7: true"
            #Else
                L "Win32 = 0: VBA7: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Win32 = 0: X: false
            Win32 = 0: X = 1: false
            Win32 = 0: Win32: false
            Win32 = 0: VBA7: false
            compiled
            """"""),
        ["3/f-Abs-undef"] = new(null, """"""
            Sub Main()
            #If Abs(Foo) = 0 Then
                L "Abs(Foo) = 0: true"
            #Else
                L "Abs(Foo) = 0: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Abs(Foo) = 0: true
            compiled
            """"""),
        ["3/f-CByte"] = new(null, """"""
            Sub Main()
            #If CByte(200) = 200 Then
                L "CByte(200) = 200: true"
            #Else
                L "CByte(200) = 200: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            CByte(200) = 200: true
            compiled
            """"""),
        ["3/f-CCur"] = new(null, """"""
            Sub Main()
            #If CCur(1.5) = 1.5 Then
                L "CCur(1.5) = 1.5: true"
            #Else
                L "CCur(1.5) = 1.5: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            CCur(1.5) = 1.5: true
            compiled
            """"""),
        ["3/f-CDate"] = new(null, """"""
            Sub Main()
            #If CDate(1) = #12/31/1899# Then
                L "CDate(1) = #12/31/1899#: true"
            #Else
                L "CDate(1) = #12/31/1899#: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            CDate(1) = #12/31/1899#: true
            compiled
            """"""),
        ["3/f-CDbl"] = new(null, """"""
            Sub Main()
            #If CDbl(1) = 1 Then
                L "CDbl(1) = 1: true"
            #Else
                L "CDbl(1) = 1: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            CDbl(1) = 1: true
            compiled
            """"""),
        ["3/f-CInt-round"] = new(null, """"""
            Sub Main()
            #If CInt(2.5) = 2 Then
                L "CInt(2.5) = 2: true"
            #Else
                L "CInt(2.5) = 2: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            CInt(2.5) = 2: true
            compiled
            """"""),
        ["3/f-CLng"] = new(null, """"""
            Sub Main()
            #If CLng(70000) = 70000 Then
                L "CLng(70000) = 70000: true"
            #Else
                L "CLng(70000) = 70000: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            CLng(70000) = 70000: true
            compiled
            """"""),
        ["3/f-CSng"] = new(null, """"""
            Sub Main()
            #If CSng(1.5) = 1.5 Then
                L "CSng(1.5) = 1.5: true"
            #Else
                L "CSng(1.5) = 1.5: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            CSng(1.5) = 1.5: true
            compiled
            """"""),
        ["3/f-CStr"] = new(null, """"""
            Sub Main()
            #If CStr(5) = "5" Then
                L "CStr(5) = ""5"": true"
            #Else
                L "CStr(5) = ""5"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            CStr(5) = "5": true
            compiled
            """"""),
        ["3/f-CStr-undef"] = new(null, """"""
            Sub Main()
            #If CStr(Foo) = "" Then
                L "CStr(Foo) = """": true"
            #Else
                L "CStr(Foo) = """": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            CStr(Foo) = "": false
            compiled
            """"""),
        ["3/f-CStr-undef0"] = new(null, """"""
            Sub Main()
            #If CStr(Foo) = "0" Then
                L "CStr(Foo) = ""0"": true"
            #Else
                L "CStr(Foo) = ""0"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            CStr(Foo) = "0": true
            compiled
            """"""),
        ["3/f-CVar"] = new(null, """"""
            Sub Main()
            #If CVar(5) = 5 Then
                L "CVar(5) = 5: true"
            #Else
                L "CVar(5) = 5: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            CVar(5) = 5: true
            compiled
            """"""),
        ["3/f-Fix"] = new(null, """"""
            Sub Main()
            #If Fix(-2.5) = -2 Then
                L "Fix(-2.5) = -2: true"
            #Else
                L "Fix(-2.5) = -2: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Fix(-2.5) = -2: true
            compiled
            """"""),
        ["3/f-Int"] = new(null, """"""
            Sub Main()
            #If Int(2.5) = 2 Then
                L "Int(2.5) = 2: true"
            #Else
                L "Int(2.5) = 2: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Int(2.5) = 2: true
            compiled
            """"""),
        ["3/f-Int-of-const"] = new(null, """"""
            Sub Main()
            #If Int(Win32) = 1 Then
                L "Int(Win32) = 1: true"
            #Else
                L "Int(Win32) = 1: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Int(Win32) = 1: true
            compiled
            """"""),
        ["3/f-LenB"] = new(null, """"""
            Sub Main()
            #If LenB("ab") = 4 Then
                L "LenB(""ab"") = 4: true"
            #Else
                L "LenB(""ab"") = 4: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            LenB("ab") = 4: true
            compiled
            """"""),
        ["3/f-Len-undef"] = new(null, """"""
            Sub Main()
            #If Len(Foo) = 0 Then
                L "Len(Foo) = 0: true"
            #Else
                L "Len(Foo) = 0: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Len(Foo) = 0: false
            compiled
            """"""),
        ["3/f-Sgn"] = new(null, """"""
            Sub Main()
            #If Sgn(-5) = -1 Then
                L "Sgn(-5) = -1: true"
            #Else
                L "Sgn(-5) = -1: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Sgn(-5) = -1: true
            compiled
            """"""),
        ["3/s-cond-string-one"] = new(null, """"""
            Sub Main()
            #If "1" Then
                L """1"": true"
            #Else
                L """1"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            "1": true
            compiled
            """"""),
        ["3/s-cond-string-true"] = new(null, """"""
            Sub Main()
            #If "True" Then
                L """True"": true"
            #Else
                L """True"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            "True": true
            compiled
            """"""),
        ["3/s-const-string-concat"] = new(null, """"""
            #Const SC = "a" & 1
            Sub Main()
            #If SC = "a1" Then
                L "SC = ""a1"": true"
            #Else
                L "SC = ""a1"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            SC = "a1": true
            compiled
            """"""),
        ["3/s-continued-comment"] = new(null, """"""
            Sub Main()
                ' a comment _
            #If 0 Then
                L "after a continued comment"
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            after a continued comment
            compiled
            """"""),
        ["3/s-decl-section-if"] = new(null, """"""
            #If 1 Then
            Private Const KX = 1
            #Else
            Private Const KX = 2
            #End If
            Sub Main()
                L "KX = " & KX
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            KX = 1
            compiled
            """"""),
        ["3/s-elseif-no-then"] = new(null, """"""
            Sub Main()
            #If 0 Then
            #ElseIf 1
                L "ElseIf without Then: true"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            compiled
            """"""),
        ["3/s-endif-one-word"] = new(null, """"""
            Sub Main()
            #If 1 Then
                L "#EndIf: true"
            #EndIf
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            #EndIf: true
            compiled
            """"""),
        ["3/s-indented"] = new(null, """"""
            Sub Main()
                #If 1 Then
                    L "indented: true"
                #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            indented: true
            compiled
            """"""),
        ["3/s-option-compare-text"] = new(null, """"""
            Option Compare Text
            Sub Main()
            #If "Z" < "a" Then
                L """Z"" < ""a"": true"
            #Else
                L """Z"" < ""a"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            "Z" < "a": false
            compiled
            """"""),
        ["3/s-whole-proc-switched"] = new(null, """"""
            #If 0 Then
            Sub Both()
                L "first Both"
            End Sub
            #Else
            Sub Both()
                L "second Both"
            End Sub
            #End If
            Sub Main()
                Both
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            second Both
            compiled
            """"""),
        ["4/e-a-lt-B"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If "a" < "B" Then
                L """a"" < ""B"": true"
            #Else
                L """a"" < ""B"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            "a" < "B": true
            compiled
            """"""),
        ["4/e-date-lt-num"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If #1/1/2000# < 40000 Then
                L "#1/1/2000# < 40000: true"
            #Else
                L "#1/1/2000# < 40000: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            #1/1/2000# < 40000: true
            compiled
            """"""),
        ["4/e-foo-amp"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If Foo & "x" = "0x" Then
                L "Foo & ""x"" = ""0x"": true"
            #Else
                L "Foo & ""x"" = ""0x"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Foo & "x" = "0x": true
            compiled
            """"""),
        ["4/e-foo-eq-empty"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If Foo = "" Then
                L "Foo = """": true"
            #Else
                L "Foo = """": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Foo = "": false
            compiled
            """"""),
        ["4/e-foo-gt-empty"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If Foo > "" Then
                L "Foo > """": true"
            #Else
                L "Foo > """": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Foo > "": true
            compiled
            """"""),
        ["4/e-foo-lt-empty"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If Foo < "" Then
                L "Foo < """": true"
            #Else
                L "Foo < """": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Foo < "": false
            compiled
            """"""),
        ["4/e-foo-plus"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If Foo + 1 = 1 Then
                L "Foo + 1 = 1: true"
            #Else
                L "Foo + 1 = 1: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Foo + 1 = 1: true
            compiled
            """"""),
        ["4/e-int-lit-cstr"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If CStr(N5) = "5" Then
                L "CStr(N5) = ""5"": true"
            #Else
                L "CStr(N5) = ""5"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            CStr(N5) = "5": true
            compiled
            """"""),
        ["4/e-lit5-lt-10"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If 5 < "10" Then
                L "5 < ""10"": true"
            #Else
                L "5 < ""10"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            5 < "10": true
            compiled
            """"""),
        ["4/e-ml-plus-1"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If ML + 1 = 2147483648# Then
                L "ML + 1 = 2147483648#: true"
            #Else
                L "ML + 1 = 2147483648#: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            ML + 1 = 2147483648#: true
            compiled
            """"""),
        ["4/e-m-plus-1"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If M + 1 = 32768 Then
                L "M + 1 = 32768: true"
            #Else
                L "M + 1 = 32768: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            M + 1 = 32768: true
            compiled
            """"""),
        ["4/e-m-times-2"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If M * 2 = 65534 Then
                L "M * 2 = 65534: true"
            #Else
                L "M * 2 = 65534: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            M * 2 = 65534: true
            compiled
            """"""),
        ["4/e-n5-amp-s5"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If N5 & S5 = "55" Then
                L "N5 & S5 = ""55"": true"
            #Else
                L "N5 & S5 = ""55"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            N5 & S5 = "55": true
            compiled
            """"""),
        ["4/e-n5-eq-5.0"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If N5 = "5.0" Then
                L "N5 = ""5.0"": true"
            #Else
                L "N5 = ""5.0"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            N5 = "5.0": false
            compiled
            """"""),
        ["4/e-n5-eq-s5"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If N5 = S5 Then
                L "N5 = S5: true"
            #Else
                L "N5 = S5: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            N5 = S5: false
            compiled
            """"""),
        ["4/e-n5-gt-sa"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If N5 > SA Then
                L "N5 > SA: true"
            #Else
                L "N5 > SA: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            N5 > SA: false
            compiled
            """"""),
        ["4/e-n5-lt-10"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If N5 < "10" Then
                L "N5 < ""10"": true"
            #Else
                L "N5 < ""10"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            N5 < "10": false
            compiled
            """"""),
        ["4/e-n5-lt-s5"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If N5 < S5 Then
                L "N5 < S5: true"
            #Else
                L "N5 < S5: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            N5 < S5: true
            compiled
            """"""),
        ["4/e-n5-plus-s5"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If N5 + S5 = 10 Then
                L "N5 + S5 = 10: true"
            #Else
                L "N5 + S5 = 10: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            N5 + S5 = 10: true
            compiled
            """"""),
        ["4/e-not-s5"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If Not S5 = -6 Then
                L "Not S5 = -6: true"
            #Else
                L "Not S5 = -6: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Not S5 = -6: true
            compiled
            """"""),
        ["4/e-one-eq-1.0"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If 1 = "1.0" Then
                L "1 = ""1.0"": true"
            #Else
                L "1 = ""1.0"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            1 = "1.0": true
            compiled
            """"""),
        ["4/e-one-eq-sp1"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If 1 = " 1" Then
                L "1 = "" 1"": true"
            #Else
                L "1 = "" 1"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            1 = " 1": true
            compiled
            """"""),
        ["4/e-s5-plus-1"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If S5 + 1 = 6 Then
                L "S5 + 1 = 6: true"
            #Else
                L "S5 + 1 = 6: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            S5 + 1 = 6: true
            compiled
            """"""),
        ["4/e-sa-eq-A"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If SA = "A" Then
                L "SA = ""A"": true"
            #Else
                L "SA = ""A"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            SA = "A": true
            compiled
            """"""),
        ["4/e-sa-gt-5"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If SA > 5 Then
                L "SA > 5: true"
            #Else
                L "SA > 5: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            SA > 5: true
            compiled
            """"""),
        ["4/e-sa-lt-B"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If SA < "B" Then
                L "SA < ""B"": true"
            #Else
                L "SA < ""B"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            SA < "B": true
            compiled
            """"""),
        ["4/e-win32-eq-1.0"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If Win32 = "1" Then
                L "Win32 = ""1"": true"
            #Else
                L "Win32 = ""1"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Win32 = "1": true
            compiled
            """"""),
        ["4/e-Z-lt-a"] = new(null, """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If "Z" < "a" Then
                L """Z"" < ""a"": true"
            #Else
                L """Z"" < ""a"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            "Z" < "a": false
            compiled
            """"""),
        ["4/s-bad-const-inactive"] = new(null, """"""
            #If 0 Then
            #Const Z = ((
            #End If
            Sub Main()
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            compiled
            """"""),
        ["4/s-condcomp-then-const-self"] = new("ARG = 5", """"""
            #Const ARG = ARG + 1
            Sub Main()
            #If ARG = 6 Then
                L "ARG after #Const ARG = ARG + 1: true"
            #Else
                L "ARG after #Const ARG = ARG + 1: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            ARG after #Const ARG = ARG + 1: true
            compiled
            """"""),
        ["4/s-cond-date"] = new(null, """"""
            Sub Main()
            #If #1/1/2000# Then
                L "#1/1/2000#: true"
            #Else
                L "#1/1/2000#: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            #1/1/2000#: true
            compiled
            """"""),
        ["4/s-cond-false-string"] = new(null, """"""
            Sub Main()
            #If "False" Then
                L """False"": true"
            #Else
                L """False"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            "False": false
            compiled
            """"""),
        ["4/s-const-comment"] = new(null, """"""
            #Const W = 1 ' a comment
            Sub Main()
            #If W = 1 Then
                L "a comment after #Const: true"
            #Else
                L "a comment after #Const: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            a comment after #Const: true
            compiled
            """"""),
        ["4/s-const-in-inactive"] = new(null, """"""
            #If 0 Then
            #Const Z = 1
            #End If
            Sub Main()
            #If Z Then
                L "Z defined only in an inactive branch: true"
            #Else
                L "Z defined only in an inactive branch: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Z defined only in an inactive branch: true
            compiled
            """"""),
        ["4/s-cvar-null"] = new(null, """"""
            Sub Main()
            #If CVar(1) = 1 Then
                L "CVar(1) = 1: true"
            #Else
                L "CVar(1) = 1: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            CVar(1) = 1: true
            compiled
            """"""),
        ["4/s-self-reference"] = new(null, """"""
            #Const R = R + 1
            Sub Main()
            #If R = 1 Then
                L "R after #Const R = R + 1: true"
            #Else
                L "R after #Const R = R + 1: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            R after #Const R = R + 1: true
            compiled
            """"""),
        ["4/s-string-on-const-line-continued"] = new(null, """"""
            #Const LC = _
                1
            Sub Main()
            #If LC = 1 Then
                L "a continued #Const: true"
            #Else
                L "a continued #Const: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            a continued #Const: true
            compiled
            """"""),
        ["5/cc-quoted-empty"] = new("", """"""
            Sub Main()
            #If X Then
                L "empty CondComp: true"
            #Else
                L "empty CondComp: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            empty CondComp: false
            compiled
            """"""),
        ["5/e-cstr-empty"] = new(null, """"""
            #Const S5 = "5"
            #Const SA = "a"
            #Const S10 = "10"
            Sub Main()
            #If CStr(Empty) = "" Then
                L "CStr(Empty) = """": true"
            #Else
                L "CStr(Empty) = """": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            CStr(Empty) = "": true
            compiled
            """"""),
        ["5/e-date-eq-str"] = new(null, """"""
            #Const S5 = "5"
            #Const SA = "a"
            #Const S10 = "10"
            Sub Main()
            #If #1/1/2000# = "1/1/2000" Then
                L "#1/1/2000# = ""1/1/2000"": true"
            #Else
                L "#1/1/2000# = ""1/1/2000"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            #1/1/2000# = "1/1/2000": true
            compiled
            """"""),
        ["5/e-empty-eq-0"] = new(null, """"""
            #Const S5 = "5"
            #Const SA = "a"
            #Const S10 = "10"
            Sub Main()
            #If Empty = 0 Then
                L "Empty = 0: true"
            #Else
                L "Empty = 0: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Empty = 0: true
            compiled
            """"""),
        ["5/e-empty-eq-str"] = new(null, """"""
            #Const S5 = "5"
            #Const SA = "a"
            #Const S10 = "10"
            Sub Main()
            #If Empty = "" Then
                L "Empty = """": true"
            #Else
                L "Empty = """": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            Empty = "": true
            compiled
            """"""),
        ["5/e-lit5-lt-sa"] = new(null, """"""
            #Const S5 = "5"
            #Const SA = "a"
            #Const S10 = "10"
            Sub Main()
            #If 5 < SA Then
                L "5 < SA: true"
            #Else
                L "5 < SA: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            5 < SA: true
            compiled
            """"""),
        ["5/e-s10-gt-9"] = new(null, """"""
            #Const S5 = "5"
            #Const SA = "a"
            #Const S10 = "10"
            Sub Main()
            #If S10 > 9 Then
                L "S10 > 9: true"
            #Else
                L "S10 > 9: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            S10 > 9: true
            compiled
            """"""),
        ["5/e-s5-eq-5.0"] = new(null, """"""
            #Const S5 = "5"
            #Const SA = "a"
            #Const S10 = "10"
            Sub Main()
            #If S5 = 5.0 Then
                L "S5 = 5.0: true"
            #Else
                L "S5 = 5.0: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            S5 = 5.0: true
            compiled
            """"""),
        ["5/e-s5-eq-5"] = new(null, """"""
            #Const S5 = "5"
            #Const SA = "a"
            #Const S10 = "10"
            Sub Main()
            #If S5 = 5 Then
                L "S5 = 5: true"
            #Else
                L "S5 = 5: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            S5 = 5: true
            compiled
            """"""),
        ["5/e-s5-lt-10"] = new(null, """"""
            #Const S5 = "5"
            #Const SA = "a"
            #Const S10 = "10"
            Sub Main()
            #If S5 < 10 Then
                L "S5 < 10: true"
            #Else
                L "S5 < 10: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            S5 < 10: true
            compiled
            """"""),
        ["5/e-sa-eq-5"] = new(null, """"""
            #Const S5 = "5"
            #Const SA = "a"
            #Const S10 = "10"
            Sub Main()
            #If SA = 5 Then
                L "SA = 5: true"
            #Else
                L "SA = 5: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """""",
            null,
            """"""
            SA = 5: false
            compiled
            """"""),
    };

    private static readonly Dictionary<string, (string? Message, string Module1)> Refused = new()
    {
        ["1/s-const-in-sub-then-module"] = ("Duplicate definition", """"""
            Sub Main()
            #Const Q = 1
            End Sub
            #Const Q = 2
            Sub Other()
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["1/s-divzero"] = ("Division by zero", """"""
            Sub Main()
            #If 1 / 0 Then
                L "1 / 0: true"
            #Else
                L "1 / 0: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["1/s-duplicate-const"] = ("Duplicate definition", """"""
            #Const A = 1
            #Const A = 2
            Sub Main()
            #If A = 2 Then
                L "A = 2: true"
            #Else
                L "A = 2: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["1/s-elseif-after-else"] = ("An # ElseIf must be preceded by an # If or # ElseIf and followed by an # Else or # EndIf", """"""
            Sub Main()
            #If 0 Then
            #Else
            #ElseIf 1 Then
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["1/s-no-then"] = ("An # ElseIf, # Else, or # EndIf must be preceded by an # If clause", """"""
            Sub Main()
            #If 1
                L "no Then: true"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["1/s-not-string"] = ("Type mismatch", """"""
            #Const S = "x"
            Sub Main()
            #If Not S Then
                L "Not S: true"
            #Else
                L "Not S: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["1/s-null-keyword"] = ("Invalid use of Null", """"""
            Sub Main()
            #If Null Then
                L "Null: true"
            #Else
                L "Null: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["1/s-stray-endif"] = ("An # ElseIf, # Else, or # EndIf must be preceded by an # If clause", """"""
            Sub Main()
                L "compiled"
            #End If
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["1/s-string-cond"] = ("Type mismatch", """"""
            Sub Main()
            #If "abc" Then
                L """abc"": true"
            #Else
                L """abc"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["1/s-unterminated"] = ("You must terminate the # If block with an # EndIf", """"""
            Sub Main()
            #If 1 Then
                L "unterminated"
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["2/s-fn-chr"] = ("Variable not defined", """"""
            Sub Main()
            #If Chr(65) = "A" Then
                L "Chr(65) = ""A"": true"
            #Else
                L "Chr(65) = ""A"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["2/s-fn-now"] = ("Variable not defined", """"""
            Sub Main()
            #If Year(Now) > 2000 Then
                L "Year(Now) > 2000: true"
            #Else
                L "Year(Now) > 2000: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["2/s-fn-user"] = ("Variable not defined", """"""
            Function MyF() As Integer
                MyF = 1
            End Function
            Sub Main()
            #If MyF() = 1 Then
                L "MyF() = 1: true"
            #Else
                L "MyF() = 1: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/f-Asc"] = ("Variable not defined", """"""
            Sub Main()
            #If Asc("A") = 65 Then
                L "Asc(""A"") = 65: true"
            #Else
                L "Asc(""A"") = 65: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/f-CDec"] = ("Variable not defined", """"""
            Sub Main()
            #If CDec(1) = 1 Then
                L "CDec(1) = 1: true"
            #Else
                L "CDec(1) = 1: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/f-Hex"] = ("Variable not defined", """"""
            Sub Main()
            #If Hex(255) = "FF" Then
                L "Hex(255) = ""FF"": true"
            #Else
                L "Hex(255) = ""FF"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/f-IIf"] = ("Variable not defined", """"""
            Sub Main()
            #If IIf(1, 2, 3) = 2 Then
                L "IIf(1, 2, 3) = 2: true"
            #Else
                L "IIf(1, 2, 3) = 2: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/f-Left"] = ("Variable not defined", """"""
            Sub Main()
            #If Left("ab", 1) = "a" Then
                L "Left(""ab"", 1) = ""a"": true"
            #Else
                L "Left(""ab"", 1) = ""a"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/f-Round"] = ("Variable not defined", """"""
            Sub Main()
            #If Round(2.5) = 2 Then
                L "Round(2.5) = 2: true"
            #Else
                L "Round(2.5) = 2: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/f-Sqr"] = ("Variable not defined", """"""
            Sub Main()
            #If Sqr(4) = 2 Then
                L "Sqr(4) = 2: true"
            #Else
                L "Sqr(4) = 2: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/f-Str"] = ("Variable not defined", """"""
            Sub Main()
            #If Str(5) = " 5" Then
                L "Str(5) = "" 5"": true"
            #Else
                L "Str(5) = "" 5"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/f-TypeName"] = ("Variable not defined", """"""
            Sub Main()
            #If TypeName(1) = "Integer" Then
                L "TypeName(1) = ""Integer"": true"
            #Else
                L "TypeName(1) = ""Integer"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/f-UCase"] = ("Variable not defined", """"""
            Sub Main()
            #If UCase("a") = "A" Then
                L "UCase(""a"") = ""A"": true"
            #Else
                L "UCase(""a"") = ""A"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/f-Val"] = ("Variable not defined", """"""
            Sub Main()
            #If Val("5") = 5 Then
                L "Val(""5"") = 5: true"
            #Else
                L "Val(""5"") = 5: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/f-VarType"] = ("Variable not defined", """"""
            Sub Main()
            #If VarType(1) = 2 Then
                L "VarType(1) = 2: true"
            #Else
                L "VarType(1) = 2: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/f-Len-num"] = ("Variable required - can't assign to this expression", """"""
            Sub Main()
            #If Len(5) = 2 Then
                L "Len(5) = 2: true"
            #Else
                L "Len(5) = 2: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/main"] = ("Type mismatch", """"""
            Sub Main()
            #If 0 < "a" Then
                L "0 < ""a"": true"
            #Else
                L "0 < ""a"": false"
            #End If
            #If 0 > "a" Then
                L "0 > ""a"": true"
            #Else
                L "0 > ""a"": false"
            #End If
            #If 0 = "a" Then
                L "0 = ""a"": true"
            #Else
                L "0 = ""a"": false"
            #End If
            #If 5 = "x" Then
                L "5 = ""x"": true"
            #Else
                L "5 = ""x"": false"
            #End If
            #If "" < 1 Then
                L """"" < 1: true"
            #Else
                L """"" < 1: false"
            #End If
            #If Foo < "" Then
                L "Foo < """": true"
            #Else
                L "Foo < """": false"
            #End If
            #If Foo > "" Then
                L "Foo > """": true"
            #Else
                L "Foo > """": false"
            #End If
            #If "abc" > 5 Then
                L """abc"" > 5: true"
            #Else
                L """abc"" > 5: false"
            #End If
            #If 1 = "1.0" Then
                L "1 = ""1.0"": true"
            #Else
                L "1 = ""1.0"": false"
            #End If
            #If 1 = " 1" Then
                L "1 = "" 1"": true"
            #Else
                L "1 = "" 1"": false"
            #End If
            #If Foo & "x" = "x" Then
                L "Foo & ""x"" = ""x"": true"
            #Else
                L "Foo & ""x"" = ""x"": false"
            #End If
            #If Foo & "x" = "0x" Then
                L "Foo & ""x"" = ""0x"": true"
            #Else
                L "Foo & ""x"" = ""0x"": false"
            #End If
            #If Foo = Empty Then
                L "Foo = Empty: true"
            #Else
                L "Foo = Empty: false"
            #End If
            #If Foo + 1 = 1 Then
                L "Foo + 1 = 1: true"
            #Else
                L "Foo + 1 = 1: false"
            #End If
            #If "Z" < "a" Then
                L """Z"" < ""a"": true"
            #Else
                L """Z"" < ""a"": false"
            #End If
            #If "a" < "B" Then
                L """a"" < ""B"": true"
            #Else
                L """a"" < ""B"": false"
            #End If
            #If "ab" = "AB" Then
                L """ab"" = ""AB"": true"
            #Else
                L """ab"" = ""AB"": false"
            #End If
            #If 1 = 1.0 Then
                L "1 = 1.0: true"
            #Else
                L "1 = 1.0: false"
            #End If
            #If 7 \ 2 * 2 = 6 Then
                L "7 \ 2 * 2 = 6: true"
            #Else
                L "7 \ 2 * 2 = 6: false"
            #End If
            #If -2 ^ 2 = 4 Then
                L "-2 ^ 2 = 4: true"
            #Else
                L "-2 ^ 2 = 4: false"
            #End If
            #If 2 + 3 * 4 = 14 Then
                L "2 + 3 * 4 = 14: true"
            #Else
                L "2 + 3 * 4 = 14: false"
            #End If
            #If 10 - 2 - 3 = 5 Then
                L "10 - 2 - 3 = 5: true"
            #Else
                L "10 - 2 - 3 = 5: false"
            #End If
            #If Not 0 = -1 Then
                L "Not 0 = -1: true"
            #Else
                L "Not 0 = -1: false"
            #End If
            #If (Not 1) = -2 Then
                L "(Not 1) = -2: true"
            #Else
                L "(Not 1) = -2: false"
            #End If
            #If True + True = -2 Then
                L "True + True = -2: true"
            #Else
                L "True + True = -2: false"
            #End If
            #If "1" + 1 = 2 Then
                L """1"" + 1 = 2: true"
            #Else
                L """1"" + 1 = 2: false"
            #End If
            #If "1" & 1 = "11" Then
                L """1"" & 1 = ""11"": true"
            #Else
                L """1"" & 1 = ""11"": false"
            #End If
            #If #1/2/2000# > #1/1/2000# Then
                L "#1/2/2000# > #1/1/2000#: true"
            #Else
                L "#1/2/2000# > #1/1/2000#: false"
            #End If
            #If 0 Then
            #Const Z = 1
            #End If
            #If Z Then
                L "Z defined only in an inactive branch: true"
            #Else
                L "Z defined only in an inactive branch: false"
            #End If
            #Const Y = 1
            #If 0 Then
            #Const Y = 2
            #End If
            #If Y = 1 Then
                L "Y redefined only in an inactive branch: true"
            #Else
                L "Y redefined only in an inactive branch: false"
            #End If
            #Const W = 1 ' a comment
            #If W = 1 Then
                L "a comment after #Const: true"
            #Else
                L "a comment after #Const: false"
            #End If
                L "end"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/s-colon-after"] = ("An # ElseIf, # Else, or # EndIf must be preceded by an # If clause", """"""
            Sub Main()
            #If 1 Then: L "colon"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/s-if-single-line"] = ("An # ElseIf, # Else, or # EndIf must be preceded by an # If clause", """"""
            Sub Main()
            #If 1 Then L "single line"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/s-label-before"] = ("An # ElseIf, # Else, or # EndIf must be preceded by an # If clause", """"""
            Sub Main()
            10 #If 1 Then
                L "label: true"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/s-const-overflow"] = ("Overflow", """"""
            #Const BIG = 32767 * 2
            Sub Main()
            #If BIG = 65534 Then
                L "BIG = 65534: true"
            #Else
                L "BIG = 65534: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/s-inactive-bad-directive"] = ("An # ElseIf, # Else, or # EndIf must be preceded by an # If clause", """"""
            Sub Main()
            #If 0 Then
            #If (( Then
            #End If
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/s-inactive-unknown-fn"] = ("Variable not defined", """"""
            Sub Main()
            #If 1 Then
                L "first"
            #ElseIf Chr(65) = "A" Then
                L "second"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/s-is-operator"] = ("Type mismatch", """"""
            Sub Main()
            #If 1 Is 1 Then
                L "1 Is 1: true"
            #Else
                L "1 Is 1: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/s-nested-unterminated-inactive"] = ("You must terminate the # If block with an # EndIf", """"""
            Sub Main()
            #If 0 Then
            #If 1 Then
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/s-nothing"] = ("Invalid use of object", """"""
            Sub Main()
            #If Nothing Then
                L "Nothing: true"
            #Else
                L "Nothing: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/s-overflow-int"] = ("Overflow", """"""
            Sub Main()
            #If 32767 + 1 > 0 Then
                L "32767 + 1 > 0: true"
            #Else
                L "32767 + 1 > 0: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/s-overflow-long"] = ("Overflow", """"""
            Sub Main()
            #If 2147483647 + 1 > 0 Then
                L "2147483647 + 1 > 0: true"
            #Else
                L "2147483647 + 1 > 0: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["4/e-abc-gt-5"] = ("Type mismatch", """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If "abc" > 5 Then
                L """abc"" > 5: true"
            #Else
                L """abc"" > 5: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["4/e-sa-plus-1"] = ("Type mismatch", """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If SA + 1 = 2 Then
                L "SA + 1 = 2: true"
            #Else
                L "SA + 1 = 2: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["4/e-zero-eq-a"] = ("Type mismatch", """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If 0 = "a" Then
                L "0 = ""a"": true"
            #Else
                L "0 = ""a"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["4/e-zero-eq-empty"] = ("Type mismatch", """"""
            #Const N5 = 5
            #Const S5 = "5"
            #Const SA = "a"
            #Const M = 32767
            #Const ML = 2147483647
            Sub Main()
            #If 0 = "" Then
                L "0 = """": true"
            #Else
                L "0 = """": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["4/s-cond-empty-string"] = ("Type mismatch", """"""
            Sub Main()
            #If "" Then
                L """"": true"
            #Else
                L """"": false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["4/s-divzero-elseif-after-taken"] = ("Division by zero", """"""
            Sub Main()
            #If 1 Then
                L "first"
            #ElseIf 1 / 0 Then
                L "second"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["4/s-divzero-elseif-in-inactive"] = ("Division by zero", """"""
            Sub Main()
            #If 0 Then
            #If 1 Then
            #ElseIf 1 / 0 Then
            #End If
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["4/s-else-twice"] = ("An # Else clause must be preceded by an # If", """"""
            Sub Main()
            #If 0 Then
            #Else
            #Else
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["4/s-malformed-active-if"] = ("An # ElseIf, # Else, or # EndIf must be preceded by an # If clause", """"""
            Sub Main()
            #If (( Then
                L "x"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["4/s-redefine-in-inactive"] = ("Duplicate definition", """"""
            #Const Y = 1
            #If 0 Then
            #Const Y = 2
            #End If
            Sub Main()
            #If Y = 1 Then
                L "Y redefined only in an inactive branch: true"
            #Else
                L "Y redefined only in an inactive branch: false"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["4/s-unknown-fn-const-inactive"] = ("Variable not defined", """"""
            #If 0 Then
            #Const Z = Chr(65)
            #End If
            Sub Main()
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["4/s-unknown-fn-inactive-if"] = ("Variable not defined", """"""
            Sub Main()
            #If 0 Then
            #If Chr(65) = "A" Then
                L "x"
            #End If
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["2/s-else-space-if-1"] = (null, """"""
            Sub Main()
            #If 1 Then
                L "first(1)"
            #Else If 0 Then
                L "second(1)"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/s-const-no-value"] = (null, """"""
            #Const NV
            Sub Main()
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
        ["3/s-inactive-elseif-bad"] = (null, """"""
            Sub Main()
            #If 1 Then
                L "first"
            #ElseIf (( Then
                L "second"
            #End If
                L "compiled"
            End Sub
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub
            Main

            """"""),
    };
}
