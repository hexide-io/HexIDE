#!/usr/bin/env bash
# Hand the xunit report check a clean report, one with errors, a missing one and one with no totals, and watch it
# pass only the first. A check whose failure mode is a false green has to be shown catching something.
set -euo pipefail
cd "$(dirname "$0")/.."
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

assembly='<assembly name="Example.Tests.dll" environment="64-bit [collection-per-class, parallel (4 threads)]"'
printf '%s errors="0" failed="0" not-run="0" passed="3" skipped="0" total="3"><collection/></assembly>' \
    "<assemblies>$assembly" > "$work/clean.xml"
echo '</assemblies>' >> "$work/clean.xml"
{
    printf '%s errors="2" failed="0" not-run="0" passed="1" skipped="0" total="1"><collection/>' "<assemblies>$assembly"
    printf '<errors><error type="test-case-cleanup" name="Example.Tests.A.First"><failure exception-type="System.InvalidOperationException"><message><![CDATA[System.InvalidOperationException : Cannot get KeyValueStorage on the idle test context]]></message><stack-trace><![CDATA[   at Example\n   at Elsewhere]]></stack-trace></failure></error>'
    printf '<error type="test-case-cleanup" name="Example.Tests.A.Second"><failure exception-type="System.InvalidOperationException"><message><![CDATA[System.InvalidOperationException : Cannot get KeyValueStorage on the idle test context]]></message></failure></error></errors>'
    echo '</assembly></assemblies>'
} > "$work/errors.xml"
echo '<assemblies></assemblies>' > "$work/no-totals.xml"
# Enough errors to fill a pipe, so its first-ten listing has to stop early without dying of SIGPIPE (a run lost 1542).
{
    printf '%s errors="2000" failed="0" not-run="0" passed="0" skipped="0" total="0"><collection/><errors>' "<assemblies>$assembly"
    i=0
    while [ "$i" -lt 2000 ]; do
        i=$((i + 1))
        printf '<error type="test-case-cleanup" name="Example.Tests.B.Row(text: &quot;%s&quot;)"><failure exception-type="X"><message><![CDATA[X : row %s]]></message></failure></error>' "$i" "$i"
    done
    echo '</errors></assembly></assemblies>'
} > "$work/many.xml"

fail=0
expect() {
    local want=$1 needle=$2 output code
    shift 2
    set +e
    output=$(bash scripts/check-xunit-report.sh ${@+"$@"} 2>&1)
    code=$?
    set -e
    if [ "$code" -ne "$want" ] || ! printf '%s' "$output" | grep -qF -- "$needle"; then
        echo "FAIL: $* exited $code (expected $want), output:"; printf '%s\n' "$output"; fail=$((fail + 1))
    else
        echo "ok: exit $code, says '$needle'"
    fi
}
expect 0 'records 3 tests run and no errors' "$work/clean.xml"
expect 1 'records 2 errors beside the 1 tests run' "$work/errors.xml"
expect 1 'Example.Tests.A.Second: System.InvalidOperationException : Cannot get KeyValueStorage' "$work/errors.xml"
expect 1 'there is no report at' "$work/missing.xml"
expect 1 'has no assembly totals' "$work/no-totals.xml"
expect 1 'records 2 errors' "$work/clean.xml" "$work/errors.xml"
expect 1 'Example.Tests.B.Row(text: "10"): X : row 10' "$work/many.xml"
GITHUB_ACTIONS=true expect 1 '::error title=xunit report::xunit recorded 2000 errors' "$work/many.xml"
expect 2 'check-xunit-report.sh <report.xml>'

if [ "$fail" -ne 0 ]; then echo "check-xunit-report.selftest: $fail FAILED"; exit 1; fi
echo "check-xunit-report.selftest: all passed"
