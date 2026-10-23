// Generated from the vb6.exe probes for #1547 (scratchpad p1547/gentests.py): each row is a measured answer, native and
// p-code alike, under Option Compare Binary (B) and Text (T). Edit the probes and regenerate rather than editing a row.

using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class LikeOperatorTests : BaseVBTestFixture
{
    // Module1 compares as Binary and Module2 as Text, as the probe did, and each row logs both answers on one line.
    private const string BinaryModule = """""""
        Option Compare Binary

        Public Function Show(v As Variant) As String
            If IsNull(v) Then
                Show = "Null"
            ElseIf IsEmpty(v) Then
                Show = "Empty"
            Else
                Show = TypeName(v) & " " & CStr(v)
            End If
        End Function

        Public Function TB(ByVal s As Variant, ByVal p As Variant) As String
            Dim r As Variant
            On Error Resume Next
            Err.Clear
            r = (s Like p)
            If Err.Number <> 0 Then
                TB = "err " & Err.Number & " " & Err.Description
            Else
                TB = Show(r)
            End If
        End Function

        Public Sub R(ByVal label As String, ByVal s As Variant, ByVal p As Variant)
            Debug.Print label & ": B=" & TB(s, p) & " | T=" & TT(s, p)
        End Sub
        """"""";

    private const string TextModule = """""""
        Option Compare Text

        Public Function TT(ByVal s As Variant, ByVal p As Variant) As String
            Dim r As Variant
            On Error Resume Next
            Err.Clear
            r = (s Like p)
            If Err.Number <> 0 Then
                TT = "err " & Err.Number & " " & Err.Description
            Else
                TT = Show(r)
            End If
        End Function
        """"""";

    private async Task Row(string call) =>
        await RunModules(BinaryModule + "\n\n" + call + "\n", ("Module2", TextModule));

    [Theory]
    [InlineData("same", "\"abc\"", "\"abc\"", "B=Boolean True | T=Boolean True")]
    [InlineData("case", "\"abc\"", "\"ABC\"", "B=Boolean False | T=Boolean True")]
    [InlineData("q", "\"abc\"", "\"a?c\"", "B=Boolean True | T=Boolean True")]
    [InlineData("q-short", "\"ac\"", "\"a?c\"", "B=Boolean False | T=Boolean False")]
    [InlineData("star-end", "\"abc\"", "\"a*\"", "B=Boolean True | T=Boolean True")]
    [InlineData("star-start", "\"abc\"", "\"*c\"", "B=Boolean True | T=Boolean True")]
    [InlineData("star-only", "\"abc\"", "\"*\"", "B=Boolean True | T=Boolean True")]
    [InlineData("empty-star", "\"\"", "\"*\"", "B=Boolean True | T=Boolean True")]
    [InlineData("empty-empty", "\"\"", "\"\"", "B=Boolean True | T=Boolean True")]
    [InlineData("a-empty", "\"a\"", "\"\"", "B=Boolean False | T=Boolean False")]
    [InlineData("empty-q", "\"\"", "\"?\"", "B=Boolean False | T=Boolean False")]
    [InlineData("star-twice", "\"abc\"", "\"a*c*\"", "B=Boolean True | T=Boolean True")]
    [InlineData("star-mid", "\"abc\"", "\"*b*\"", "B=Boolean True | T=Boolean True")]
    [InlineData("star-back", "\"aXbXc\"", "\"a*b*c\"", "B=Boolean True | T=Boolean True")]
    [InlineData("star-star", "\"abc\"", "\"a**c\"", "B=Boolean True | T=Boolean True")]
    [InlineData("star-trail", "\"ab\"", "\"a*b*\"", "B=Boolean True | T=Boolean True")]
    [InlineData("star-greedy", "\"abcbc\"", "\"a*bc\"", "B=Boolean True | T=Boolean True")]
    [InlineData("hash", "\"5\"", "\"#\"", "B=Boolean True | T=Boolean True")]
    [InlineData("hash-letter", "\"a\"", "\"#\"", "B=Boolean False | T=Boolean False")]
    [InlineData("hash-two", "\"55\"", "\"##\"", "B=Boolean True | T=Boolean True")]
    [InlineData("hash-one-of-two", "\"5\"", "\"##\"", "B=Boolean False | T=Boolean False")]
    [InlineData("hash-q", "\"5\"", "\"?\"", "B=Boolean True | T=Boolean True")]
    [InlineData("hash-super2", "Chr(178)", "\"#\"", "B=Boolean False | T=Boolean False")]
    [InlineData("hash-fullwidth", "ChrW(&HFF15)", "\"#\"", "B=Boolean True | T=Boolean True")]
    [InlineData("list-in", "\"b\"", "\"[abc]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("list-out", "\"d\"", "\"[abc]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("notlist-in", "\"d\"", "\"[!abc]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("notlist-out", "\"b\"", "\"[!abc]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("range-in", "\"m\"", "\"[a-z]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("range-upper", "\"M\"", "\"[a-z]\"", "B=Boolean False | T=Boolean True")]
    [InlineData("range-upper-pat", "\"m\"", "\"[A-Z]\"", "B=Boolean False | T=Boolean True")]
    [InlineData("range-two-in", "\"5\"", "\"[0-9a-f]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("range-two-out", "\"g\"", "\"[0-9a-f]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("ranges-gap", "\"d\"", "\"[a-ce-g]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("ranges-hit", "\"f\"", "\"[a-ce-g]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("dash-end", "\"-\"", "\"[a-]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("dash-start", "\"-\"", "\"[-a]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("dash-end-a", "\"a\"", "\"[a-]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("dash-only", "\"-\"", "\"[-]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("not-dash", "\"-\"", "\"[!-]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("bracket-lit", "\"[\"", "\"[[]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("star-lit", "\"*\"", "\"[*]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("star-lit-a", "\"a\"", "\"[*]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("q-lit", "\"?\"", "\"[?]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("hash-lit", "\"#\"", "\"[#]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("hash-lit-5", "\"5\"", "\"[#]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("close-alone", "\"]\"", "\"]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("close-in-list", "\"]\"", "\"[]]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("bang-alone", "\"!\"", "\"!\"", "B=Boolean True | T=Boolean True")]
    [InlineData("bang-list", "\"!\"", "\"[!]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("bang-list-a", "\"a\"", "\"[!]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("bang-bang", "\"!\"", "\"[!!]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("bang-not-start", "\"!\"", "\"[a!]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("unclosed", "\"a\"", "\"[a\"", "B=err 93 Invalid pattern string | T=err 93 Invalid pattern string")]
    [InlineData("unclosed-tail", "\"ab\"", "\"a[b\"", "B=err 93 Invalid pattern string | T=err 93 Invalid pattern string")]
    [InlineData("open-alone", "\"[\"", "\"[\"", "B=err 93 Invalid pattern string | T=err 93 Invalid pattern string")]
    [InlineData("empty-list", "\"a\"", "\"[]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("empty-list-empty", "\"\"", "\"[]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("empty-list-x", "\"ax\"", "\"a[]x\"", "B=Boolean True | T=Boolean True")]
    [InlineData("reversed", "\"a\"", "\"[z-a]\"", "B=err 93 Invalid pattern string | T=err 93 Invalid pattern string")]
    [InlineData("reversed-b", "\"b\"", "\"[z-a]\"", "B=err 93 Invalid pattern string | T=err 93 Invalid pattern string")]
    [InlineData("reversed-not", "\"b\"", "\"[!z-a]\"", "B=err 93 Invalid pattern string | T=err 93 Invalid pattern string")]
    [InlineData("same-range", "\"a\"", "\"[a-a]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("star-in-list", "\"x\"", "\"[*x]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("list-star", "\"abc\"", "\"[a]*\"", "B=Boolean True | T=Boolean True")]
    [InlineData("space", "\"a b\"", "\"a b\"", "B=Boolean True | T=Boolean True")]
    [InlineData("space-q", "\"a b\"", "\"a?b\"", "B=Boolean True | T=Boolean True")]
    [InlineData("lf", "\"a\" & vbLf & \"b\"", "\"a?b\"", "B=Boolean True | T=Boolean True")]
    [InlineData("lf-star", "\"a\" & vbCrLf & \"b\"", "\"a*b\"", "B=Boolean True | T=Boolean True")]
    [InlineData("nul", "\"a\" & Chr(0) & \"b\"", "\"a?b\"", "B=Boolean True | T=Boolean True")]
    [InlineData("e-acute-az", "Chr(233)", "\"[a-z]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("e-acute-e", "Chr(233)", "\"e\"", "B=Boolean False | T=Boolean False")]
    [InlineData("e-acute-E-acute", "Chr(233)", "Chr(201)", "B=Boolean False | T=Boolean True")]
    [InlineData("e-acute-range", "Chr(233)", "\"[\" & Chr(224) & \"-\" & Chr(255) & \"]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("E-acute-range", "Chr(201)", "\"[\" & Chr(224) & \"-\" & Chr(255) & \"]\"", "B=Boolean False | T=Boolean True")]
    [InlineData("sz-st", "Chr(223)", "\"[s-t]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("n-tilde-az", "Chr(241)", "\"[a-z]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("Z-az", "\"Z\"", "\"[a-z]\"", "B=Boolean False | T=Boolean True")]
    [InlineData("B-ac", "\"B\"", "\"[a-c]\"", "B=Boolean False | T=Boolean True")]
    [InlineData("b-AC", "\"b\"", "\"[A-C]\"", "B=Boolean False | T=Boolean True")]
    [InlineData("bracket-Z", "\"_\"", "\"[A-z]\"", "B=Boolean True | T=Boolean False")]
    [InlineData("caret-range", "\"^\"", "\"[A-z]\"", "B=Boolean True | T=Boolean False")]
    [InlineData("int", "5", "\"#\"", "B=Boolean True | T=Boolean True")]
    [InlineData("int-pat", "\"5\"", "5", "B=Boolean True | T=Boolean True")]
    [InlineData("double", "5.5", "\"#.#\"", "B=Boolean True | T=Boolean True")]
    [InlineData("double-big", "1E+20", "\"*E*\"", "B=Boolean True | T=Boolean True")]
    [InlineData("bool", "True", "\"True\"", "B=Boolean True | T=Boolean True")]
    [InlineData("bool-num", "True", "\"-1\"", "B=Boolean False | T=Boolean False")]
    [InlineData("date", "#1/2/2003#", "\"*2003*\"", "B=Boolean True | T=Boolean True")]
    [InlineData("currency", "CCur(1.5)", "\"1.5\"", "B=Boolean True | T=Boolean True")]
    [InlineData("null-s", "Null", "\"*\"", "B=Null | T=Null")]
    [InlineData("null-p", "\"a\"", "Null", "B=Null | T=Null")]
    [InlineData("null-both", "Null", "Null", "B=Null | T=Null")]
    [InlineData("empty-s", "Empty", "\"\"", "B=Boolean True | T=Boolean True")]
    [InlineData("empty-s-star", "Empty", "\"*\"", "B=Boolean True | T=Boolean True")]
    [InlineData("empty-p", "\"a\"", "Empty", "B=Boolean False | T=Boolean False")]
    [InlineData("digit-arabic", "ChrW(&H660)", "\"#\"", "B=Boolean False | T=Boolean False")]
    [InlineData("digit-devanagari", "ChrW(&H966)", "\"#\"", "B=Boolean False | T=Boolean False")]
    [InlineData("fullwidth-a", "ChrW(&HFF41)", "\"a\"", "B=Boolean False | T=Boolean False")]
    [InlineData("fullwidth-a-range", "ChrW(&HFF41)", "\"[a-z]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("fullwidth-a-q", "ChrW(&HFF41)", "\"?\"", "B=Boolean True | T=Boolean True")]
    [InlineData("backtick-fold", "\"`\"", "\"[_-a]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("B-fold", "\"B\"", "\"[_-a]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("o-umlaut-op", "Chr(246)", "\"[o-p]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("sz-qq", "Chr(223)", "\"??\"", "B=Boolean False | T=Boolean False")]
    [InlineData("sz-q", "Chr(223)", "\"?\"", "B=Boolean True | T=Boolean True")]
    [InlineData("sz-list-sz", "Chr(223)", "\"[\" & Chr(223) & \"]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("close-list-x", "\"x\"", "\"[]x]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("close-list-a", "\"a]\"", "\"[]]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("euro-ansi-range", "Chr(128)", "\"[\" & Chr(127) & \"-\" & Chr(129) & \"]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("euro-unicode-range", "Chr(128)", "\"[~-\" & ChrW(&H2100) & \"]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("euro-eq", "Chr(128)", "ChrW(&H20AC)", "B=Boolean True | T=Boolean True")]
    [InlineData("unmapped-q", "ChrW(&H2100)", "\"?\"", "B=Boolean True | T=Boolean True")]
    [InlineData("unmapped-literal", "ChrW(&H2100)", "\"?\"", "B=Boolean True | T=Boolean True")]
    [InlineData("unmapped-vs-qmark", "ChrW(&H2100)", "\"[?]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("surrogate-q", "ChrW(&HD83D) & ChrW(&HDE00)", "\"?\"", "B=Boolean False | T=Boolean False")]
    [InlineData("surrogate-qq", "ChrW(&HD83D) & ChrW(&HDE00)", "\"??\"", "B=Boolean True | T=Boolean True")]
    [InlineData("yuml-upper", "ChrW(&H178)", "\"[\" & Chr(224) & \"-\" & Chr(255) & \"]\"", "B=Boolean False | T=Boolean True")]
    [InlineData("dotless-i", "ChrW(&H131)", "\"i\"", "B=Boolean False | T=Boolean False")]
    [InlineData("kelvin", "ChrW(&H212A)", "\"k\"", "B=Boolean False | T=Boolean False")]
    [InlineData("star-only-long", "String(1000, \"a\")", "\"*a*a*a*b\"", "B=Boolean False | T=Boolean False")]
    [InlineData("fw-0", "ChrW(&HFF10)", "\"#\"", "B=Boolean True | T=Boolean True")]
    [InlineData("fw-9", "ChrW(&HFF19)", "\"#\"", "B=Boolean True | T=Boolean True")]
    [InlineData("fw-colon", "ChrW(&HFF1A)", "\"#\"", "B=Boolean False | T=Boolean False")]
    [InlineData("fw-slash", "ChrW(&HFF0F)", "\"#\"", "B=Boolean False | T=Boolean False")]
    [InlineData("fw-5-range", "ChrW(&HFF15)", "\"[0-9]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("fw-5-literal", "ChrW(&HFF15)", "\"5\"", "B=Boolean False | T=Boolean False")]
    [InlineData("super1", "Chr(185)", "\"#\"", "B=Boolean False | T=Boolean False")]
    [InlineData("ext-arabic", "ChrW(&H6F5)", "\"#\"", "B=Boolean False | T=Boolean False")]
    [InlineData("reversed-text-ok", "\"a\"", "\"[Z-a]\"", "B=Boolean True | T=err 93 Invalid pattern string")]
    [InlineData("reversed-text-ok-b", "\"_\"", "\"[Z-a]\"", "B=Boolean True | T=err 93 Invalid pattern string")]
    [InlineData("reversed-binary-ok", "\"b\"", "\"[a-Z]\"", "B=err 93 Invalid pattern string | T=Boolean True")]
    [InlineData("reversed-binary-ok-Q", "\"Q\"", "\"[a-Z]\"", "B=err 93 Invalid pattern string | T=Boolean True")]
    [InlineData("kelvin-range", "ChrW(&H212A)", "\"[j-l]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("dotted-I", "ChrW(&H130)", "\"i\"", "B=Boolean False | T=Boolean False")]
    [InlineData("dotted-I-range", "ChrW(&H130)", "\"[h-j]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("dash-chain-d", "\"d\"", "\"[a-c-e]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("dash-chain-dash", "\"-\"", "\"[a-c-e]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("dash-chain-e", "\"e\"", "\"[a-c-e]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("not-close-x", "\"x\"", "\"[!]]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("not-close-bang", "\"!]\"", "\"[!]]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("lazy-invalid", "\"x\"", "\"y[\"", "B=Boolean False | T=Boolean False")]
    [InlineData("lazy-invalid-range", "\"ab\"", "\"b[z-a]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("invalid-after-star", "\"\"", "\"*[\"", "B=Boolean False | T=Boolean False")]
    [InlineData("invalid-after-star-x", "\"abc\"", "\"a*[\"", "B=err 93 Invalid pattern string | T=err 93 Invalid pattern string")]
    [InlineData("invalid-unreached-match", "\"a\"", "\"a[z-a]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("empty-list-star", "\"abc\"", "\"a[]*\"", "B=Boolean True | T=Boolean True")]
    [InlineData("empty-neg-list", "\"a\"", "\"[!]a\"", "B=Boolean False | T=Boolean False")]
    [InlineData("star-q", "\"ab\"", "\"*?\"", "B=Boolean True | T=Boolean True")]
    [InlineData("star-q-empty", "\"\"", "\"*?\"", "B=Boolean False | T=Boolean False")]
    [InlineData("q-star-q", "\"a\"", "\"?*?\"", "B=Boolean False | T=Boolean False")]
    [InlineData("hash-star", "\"12x\"", "\"#*\"", "B=Boolean True | T=Boolean True")]
    [InlineData("list-upper-text", "\"A\"", "\"[abc]\"", "B=Boolean False | T=Boolean True")]
    [InlineData("notlist-upper-text", "\"A\"", "\"[!abc]\"", "B=Boolean True | T=Boolean False")]
    [InlineData("star-text", "\"ABC\"", "\"a*c\"", "B=Boolean False | T=Boolean True")]
    [InlineData("fold-range-upper-bounds", "\"m\"", "\"[A-Z]\"", "B=Boolean False | T=Boolean True")]
    [InlineData("fold-range-mixed", "\"M\"", "\"[a-Z]\"", "B=err 93 Invalid pattern string | T=Boolean True")]
    [InlineData("group-match-then-reversed", "\"a\"", "\"[az-a]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("group-match-then-unclosed", "\"a\"", "\"[ab\"", "B=err 93 Invalid pattern string | T=err 93 Invalid pattern string")]
    [InlineData("empty-input-reversed", "\"\"", "\"[z-a]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("empty-input-unclosed", "\"\"", "\"[a\"", "B=Boolean False | T=Boolean False")]
    [InlineData("empty-input-open", "\"\"", "\"[\"", "B=Boolean False | T=Boolean False")]
    [InlineData("empty-input-after-literal", "\"\"", "\"a[\"", "B=Boolean False | T=Boolean False")]
    [InlineData("star-then-unclosed", "\"a\"", "\"*[\"", "B=err 93 Invalid pattern string | T=err 93 Invalid pattern string")]
    [InlineData("star-then-reversed-nomatch", "\"a\"", "\"*[z-a]\"", "B=err 93 Invalid pattern string | T=err 93 Invalid pattern string")]
    [InlineData("literal-fails-before-group", "\"b\"", "\"a[z-a]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("group-fails-before-group", "\"b\"", "\"[a][z-a]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("empty-list-end", "\"a\"", "\"a[]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("empty-list-twice", "\"a\"", "\"[][]a[]\"", "B=Boolean True | T=Boolean True")]
    [InlineData("empty-neg-list-only", "\"\"", "\"[!]\"", "B=Boolean False | T=Boolean False")]
    [InlineData("array", "Array(1)", "\"*\"", "B=err 13 Type mismatch | T=err 13 Type mismatch")]
    [InlineData("object-nothing", "Nothing", "\"*\"", "B=err 91 Object variable or With block variable not set | T=err 91 Object variable or With block variable not set")]
    [InlineData("decimal", "CDec(1.5)", "\"1.5\"", "B=Boolean True | T=Boolean True")]
    [InlineData("single", "CSng(0.1)", "\"0.1\"", "B=Boolean True | T=Boolean True")]
    [InlineData("date-only", "#1/2/2003#", "\"*/*/*\"", "B=Boolean True | T=Boolean True")]
    [InlineData("long-pattern", "\"a\"", "String(5000, \"*\") & \"a\"", "B=Boolean True | T=Boolean True")]
    [InlineData("hash-0", "\"0\"", "\"#\"", "B=Boolean True | T=Boolean True")]
    [InlineData("hash-9", "\"9\"", "\"#\"", "B=Boolean True | T=Boolean True")]
    [InlineData("hash-slash", "\"/\"", "\"#\"", "B=Boolean False | T=Boolean False")]
    [InlineData("hash-colon", "\":\"", "\"#\"", "B=Boolean False | T=Boolean False")]
    public async Task Like_answers_as_vb6_exe_does(string label, string s, string p, string answer)
    {
        await Row($"R \"{label}\", {s}, {p}");

        AssertDebugLog([new Vb6Value($"{label}: {answer}")]);
    }

    // VB6's Text comparison takes a ligature or a sharp s for the letters it stands for, so "ß" Like "ss" is True there.
    // Matching a character at a time cannot, so these are False under Text here, as Split and Filter are (#1479). Each
    // row gives vb6.exe's answer beside HexIDE's, so the divergence is pinned rather than left to drift.
    [Theory]
    [InlineData("sz-ss", "Chr(223)", "\"ss\"", "B=Boolean False | T=Boolean True", "B=Boolean False | T=Boolean False")]
    [InlineData("ae-lig", "Chr(230)", "\"ae\"", "B=Boolean False | T=Boolean True", "B=Boolean False | T=Boolean False")]
    [InlineData("sz-s-star", "Chr(223)", "\"s*\"", "B=Boolean False | T=Boolean True", "B=Boolean False | T=Boolean False")]
    [InlineData("ss-sz", "\"ss\"", "Chr(223)", "B=Boolean False | T=Boolean True", "B=Boolean False | T=Boolean False")]
    [InlineData("ss-list-sz", "\"ss\"", "\"[\" & Chr(223) & \"]\"", "B=Boolean False | T=Boolean True", "B=Boolean False | T=Boolean False")]
    [InlineData("ae-star", "Chr(230) & \"x\"", "\"ae*\"", "B=Boolean False | T=Boolean True", "B=Boolean False | T=Boolean False")]
    [InlineData("ae-split", "Chr(230)", "\"a*\"", "B=Boolean False | T=Boolean True", "B=Boolean False | T=Boolean False")]
    [InlineData("sz-text-mid", "\"ma\" & Chr(223) & \"e\"", "\"mass?\"", "B=Boolean False | T=Boolean True", "B=Boolean False | T=Boolean False")]
    [InlineData("ss-text-q", "\"mass\"", "\"ma\" & Chr(223)", "B=Boolean False | T=Boolean True", "B=Boolean False | T=Boolean False")]
    [InlineData("ss-star-sz", "\"ssx\"", "\"*\" & Chr(223) & \"x\"", "B=Boolean False | T=Boolean True", "B=Boolean False | T=Boolean False")]
    public async Task Like_under_Text_does_not_expand_a_ligature_or_a_sharp_s(string label, string s, string p, string vb6, string ours)
    {
        await Row($"R \"{label}\", {s}, {p}");

        vb6.Should().NotBe(ours);
        AssertDebugLog([new Vb6Value($"{label}: {ours}")]);
    }

    // Typed operands written inline, each in its own procedure, as the probe had them. Each clears Err itself, which
    // VB6's On Error does for it and HexIDE's does not yet (#877).
    [Fact]
    public async Task Like_takes_typed_operands_and_binds_as_vb6_exe_does()
    {
        await Run("""""""
            Option Compare Binary
            Dim sv As String
            Dim iv As Integer
            Dim lv As Long
            Dim dv As Double
            Dim tv As Date

            Public Function Show(v As Variant) As String
                If IsNull(v) Then
                    Show = "Null"
                ElseIf IsEmpty(v) Then
                    Show = "Empty"
                Else
                    Show = TypeName(v) & " " & CStr(v)
                End If
            End Function

            Sub P0()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = ("a" Like "[a")
                If Err.Number <> 0 Then
                    Debug.Print "lit-unclosed: err " & Err.Number & " " & Err.Description
                Else
                    Debug.Print "lit-unclosed: " & Show(r)
                End If
            End Sub
            Sub P1()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = ("a" Like "[]")
                If Err.Number <> 0 Then
                    Debug.Print "lit-empty-list: err " & Err.Number & " " & Err.Description
                Else
                    Debug.Print "lit-empty-list: " & Show(r)
                End If
            End Sub
            Sub P2()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = ("b" Like "[z-a]")
                If Err.Number <> 0 Then
                    Debug.Print "lit-reversed: err " & Err.Number & " " & Err.Description
                Else
                    Debug.Print "lit-reversed: " & Show(r)
                End If
            End Sub
            Sub P3()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = (5 Like "#")
                If Err.Number <> 0 Then
                    Debug.Print "lit-int: err " & Err.Number & " " & Err.Description
                Else
                    Debug.Print "lit-int: " & Show(r)
                End If
            End Sub
            Sub P4()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = ("5" Like 5)
                If Err.Number <> 0 Then
                    Debug.Print "lit-int-pat: err " & Err.Number & " " & Err.Description
                Else
                    Debug.Print "lit-int-pat: " & Show(r)
                End If
            End Sub
            Sub P5()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = (5.5 Like "#.#")
                If Err.Number <> 0 Then
                    Debug.Print "lit-double: err " & Err.Number & " " & Err.Description
                Else
                    Debug.Print "lit-double: " & Show(r)
                End If
            End Sub
            Sub P6()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = (True Like "True")
                If Err.Number <> 0 Then
                    Debug.Print "lit-bool: err " & Err.Number & " " & Err.Description
                Else
                    Debug.Print "lit-bool: " & Show(r)
                End If
            End Sub
            Sub P7()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = (Null Like "*")
                If Err.Number <> 0 Then
                    Debug.Print "lit-null: err " & Err.Number & " " & Err.Description
                Else
                    Debug.Print "lit-null: " & Show(r)
                End If
            End Sub
            Sub P8()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = ("a" Like Null)
                If Err.Number <> 0 Then
                    Debug.Print "lit-null-p: err " & Err.Number & " " & Err.Description
                Else
                    Debug.Print "lit-null-p: " & Show(r)
                End If
            End Sub
            Sub P9()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = (sv Like "a*")
                If Err.Number <> 0 Then
                    Debug.Print "lit-string-var: err " & Err.Number & " " & Err.Description
                Else
                    Debug.Print "lit-string-var: " & Show(r)
                End If
            End Sub
            Sub P10()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = (iv Like "##")
                If Err.Number <> 0 Then
                    Debug.Print "lit-int-var: err " & Err.Number & " " & Err.Description
                Else
                    Debug.Print "lit-int-var: " & Show(r)
                End If
            End Sub
            Sub P11()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = (lv Like "-#")
                If Err.Number <> 0 Then
                    Debug.Print "lit-long-var: err " & Err.Number & " " & Err.Description
                Else
                    Debug.Print "lit-long-var: " & Show(r)
                End If
            End Sub
            Sub P12()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = (dv Like "0.5")
                If Err.Number <> 0 Then
                    Debug.Print "lit-double-var: err " & Err.Number & " " & Err.Description
                Else
                    Debug.Print "lit-double-var: " & Show(r)
                End If
            End Sub
            Sub P13()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = (tv Like "*:*")
                If Err.Number <> 0 Then
                    Debug.Print "lit-date-var: err " & Err.Number & " " & Err.Description
                Else
                    Debug.Print "lit-date-var: " & Show(r)
                End If
            End Sub
            Sub P14()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = ("a" Like "a" = True)
                If Err.Number <> 0 Then
                    Debug.Print "lit-chain: err " & Err.Number & " " & Err.Description
                Else
                    Debug.Print "lit-chain: " & Show(r)
                End If
            End Sub
            Sub P15()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = (Not "a" Like "b")
                If Err.Number <> 0 Then
                    Debug.Print "lit-not: err " & Err.Number & " " & Err.Description
                Else
                    Debug.Print "lit-not: " & Show(r)
                End If
            End Sub
            Sub P16()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = ("a" & "b" Like "ab")
                If Err.Number <> 0 Then
                    Debug.Print "lit-concat: err " & Err.Number & " " & Err.Description
                Else
                    Debug.Print "lit-concat: " & Show(r)
                End If
            End Sub

            sv = "abc"
            iv = 42
            lv = -7
            dv = 0.5
            tv = #1/2/2003 3:04:05 PM#
            P0
            P1
            P2
            P3
            P4
            P5
            P6
            P7
            P8
            P9
            P10
            P11
            P12
            P13
            P14
            P15
            P16
            """"""");

        AssertDebugLog(Lines("""
            lit-unclosed: err 93 Invalid pattern string
            lit-empty-list: Boolean False
            lit-reversed: err 93 Invalid pattern string
            lit-int: Boolean True
            lit-int-pat: Boolean True
            lit-double: Boolean True
            lit-bool: Boolean True
            lit-null: Null
            lit-null-p: Null
            lit-string-var: Boolean True
            lit-int-var: Boolean True
            lit-long-var: Boolean True
            lit-double-var: Boolean True
            lit-date-var: Boolean True
            lit-chain: Boolean True
            lit-not: Boolean True
            lit-concat: Boolean True
            """));
    }

    private static List<Vb6Value> Lines(string text) =>
        text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => new Vb6Value(l)).ToList();
}
