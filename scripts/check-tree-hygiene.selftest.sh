#!/usr/bin/env bash
# Does the hygiene guard actually see what it claims to, and refuse it as hard as it claims to?
#
# WHY THIS EXISTS. Three of the guard's scans listed tracked files only, while its own header said
# every scan saw untracked ones too (#437). CI could never have noticed: everything is tracked by
# the time a runner checks out, so those scans were exercised solely against the case they already
# handled. The run that was wrong is the local pre-push one, which is the only one that happens
# before bytes leave the machine.
#
# A guard whose failure mode is a FALSE GREEN cannot be verified by watching it pass. It has to be
# handed something bad and observed catching it. So this plants a file of each kind the guard is
# meant to refuse -- one per scan, all eight -- runs the guard for real, and fails if any goes
# unreported.
#
# ASSERTIONS MATCH THE RENDERED LINE, NOT THE FILENAME, and that is the difference between this
# file having teeth and merely looking as though it does. An earlier draft matched `planted.pem`
# anywhere in the output, which cannot tell FAIL from WARN: flipping the guard so that a
# COMMITTABLE private key merely warned left every one of its checks green. Matching
# `X private key file: <path>` pins the scan, the severity and the subject at once.
#
# THREE RUNS, in this order, because each earns the next:
#   A. the tree as it stands          -- must pass, or nothing below means anything
#   B. only material that should WARN -- must still pass, with the warnings named
#   C. one probe per scan             -- every one reported, and the run must fail
#
# Each run is a full guard invocation and the guard is not fast (the Markdown link scan dominates).
# That is the price of the B run in particular, which is the only thing anywhere that pins "a
# maintainer holding the dev signing key still gets a green" -- the ergonomic the warn/fail split
# exists for, and one nothing else would notice the loss of.
set -uo pipefail
cd "$(dirname "$0")/.." || exit 2

GUARD="scripts/check-tree-hygiene.sh"
PROBE=".hygiene-selftest"
failures=0

# Built rather than written, so the comparison is against the same bytes the guard emits and this
# file needs no particular encoding to survive whatever handles it in transit.
FAILMARK="$(printf '\xE2\x9C\x97')"
WARNMARK="$(printf '\xE2\x9A\xA0')"

# A name git C-quotes in its default listing ("caf\303\251.pem"), which is how an accented private
# key stayed invisible to all three converted scans until they went NUL-delimited.
ACCENTED="caf$(printf '\xC3\xA9')"

# Every forbidden literal below is ASSEMBLED AT RUN TIME. This file is deliberately NOT in the
# guard's EXCLUDE list -- it is the one file most likely to grow a real path or a real key by
# accident, so it stays under the content scans, which means it must not contain what it plants.
PEM_HEADER="$(printf -- '-----BEGIN RSA PRIVATE %s-----' KEY)"
MACHINE_PATH="$(printf 'C:\\%s\\somebody\\notes.txt' Users)"
NEIGHBOUR="$(printf 'twin%s' BASIC)"
UPSTREAM="$(printf 'Avalonia%s' VisualBasic)"
IDENTITY="zzselftest$(printf '%s' persona)"

# Planted files must never survive a failure, or the next run of the guard reports them as real.
cleanup() { rm -rf "${PROBE:?}"; }
trap cleanup EXIT
trap 'cleanup; exit 130' INT TERM
cleanup

ok()  { printf '  \xE2\x9C\x93 %s\n' "$1"; }
bad() { printf '  \xE2\x9C\x97 %s\n' "$1"; failures=$((failures + 1)); }

# Set by run_guard, read by the assertions.
output=""
status=0

# The identity scan's pattern comes from a repository secret and is not available here, so it is
# supplied explicitly per run: empty for A and B, a synthetic one for C.
run_guard() {
  output="$(HEXIDE_IDENTITY_PATTERN="${1:-}" bash "$GUARD" 2>&1)"
  status=$?
}

seen()   { case "$output" in *"$2"*) ok "$1";; *) bad "$1 - nothing in the guard's output matched: $2";; esac; }
absent() { case "$output" in *"$2"*) bad "$1 - but the output contains: $2";; *) ok "$1";; esac; }

echo "check-tree-hygiene.selftest: handing the guard things it must refuse..."
echo
echo "A. the tree as it stands"

# THE RUN THAT MAKES THE REST MEAN ANYTHING, and it goes first so a dirty tree is diagnosed as a
# dirty tree rather than as a scatter of mysterious failures. A guard that refused every tree it
# was shown would satisfy every assertion below this one.
run_guard ''
if [ "$status" -eq 0 ]; then
  ok "a tree with no probes in it passes"
else
  bad "this tree does not pass the guard on its own, so nothing below could be attributed to a probe."
  printf '%s\n' "$output" | sed 's/^/      /'
  echo
  echo "check-tree-hygiene.selftest: ABORTED - clean the working tree, then re-run."
  exit 1
fi
# An unset identity pattern must announce itself. The guard's own note says a check that quietly
# does nothing is worse than no check, because it still reads green; this is that note, asserted.
seen "an unarmed identity scan says so rather than passing silently" "identity scan SKIPPED"

echo
echo "B. only material that should WARN"

# The warn half of the split, which no assertion in the C run can reach: once anything fails, the
# exit status is 1 whatever the key scan decided. A maintainer legitimately holds the dev signing
# key on disk (HexIDE.Desktop.csproj gates on it), so an IGNORED key must warn and still pass. Two
# mutations one character apart kill this and nothing else would notice: making warned() set fail,
# or swapping the two branches of the key scan.
mkdir -p "$PROBE"
printf 'not a real key\n' > "$PROBE/planted.key"
printf '# planted\n\nDerived from %s6, with thanks.\n' "$UPSTREAM" > "$PROBE/attribution.md"

run_guard ''
seen   "a gitignored key is seen and WARNS" "$WARNMARK private key material on disk, gitignored so not committable as things stand: $PROBE/planted.key"
seen   "the upstream attribution is seen and WARNS" "$WARNMARK $UPSTREAM mentioned"
absent "nothing in a warn-only tree is reported as a failure" "$FAILMARK"
if [ "$status" -eq 0 ]; then
  ok "a tree whose only findings are warnings still passes"
else
  bad "warnings failed the run - the dev-key ergonomic the warn/fail split exists for is gone"
fi

cleanup

echo
echo "C. one probe per scan"

mkdir -p "$PROBE"
printf 'not a real key\n'                                 > "$PROBE/planted.pem"   # 1a key filename, committable
printf 'not a real key\n'                                 > "$PROBE/planted.key"   # 1a key filename, gitignored
printf '%s\nnot a real key\n' "$PEM_HEADER"               > "$PROBE/secret.txt"    # 1b PEM block content
printf 'MZ not a real binary\n'                           > "$PROBE/planted.exe"   # 2  build artefact
printf 'see %s\n' "$MACHINE_PATH"                         > "$PROBE/paths.md"      # 3  machine-specific path
printf '%s\n' "$IDENTITY"                                 > "$PROBE/identity.md"   # 4  personal identity
printf '# planted\n\nSee [nothing](./no-such-file.md).\n' > "$PROBE/planted.md"    # 5  dangling link
printf 'A note about %s.\n' "$NEIGHBOUR"                  > "$PROBE/neighbour.md"  # 6  third-party naming

# The same three scans again, under a name git C-quotes. Each was blind to this until `-z`.
printf 'not a real key\n'                                 > "$PROBE/$ACCENTED.pem"
printf 'MZ not a real binary\n'                           > "$PROBE/$ACCENTED.exe"
printf '# planted\n\nSee [nothing](./no-such-file.md).\n' > "$PROBE/$ACCENTED.md"

run_guard "$IDENTITY"

seen "1a. a committable key extension FAILS"       "$FAILMARK private key file: $PROBE/planted.pem"
seen "1a. a gitignored key is still seen"          "$WARNMARK private key material on disk, gitignored so not committable as things stand: $PROBE/planted.key"
seen "1b. a PEM block in a plain file FAILS"       "$FAILMARK PEM private-key block: $PROBE/secret.txt"
seen "2.  an untracked build artefact FAILS"       "$FAILMARK build artefact / backup in the tree: $PROBE/planted.exe"
seen "3.  a machine-specific absolute path FAILS"  "$FAILMARK machine-specific absolute path: $PROBE/paths.md:1:"
seen "4.  an armed identity scan FAILS on a hit"   "$FAILMARK personal-identity reference: $PROBE/identity.md:1:"
seen "5.  a dangling link in Markdown FAILS"       "$FAILMARK dangling relative link: $PROBE/planted.md->./no-such-file.md"
seen "6.  an unagreed third-party name FAILS"      "$FAILMARK third-party project named outside the agreed places: $PROBE/neighbour.md"

seen "1a. ...and sees it under an accented name"   "$FAILMARK private key file: $PROBE/$ACCENTED.pem"
seen "2.  ...and sees it under an accented name"   "$FAILMARK build artefact / backup in the tree: $PROBE/$ACCENTED.exe"
seen "5.  ...and sees it under an accented name"   "$FAILMARK dangling relative link: $PROBE/$ACCENTED.md->./no-such-file.md"

if [ "$status" -ne 0 ]; then
  ok "the guard exits non-zero when something refusable is present"
else
  bad "the guard exited 0 with planted files present - it reports but does not fail"
fi

cleanup

echo
if [ "$failures" -eq 0 ]; then
  echo "check-tree-hygiene.selftest: OK - every scan sees untracked files and refuses them as documented."
  exit 0
fi

# Without this a CI failure reads "nothing matched: X private key file: ..." with nothing to
# diagnose from, which is a poor way to find out that git was not on PATH.
echo "check-tree-hygiene.selftest: FAILED - $failures check(s) above. Last guard run (exit $status):"
printf '%s\n' "$output" | sed 's/^/      /'
exit 1
