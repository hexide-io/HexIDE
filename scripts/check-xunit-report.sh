#!/usr/bin/env bash
# Fails when an xunit v3 report records errors, or when there is no report to read.
#
#   bash scripts/check-xunit-report.sh <report.xml>...
#
# xunit counts a failure outside a test as an error, not as a failed test: a test case that throws before its test
# starts is one. xunit's MTP adapter reports such an error only for a test case that has already started, so a run
# can lose hundreds of tests and still print Passed!, exit 0 and pass a minimum-test floor with room to spare (#2486).
# The report written by --report-xunit keeps the count. A report that is missing or has no totals fails too: a check
# that passes when it read nothing is no check at all.
set -euo pipefail

if [ $# -lt 1 ]; then
    sed -n '4p' "$0" >&2
    exit 2
fi

annotate() {
    if [ "${GITHUB_ACTIONS:-}" = true ]; then echo "::error title=xunit report::$1"; fi
}

status=0
for report in "$@"; do
    if [ ! -s "$report" ]; then
        echo "check-xunit-report: FAILED: there is no report at $report"
        annotate "There is no xunit report at $report, so the run's errors were not checked."
        status=1
        continue
    fi
    # sed, not head, takes the first lines: head closes the pipe early, and the SIGPIPE that grep then dies of fails
    # the whole script under pipefail before it has said why.
    totals=$(grep -o '<assembly [^>]*>' "$report" | sed -n '1p' || true)
    errors=$(printf '%s' "$totals" | sed -n 's/.* errors="\([0-9][0-9]*\)".*/\1/p')
    total=$(printf '%s' "$totals" | sed -n 's/.* total="\([0-9][0-9]*\)".*/\1/p')
    if [ -z "$errors" ] || [ -z "$total" ]; then
        echo "check-xunit-report: FAILED: $report has no assembly totals to read"
        annotate "$report has no assembly totals, so the run's errors were not checked."
        status=1
        continue
    fi
    if [ "$errors" -eq 0 ]; then
        echo "check-xunit-report: OK: $report records $total tests run and no errors"
        continue
    fi
    echo "check-xunit-report: FAILED: $report records $errors errors beside the $total tests run, and MTP counted none of them"
    echo "The first of them (kind, test case, message):"
    grep -o '<error type="[^"]*" name="[^"]*"><failure[^>]*><message><!\[CDATA\[[^]]*' "$report" |
        sed -n '1,10{s/<error type="\([^"]*\)" name="\([^"]*\)"><failure[^>]*><message><!\[CDATA\[/  \1  \2: /;s/&quot;/"/g;s/&apos;/'"'"'/g;s/&lt;/</g;s/&gt;/>/g;s/&amp;/\&/g;p;}'
    annotate "xunit recorded $errors errors that MTP did not count, beside $total tests run. The step log lists the first ten."
    status=1
done
exit $status
