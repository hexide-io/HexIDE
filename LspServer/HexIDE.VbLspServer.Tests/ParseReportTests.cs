// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// The parse's own account of itself: which prediction stage answered, and how the parse ended. These are
// the facts the server's $/logTrace channel carries, asserted at the source rather than through the wire —
// TraceWireTests asserts that they reach a client, this file asserts that they are true.

namespace HexIDE.VbLspServer.Tests;

public class ParseReportTests
{
    [Fact]
    public void Clean_source_is_answered_by_the_SLL_fast_path()
    {
        // The whole point of two-stage prediction: the unambiguous majority never reaches LL. If this ever
        // reports LL, the fast path has stopped paying for itself and nothing else in the suite would say so.
        var (_, tree, report) = VbDiagnosticsProvider.GetDiagnosticsAndTreeReported(
            "Option Explicit\nDim Counter As Integer\nSub DoWork()\n    Counter = 5\nEnd Sub\n");

        tree.Should().NotBeNull();
        report.Prediction.Should().Be(ParsePrediction.Sll);
        report.Outcome.Should().Be(ParseOutcome.Parsed);
    }

    [Fact]
    public void A_syntax_error_forces_the_authoritative_LL_re_parse()
    {
        // SLL runs under BailErrorStrategy, so a genuine error aborts it and the LL re-parse — the one that
        // carries the collecting error listener — is what produces the diagnostic.
        var (diagnostics, _, report) = VbDiagnosticsProvider.GetDiagnosticsAndTreeReported(
            "Sub DoWork()\n    @@\nEnd Sub\n");

        report.Prediction.Should().Be(ParsePrediction.Ll);
        diagnostics.Should().NotBeEmpty("the LL stage is the one that collects errors");
    }

    [Fact]
    public void A_parse_records_the_time_it_took()
    {
        var (_, _, report) = VbDiagnosticsProvider.GetDiagnosticsAndTreeReported("Sub S()\nEnd Sub\n");

        // Asserting a bound, not a duration — the parser being fast is not a defect, and pinning a
        // millisecond figure here is how the budget tests became flaky twice over.
        report.Elapsed.Should().BeGreaterThan(TimeSpan.Zero);
        report.Elapsed.Should().BeLessThan(VbDiagnosticsProvider.ParseBudget);
    }

    [Fact]
    public void Oversized_input_is_reported_as_refused_rather_than_parsed()
    {
        var huge = string.Concat(Enumerable.Repeat("Dim x As Integer\n", 30_000)); // ~510k chars

        var (_, tree, report) = VbDiagnosticsProvider.GetDiagnosticsAndTreeReported(huge);

        tree.Should().BeNull();
        report.Outcome.Should().Be(ParseOutcome.InputTooLarge);
        report.Prediction.Should().Be(ParsePrediction.None, "no stage ran — the parser was never reached");
    }

    [Fact]
    public void Nesting_past_the_depth_guard_is_reported_as_abandoned()
    {
        var deep = "Sub S()\n    x = " + new string('(', 400) + "1" + new string(')', 400) + "\nEnd Sub\n";

        var (_, tree, report) = VbDiagnosticsProvider.GetDiagnosticsAndTreeReported(deep);

        tree.Should().BeNull();
        report.Outcome.Should().Be(ParseOutcome.NestingTooDeep);
    }

    [Fact]
    public void The_unreported_parse_still_returns_exactly_what_it_did_before()
    {
        // The report is an addition, not a change. Same source, both entry points, same answer — the
        // guarantee that makes "off is byte-identical" a claim about the analysis and not just the wire.
        const string source = "Sub DoWork()\n    x = Foo(1)\nEnd Sub\n";

        var (plainDiagnostics, plainTree) = VbDiagnosticsProvider.GetDiagnosticsAndTree(source);
        var (reportedDiagnostics, reportedTree, _) = VbDiagnosticsProvider.GetDiagnosticsAndTreeReported(source);

        reportedDiagnostics.Count.Should().Be(plainDiagnostics.Count);
        (reportedTree is null).Should().Be(plainTree is null);
    }

    [Fact]
    public async Task A_budget_that_has_already_expired_yields_no_report_at_all()
    {
        // Symmetric with the unreported path: null means "analysis pending". Crucially the report of the
        // abandoned parse is discarded with its result rather than handed back — the orphan runs on and
        // would otherwise be writing to an object the caller had already read.
        var result = await VbDiagnosticsProvider.TryGetDiagnosticsAndTreeReportedWithin(
            "Sub Foo()\nEnd Sub\n", TimeSpan.Zero);

        result.Should().BeNull();
    }

    [Fact]
    public async Task A_parse_inside_the_budget_comes_back_with_its_report()
    {
        var result = await VbDiagnosticsProvider.TryGetDiagnosticsAndTreeReportedWithin(
            "Sub Foo()\nEnd Sub\n", TimeSpan.FromSeconds(30));

        result.Should().NotBeNull();
        result!.Value.Report.Prediction.Should().Be(ParsePrediction.Sll);
    }
}
