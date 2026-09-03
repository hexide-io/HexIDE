using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexIDE.Runtime.Interpreter;
using HexIDE.IDE;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The behavioural half of the conformance corpus: not "does this parse", but "does it PRINT what
/// vb6.exe printed".
///
/// <para>
/// <b>Why this exists.</b> <see cref="CorpusConformanceTests"/> compares one bit per case — did HexIDE's
/// parser accept what the compiler accepted. That gate is real and it caught real defects, but it is
/// structurally blind to every case that parses correctly and then does the wrong thing, and four such
/// defects shipped in a single session while it stayed green: an <c>Else:</c> that swallowed its own
/// branch, a line label that ate the statement beside it, a module-qualified type in a <c>Dim</c>, and
/// the whole of module scope. Each had corpus cases. Each of those cases already called
/// <c>Debug.Print</c>. The gate simply never asked what came out.
/// </para>
///
/// <para>
/// <b>Where the expectations come from.</b> Not from us. <c>scripts/vb6-legality.ps1 -CaptureOutput</c>
/// rewrites each case's <c>Debug.Print</c> to a helper that appends to a file, compiles it with the real
/// vb6.exe, RUNS it, and records what the file received. So a row here is an observation of the actual
/// compiler, in the same class of evidence as <c>docs/vb6-fidelity-oracle.md</c> — and unlike a hand-written
/// unit test it cannot be satisfied by asserting whatever the interpreter happens to do. That distinction
/// is not hypothetical: <c>ModuleScopeTests</c> pins a UDT field declared <c>As Long</c> as coming back
/// <b>Integer</b>, green, with a comment noting it is wrong. Only an oracle-backed expectation catches that.
/// </para>
///
/// <para>
/// <b>The type is compared, not just the text.</b> Each recorded line is <c>TypeName TAB value</c>, because
/// a gate that diffed rendered text alone would pass an Integer where VB6 gives a Long and a Single where it
/// gives a Double — which is exactly where a wrong value hides. CLAUDE.md's ranking is explicit that a late
/// error is acceptable and a wrong value never is; this is the gate for the second half of that sentence.
/// </para>
///
/// <para>
/// <b>Scope, and why it is only part of the corpus.</b> Statement-scope cases only. A module-scope case is a
/// whole module whose entry point is <c>Sub Main</c>, and the interpreter has no <c>Sub Main</c> startup
/// object — <c>ProjectSerializer</c> reads <c>Startup="Sub Main"</c> from the .vbp and
/// <c>BasicInterpreter</c> still describes it as "a future Sub Main". So 82 captured cases are measured and
/// waiting, and cannot be gated until that lands. They are counted, not quietly dropped: see
/// <see cref="RecordedBehaviourIsPresentAndItsCoverageIsHonest"/>.
/// </para>
/// </summary>
public class CorpusBehaviourTests
{
    private sealed record Row(string Key, string Area, string Actual, string? Ran, string? Output);

    /// <summary>Cases where the interpreter is KNOWN to print something other than what vb6.exe printed,
    /// grouped by CAUSE rather than listed one by one — a list of keys tells a later reader nothing about
    /// whether two entries are one bug or two.</summary>
    ///
    /// <remarks>
    /// Every entry here is a wrong value or a missing one, which CLAUDE.md ranks above a late error and
    /// below nothing. None of them is acceptable indefinitely; they are recorded so the gate can be turned
    /// on today rather than deferred until they are all fixed, which is how a gate never arrives at all.
    /// <see cref="KnownBehaviourDivergencesAreStillReal"/> fails when one is fixed but left listed, so the
    /// list can only shrink deliberately.
    /// </remarks>
    private static readonly Dictionary<string, string> KnownDivergences = new(StringComparer.Ordinal)
    {
        // ---- WRONG VALUE, SILENTLY. The most serious thing this gate can find, and the reason it -----
        // ---- exists: every one of these parses, runs, raises nothing, and answers differently. -------

        // DECLARED-TYPE-ON-STORE. A UDT field declared `As Long` (or `As Double`) does not coerce the
        // stored value to its declared width, so reading it back gives an Integer. Eleven cases, one
        // cause, and now the largest wrong-value cluster in the corpus.
        //
        // It was already known: ModuleScopeTests pins one instance in an assertion with a comment saying
        // it is wrong ("NB Integer, not Long … asserted as it behaves so this test measures the one thing
        // it is for"). What changed is that it is now GATED rather than noted — every one of these cases
        // was measured against vb6.exe long ago and sat unchecked because its entry point is Sub Main.
        // The fix belongs with VbNumeric.CoerceOnStore, which already implements exactly this rule for a
        // declared scalar; a UDT field is simply not routed through it.
        ["aggregate-visibility/two-private-types-same-name"] = "DECLARED-TYPE-ON-STORE: Integer, VB6 says Long",
        ["aggregate-visibility/public-and-private-type-same-name"] = "DECLARED-TYPE-ON-STORE: Integer, VB6 says Long",
        ["aggregate-visibility/two-foreign-public-types-and-no-local-one"] = "DECLARED-TYPE-ON-STORE: Integer, VB6 says Long",
        ["aggregate-visibility/ambiguous-type-disambiguated-by-module-name"] = "DECLARED-TYPE-ON-STORE: Integer, VB6 says Long",
        ["aggregate-visibility/unambiguous-type-still-reachable-by-module-name"] = "DECLARED-TYPE-ON-STORE: Integer, VB6 says Long",
        ["aggregate-visibility/local-type-still-wins-when-unqualified"] = "DECLARED-TYPE-ON-STORE: Integer, VB6 says Long",
        ["aggregate-visibility/module-prefix-overrides-a-local-declaration"] = "DECLARED-TYPE-ON-STORE: Integer, VB6 says Double",
        ["gap-fill/with-block-colon-tight-against-dot-and-end"] = "DECLARED-TYPE-ON-STORE: Integer, VB6 says Long",
        ["separator-basics/sep-udt-one-line"] = "DECLARED-TYPE-ON-STORE: Integer, VB6 says Long",
        ["separator-with-declarations/type-member-continued-before-as"] = "DECLARED-TYPE-ON-STORE: Integer, VB6 says Long",
        ["separator-with-declarations/type-members-colon-joined"] = "DECLARED-TYPE-ON-STORE: Integer, VB6 says Long",

        // DECLARE-NOT-CALLABLE. A `Declare` naming an external DLL entry point is parsed but the call
        // does nothing, so a probe that reports whether the API succeeded prints False where VB6 prints
        // True. Platform, not language — these need a real P/Invoke and are Windows-gated, which
        // OUT_OF_SCOPE.md puts in scope but not here.
        ["keyword-splitting/lib-then-continuation-before-the-string"] = "DECLARE-NOT-CALLABLE: prints False, VB6 True",
        ["keyword-splitting/lib-with-no-space-before-the-string"] = "DECLARE-NOT-CALLABLE: prints False, VB6 True",
        ["keyword-splitting/alias-then-continuation-before-the-string"] = "DECLARE-NOT-CALLABLE: prints False, VB6 True",
        ["separator-with-declarations/declare-continued-across-lines"] = "DECLARE-NOT-CALLABLE: prints False, VB6 True",
        ["separator-with-declarations/declare-continued-after-alias-keyword"] = "DECLARE-NOT-CALLABLE: prints False, VB6 True",

        // ---- REFUSES CODE VB6 RAN, newly visible now that module-scope cases are gated. --------------

        // NAMED-ARGUMENT-CONTINUATION. A line continuation inside a `name := value` argument.
        ["continuation-basics/cont-named-arg-after-assign"] = "NAMED-ARG-CONTINUATION: NotImplementedException",
        ["gap-fill/named-argument-continuation-before-colon-equals"] = "NAMED-ARG-CONTINUATION: NotImplementedException",

        // CONDITIONAL-COMPILATION-CONTINUATION. `#If` and `#Const` with a continued expression or value.
        ["separator-with-declarations/hashif-expression-continued"] = "COMPILER-DIRECTIVE-CONTINUATION: NotImplementedException",
        ["separator-with-declarations/hashconst-value-continued"] = "COMPILER-DIRECTIVE-CONTINUATION: VBCompileErrorException",

        // PROJECT-QUALIFIED-VALUE. `Project1.Module1.MyEnum.Foo` and `Project1.Foo` resolve at parse
        // level but not at run time when entered through Sub Main.
        ["qualifier-depth/value-position-project-module-enum-member"] = "PROJECT-QUALIFIER: VBVariableNotDefinedException",
        ["qualifier-depth/value-position-project-member"] = "PROJECT-QUALIFIER: VBVariableNotDefinedException",

        // Two singletons, each its own cause.
        ["rem-forms/then-colon-with-the-body-on-the-next-line"] = "THEN-COLON-BODY-NEXT-LINE: VBCompileErrorException",
        ["aggregate-visibility/two-private-enums-same-name"] = "PRIVATE-ENUM-AGGREGATION: member not found",
        ["continuation-basics/cont-declare-header-wrapped"] = "DECLARE-NOT-CALLABLE: header wrapped, Sub not defined",

        // EQUALITY-OPERATOR-STRING-VS-NUMERIC. `"1" = 1` and `"1.0" = 1` are both True in VB6; HexIDE
        // raises Type mismatch, because GetTwoValuesSameTypesOrNull has no String/numeric path and falls
        // through to its throw. Found while measuring Select Case, and deliberately NOT fixed with it: the
        // two are different rules. Select Case coerces the case value to the SELECTOR's type, so
        // `Select Case "1.0" / Case 1` does NOT match — while `"1.0" = 1` does. Sharing one helper between
        // them, which is the obvious move, would break whichever one it was not written for.
        ["select-case-matching/equality-operator-string-against-numeric"] =
            "EQ-OPERATOR-COERCION: raises Type mismatch, VB6 says True",
        ["select-case-matching/equality-operator-string-numerically-equal-textually-different"] =
            "EQ-OPERATOR-COERCION: raises Type mismatch, VB6 says True",

        // RESUME-NEXT-LANDS-PAST-THE-ARM. Both of these now raise the RIGHT error (6 for the overflowing
        // coercion, 13 for the unparseable one) and print it. They differ only in where On Error Resume
        // Next resumes to: VB6 continues INTO the matched arm's body and prints MATCH first, HexIDE
        // resumes past the whole statement. A resume-point question, not a Select Case one — and the
        // unhandled form of both cases (recorded `hung`, i.e. VB6 put up a modal) is the semantics that
        // actually matters, which HexIDE now reproduces.
        ["select-case-matching/integer-selector-out-of-range-case-error-number"] =
            "RESUME-POINT: right error (6), but Resume Next skips the arm body VB6 enters",
        ["select-case-matching/numeric-selector-unparseable-string-case-error-number"] =
            "RESUME-POINT: right error (13), but Resume Next skips the arm body VB6 enters",

        // SPLIT-IDENTIFIER-IS-TWO-NAMES. VB6 does not join an identifier across a line continuation — the
        // halves are separate names, so the read is of an undeclared Variant and yields 0. HexIDE joins
        // them and reads the real variable, answering 5. Accepting more than VB6 would be tolerable; this
        // returns a DIFFERENT NUMBER, which is not.
        ["continuation-illegal/split-identifier"] = "SPLIT-IDENTIFIER: prints Long 5, VB6 prints Long 0",

        // ---- REFUSES CODE VB6 RAN. A false rejection at run time rather than parse time — the parse ---
        // ---- gate is blind to these too, because the module parses and then dies executing. ----------

        // CONTINUATION-INSIDE-AN-EXPRESSION. A continuation between an open paren and its argument, or
        // before a comma in an argument list, reaches an unimplemented path.
        ["continuation-basics/cont-after-open-paren"] = "EXPR-CONTINUATION: NotImplementedException",
        ["continuation-basics/cont-arglist-before-comma"] = "EXPR-CONTINUATION: NotImplementedException",
        ["continuation-illegal/continuation-inside-argument-list"] = "EXPR-CONTINUATION: NotImplementedException",
        ["continuation-vs-identifier/trailing-underscore-then-type-hint"] = "EXPR-CONTINUATION: NotImplementedException",

        // LEADING-COLON. A statement separator opening a line, or standing alone between statements.
        ["separator-basics/sep-leading-colon-start-of-line"] = "LEADING-COLON: VBCompileErrorException",
        ["separator-basics/sep-spaced-empty-statements"] = "LEADING-COLON: VBCompileErrorException",
        ["separator-vs-label/leading-colon-on-line"] = "LEADING-COLON: VBCompileErrorException",

        // LABEL-BEFORE-A-BLOCK-TERMINATOR. A label immediately preceding End If / a loop terminator. Note
        // these print the FIRST value correctly and then die, which is why the recorded prefix matters.
        ["line-labels/label-immediately-before-a-loop-terminator"] = "LABEL-BEFORE-TERMINATOR: dies after the first print",
        ["separator-in-control-flow/label-before-endif"] = "LABEL-BEFORE-TERMINATOR: dies after the first print",
        ["separator-vs-label/label-before-end-if"] = "LABEL-BEFORE-TERMINATOR: dies after the first print",

        // GOSUB. Not a defect but the tree-walking limit CLAUDE.md names explicitly: a visitor-frame stack
        // has no position to return to. Listed so the count is honest, not because it is fixable here.
        ["gap-fill/gosub-return-colon-joined-one-line"] = "GOSUB: the execution-strategy limit, by design",

        // FILE I/O. Two file numbers across a colon — the largest Dies family in MISSING_LANGUAGE.md.
        ["gap-fill/two-file-numbers-across-a-colon"] = "FILE-IO: VBCompileErrorException",
    };

    [Fact]
    public async Task HexIDE_PrintsWhatVB6Printed()
    {
        var (all, gated, _, _) = await Compare();
        var divergences = all.Where(d => !KnownDivergences.ContainsKey(d.Split(' ')[0])).ToList();

        divergences.Should().BeEmpty(
            "vb6.exe was compiled, run, and its output recorded for each of these cases, so a difference "
          + "here is the interpreter producing a different ANSWER — not merely a different error, which "
          + "CLAUDE.md permits, but a different value, which it never does. {0} cases were gated.\n{1}",
            gated, string.Join("\n", divergences));
    }

    [Fact]
    public async Task RecordedBehaviourIsPresentAndItsCoverageIsHonest()
    {
        // The failure mode a corpus gate dies of is going quiet: results.json loses its Output column, every
        // case is skipped for want of an expectation, and the suite stays green while guarding nothing.
        var (_, gated, captured, ungated) = await Compare();

        captured.Should().BeGreaterThan(150,
            "results.json should carry recorded vb6.exe output — regenerate with "
          + "scripts/vb6-legality.ps1 -CaptureOutput; found {0}", captured);

        gated.Should().BeGreaterThan(150,
            "{0} cases carry recorded output but only {1} are actually run and compared. The rest are "
          + "module-scope cases awaiting a Sub Main startup object. If that ratio moved, say why.",
            captured, gated);

        // Not an assertion — a standing report, so the untested remainder stays visible rather than
        // becoming invisible the moment someone stops reading the diff.
        ungated.Should().BeLessThan(200,
            "cases measured against vb6.exe but not gated here: {0}", ungated);
    }

    [Fact]
    public async Task KnownBehaviourDivergencesAreStillReal()
    {
        var (divergences, _, _, _) = await Compare();
        var stillWrong = divergences.Select(d => d.Split(' ')[0]).ToHashSet(StringComparer.Ordinal);

        foreach (var (key, why) in KnownDivergences)
            stillWrong.Should().Contain(key,
                "KnownDivergences lists '{0}' ({1}), but the interpreter now matches vb6.exe — remove the "
              + "entry, because a stale exemption licenses a future regression under a reason that has "
              + "stopped applying", key, why);
    }

    // ------------------------------------------------------------------------------------------------

    private static async Task<(List<string> Divergences, int Gated, int Captured, int Ungated)> Compare()
    {
        var dir = Path.Combine(RepoRoot(), "corpus", "conformance");
        var resultsPath = Path.Combine(dir, "results.json");
        File.Exists(resultsPath).Should().BeTrue(
            "the recorded verdicts should be at {0} — regenerate with scripts/vb6-legality.ps1", resultsPath);

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var rows = JsonSerializer.Deserialize<List<Row>>(File.ReadAllText(resultsPath), opts)!
            // audit.json is a findings REPORT reusing the case schema, not runnable cases.
            .Where(r => r.Area != "audit")
            .ToDictionary(r => r.Key, r => r, StringComparer.Ordinal);

        var captured = rows.Values.Count(r => r.Ran == "ok" && r.Output is not null);
        var divergences = new List<string>();
        var gated = 0;

        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name is "results" or "audit") continue;

            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var el in doc.RootElement.GetProperty("cases").EnumerateArray())
            {
                var key = name + "/" + el.GetProperty("id").GetString();
                if (!rows.TryGetValue(key, out var row)) continue;
                if (row.Ran != "ok" || row.Output is null) continue;

                var scope = el.TryGetProperty("scope", out var s) ? s.GetString() : "statement";
                var code = string.Join("\n",
                    el.GetProperty("code").EnumerateArray().Select(c => c.GetString()));

                // Extra standard modules, exactly as the harness wrote them: Module2, Module3, …
                var extras = new List<(string Name, string Code)>();
                if (el.TryGetProperty("modules", out var mods))
                    foreach (var m in mods.EnumerateArray())
                        extras.Add(("Module" + (extras.Count + 2),
                            string.Join("\n", m.EnumerateArray().Select(l => l.GetString()))));

                gated++;
                var actual = scope == "module"
                    ? await RunModuleScopeAndRender(code, extras)
                    : await RunAndRender(code);
                var expected = Normalise(row.Output);

                if (actual == expected) continue;

                // Every divergence is returned, exempt or not. Filtering here instead would leave
                // KnownBehaviourDivergencesAreStillReal unable to tell "fixed" from "excluded", and its
                // whole job is to fail when an entry has stopped being true.
                divergences.Add($"{key} vb6=[{Show(expected)}] hexide=[{Show(actual)}]");
            }
        }

        return (divergences, gated, captured, captured - gated);
    }

    /// <summary>Run one case and render its Debug output the way the VB6 helper rendered it: one
    /// <c>TypeName TAB value</c> line per print.</summary>
    /// <summary>Run a MODULE-scope case: the source is a whole standard module, entered through its
    /// <c>Sub Main</c> startup object, with any further modules loaded alongside.</summary>
    ///
    /// <remarks>
    /// The harness appends `Sub Main() / End Sub` when no module declares a `Main`, because the .vbp names
    /// it as the startup and will not build without one — so a case that declares none was measured as a
    /// program that runs and prints nothing. This mirrors that rule, INCLUDING scanning every module
    /// rather than only the primary: getting that wrong on the harness side produced two confident wrong
    /// facts about VB6 before it was caught, and building a different program here than the one vb6.exe
    /// was asked about would reintroduce the same class of error from the other end.
    /// </remarks>
    private static async Task<string> RunModuleScopeAndRender(
        string code, List<(string Name, string Code)> extras)
    {
        var declaresMain = new[] { code }.Concat(extras.Select(e => e.Code))
            .SelectMany(m => m.Split('\n'))
            .Any(l => System.Text.RegularExpressions.Regex.IsMatch(
                l.Trim(), @"^(Public |Private |Friend )?(Sub|Function) +Main\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase));

        var primary = declaresMain ? code : code + "\n\nSub Main()\nEnd Sub\n";

        var debug = new List<Vb6Value>();
        try
        {
            var vb = new BasicInterpreter(new CaptureStdLib(debug), new ModuleExecutionContext(),
                new ExecutionEnvironment(), primary, "Module1",
                extras.Count == 0 ? null : extras.ToArray());

            var run = vb.RunStartupSubMain();
            if (await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10))) != run)
                return "<did not terminate>";
            await run;
        }
        catch (Exception ex)
        {
            return Render(debug) + (debug.Count > 0 ? "\n" : "") + "<error: " + ex.GetType().Name + ">";
        }

        return Render(debug);
    }

    private static async Task<string> RunAndRender(string code)
    {
        var debug = new List<Vb6Value>();
        try
        {
            var vb = new BasicInterpreter(
                new CaptureStdLib(debug), new ModuleExecutionContext(), new ExecutionEnvironment(), code);

            // A construct vb6.exe ran to completion could still spin here. The runaway walk is not
            // cancellable, so it is abandoned rather than awaited — the test reports a divergence instead
            // of hanging the whole suite behind one case.
            var run = vb.Execute();
            if (await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10))) != run)
                return "<did not terminate>";
            await run;
        }
        catch (Exception ex)
        {
            // A raised error is a legitimate outcome to compare: vb6.exe recorded whatever it printed
            // BEFORE dying, so the prefix still has to match. The marker keeps the two distinguishable.
            lock (debug) { }
            return Render(debug) + (debug.Count > 0 ? "\n" : "") + "<error: " + ex.GetType().Name + ">";
        }

        return Render(debug);
    }

    private static string Render(List<Vb6Value> values)
    {
        var sb = new StringBuilder();
        lock (values)
        {
            foreach (var v in values)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(TypeNameOf(v)).Append('\t').Append(AsText(v));
            }
        }
        return sb.ToString();
    }

    /// <summary>VB6's <c>TypeName</c>, written out here rather than reused from the interpreter ON PURPOSE:
    /// if both sides rendered through the same production helper, a bug in it would cancel out and hide
    /// itself. The expectation came from vb6.exe, so the rendering must be independent of the thing under
    /// test.</summary>
    private static string TypeNameOf(Vb6Value v)
    {
        // An if-chain rather than a switch because ValueType's members are static readonly fields, not
        // constants, so they cannot appear as patterns.
        var t = v.Type;
        if (t == Vb6Value.ValueType.String) return "String";
        if (t == Vb6Value.ValueType.Integer) return "Integer";
        if (t == Vb6Value.ValueType.Long) return "Long";
        if (t == Vb6Value.ValueType.Byte) return "Byte";
        if (t == Vb6Value.ValueType.Boolean) return "Boolean";
        if (t == Vb6Value.ValueType.Double) return "Double";
        if (t == Vb6Value.ValueType.Single) return "Single";
        if (t == Vb6Value.ValueType.Currency) return "Currency";
        if (t == Vb6Value.ValueType.Decimal) return "Decimal";
        if (t == Vb6Value.ValueType.Date) return "Date";
        if (t == Vb6Value.ValueType.EmptyVariant) return "Empty";
        if (t == Vb6Value.ValueType.Null) return "Null";
        if (t == Vb6Value.ValueType.Nothing) return "Nothing";
        return t.ToString() ?? "?";
    }

    /// <summary>What <c>CStr</c> gave the helper. Invariant throughout: the recorded file came from a
    /// machine whose locale is not this one's, and a decimal comma here would read as a divergence.</summary>
    private static string AsText(Vb6Value v) => v.Value switch
    {
        null => "",
        bool b => b ? "True" : "False",
        string s => s,
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
        IFormattable n => n.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        var o => o.ToString() ?? "",
    };

    /// <summary>The recorded output is newline-joined by the harness; normalise the line ending only.</summary>
    private static string Normalise(string recorded) => recorded.Replace("\r\n", "\n").TrimEnd('\n');

    private static string Show(string s) => s.Replace("\t", "\\t").Replace("\n", " | ");

    private sealed class CaptureStdLib(List<Vb6Value> debug) : IBasicStandardLibrary
    {
        public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons,
            MessageBoxIcon icon) => Task.FromResult(default(MessageBoxResult));

        public Task<string?> InputBox(string prompt, string? title, string defaultText) =>
            Task.FromResult<string?>(null);

        // Locked for the reason MockStdLib documents: two interpreter walks really can reach here at once,
        // and List<T>.Add is a lost update when they do.
        public void DebugPrint(Vb6Value value) { lock (debug) debug.Add(value); }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                             && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;

        dir.Should().NotBeNull("the repository root should be findable from {0}", AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
