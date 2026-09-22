#!/usr/bin/env bash
# Does the hygiene guard actually see what it claims to, and refuse it as hard as it claims to?
#
# WHY THIS EXISTS. Three of the guard's scans listed tracked files only, while its own header said every
# scan saw untracked ones too (#437). CI could never have noticed: everything is tracked by the time a
# runner checks out, so those scans were exercised solely against the case they already handled. The run
# that was wrong is the local pre-push one, which is the only one that happens before bytes leave the
# machine.
#
# A guard whose failure mode is a FALSE GREEN cannot be verified by watching it pass. It has to be handed
# something bad and observed catching it. So this plants a probe for every scan and every branch within a
# scan, runs the guard for real, and fails if any goes unreported.
#
# FOUR PROPERTIES ARE PINNED, and each was added because a mutation slipped past without it.
#
#  1. THE RENDERED LINE, not the filename. Matching `planted.pem` anywhere in the output cannot tell FAIL
#     from WARN: flipping the guard so a COMMITTABLE private key merely warned left every check green.
#     Matching `X private key file: <path>` pins scan, severity and subject at once.
#
#  2. TRACKED FILES, not only untracked ones. Every probe here used to be untracked, so the `--cached` half
#     of both listings was never exercised -- and deleting it, which reads as removing the redundant half
#     of a pair in a change all about untracked files, makes a STAGED OR COMMITTED private key completely
#     invisible while this file still reports every assertion green. Run D stages probes into a THROWAWAY
#     INDEX (`GIT_INDEX_FILE`), so the repository's real index is never touched and an interrupted run
#     cannot leave anything staged.
#
#  3. THE FAILURE COUNT. A scan whose `note` runs in a subshell still prints its line and loses the
#     increment, so the guard reports a problem and exits 0. Only an exact count catches that, and it
#     catches a deleted scan in the same assertion.
#
#  4. THIS FILE'S OWN INTEGRITY. Deleting the catch-all branch from `seen()` makes every assertion pass
#     vacuously; `[ "$status" ]` instead of `[ "$status" -eq 1 ]` is always true. So the assertions are
#     counted and the total is itself asserted.
#
# FOUR RUNS, in this order, because each earns the next:
#   A. the tree as it stands          -- must pass, or nothing below means anything
#   B. only material that should WARN -- must still pass, with the warnings named
#   C. one probe per scan and branch  -- every one reported, and the run must fail
#   D. the same probes, but STAGED    -- the index half of both listings
#
# The guard is invoked from a SUBDIRECTORY every time. It begins by cd-ing to the repository root, and
# without that line every listing is silently limited to wherever it was started from; run from the root,
# a deleted cd is a no-op and nothing here would notice.
#
# Each run is a full guard invocation and the guard is not fast (the Markdown link scan dominates). Run B
# is the price of pinning "a maintainer holding the dev signing key still gets a green" -- the ergonomic
# the warn/fail split exists for, and one nothing else would notice the loss of.
set -uo pipefail
cd "$(dirname "$0")/.." || exit 2

GUARD="scripts/check-tree-hygiene.sh"
FROM="IDE"          # any tracked subdirectory; the guard must cd out of it by itself
PROBE=".hygiene-selftest"
failures=0
checks=0

# Bump these when adding or removing an assertion or a probe. They are not bookkeeping: an assertion that
# stops running, and a scan that stops reporting, are both invisible without them.
#
# The two failure totals differ by exactly two, and that difference IS the point of run D: staging
# `planted.key` and `planted.pfx` moves them from the warn branch to the fail branch, because git has
# stopped standing between them and a push.
EXPECTED_CHECKS=65
FAILURES_UNTRACKED=17
FAILURES_STAGED=19

# Built rather than written, so the comparison is against the same bytes the guard emits and this file
# needs no particular encoding to survive.
FAILMARK="$(printf '\xE2\x9C\x97')"
WARNMARK="$(printf '\xE2\x9A\xA0')"

# A name git C-quotes in its default listing ("caf\303\251.pem"), which is how an accented private key
# stayed invisible to all three converted scans until they went NUL-delimited.
ACCENTED="caf$(printf '\xC3\xA9')"

# Every forbidden literal below is ASSEMBLED AT RUN TIME. This file is deliberately NOT in the guard's
# EXCLUDE list -- it is the one file most likely to grow a real path or a real key by accident, so it stays
# under the content scans, which means it must not contain what it plants.
PEM_RSA="$(printf -- '-----BEGIN RSA PRIVATE %s-----' KEY)"
PEM_PKCS8="$(printf -- '-----BEGIN PRIVATE %s-----' KEY)"
MACHINE_PATH="$(printf 'C:\\%s\\somebody\\notes.txt' Users)"
NEIGHBOUR_A="$(printf 'twin%s' BASIC)"
NEIGHBOUR_B="$(printf 'RD%s' Core)"
NEIGHBOUR_C="$(printf 'Rubber%s' Duck)"
UPSTREAM="$(printf 'Avalonia%s' VisualBasic)"
# Deliberately UPPER CASE in the file while the pattern below is lower case: the identity scan greps -i,
# and losing that one letter is a single-character edit.
IDENTITY="zzselftest$(printf '%s' persona)"
IDENTITY_IN_FILE="ZZSELFTEST$(printf '%s' PERSONA)"
# A pattern that matches nothing, for the armed-and-clean case. Split for the same reason the names above
# are: written whole, it would sit in this file and the armed scan would find it here, so the run that must
# come back clean would come back with one hit -- against the selftest itself.
NO_IDENTITY="zzno$(printf '%s' match)$(printf '%s' here)"
# Conflict markers, built rather than written: at the start of a line in THIS file they would be a real hit
# against the selftest itself. SEVEN of each, as git writes them, and the setext underline is the one
# character the scan must never take for a marker: a bare run of '=' is also how Markdown underlines a heading.
MARK_OURS="$(printf '<%.0s' 1 2 3 4 5 6 7) HEAD"
MARK_THEIRS="$(printf '>%.0s' 1 2 3 4 5 6 7) upstream/main"
MARK_SPLIT="$(printf '=%.0s' 1 2 3 4 5 6 7)"

# A private copy of the index, so staging a probe cannot touch the repository's own. Nothing here ever
# runs `git add` against the real index, which means an interrupted run leaves no staged files behind.
STAGED_INDEX="$(mktemp -t hygiene-selftest-index.XXXXXX)"

cleanup() { rm -rf "${PROBE:?}"; rm -f "$STAGED_INDEX"; }
trap cleanup EXIT
trap 'cleanup; exit 130' INT TERM
rm -rf "${PROBE:?}"

ok()  { printf '  \xE2\x9C\x93 %s\n' "$1"; checks=$((checks + 1)); }
bad() { printf '  \xE2\x9C\x97 %s\n' "$1"; checks=$((checks + 1)); failures=$((failures + 1)); }

output=""
status=0

# $1 = identity pattern (empty to leave the scan unarmed), $2 = index file (empty for the real one).
run_guard() {
  if [ -n "${2:-}" ]; then
    output="$(cd "$FROM" && GIT_INDEX_FILE="$2" HEXIDE_IDENTITY_PATTERN="${1:-}" bash "../$GUARD" 2>&1)"
  else
    output="$(cd "$FROM" && HEXIDE_IDENTITY_PATTERN="${1:-}" bash "../$GUARD" 2>&1)"
  fi
  status=$?
}

seen()   { case "$output" in *"$2"*) ok "$1";; *) bad "$1 - nothing in the guard's output matched: $2";; esac; }
absent() { case "$output" in *"$2"*) bad "$1 - but the output contains: $2";; *) ok "$1";; esac; }
exited() { if [ "$status" -eq "$2" ]; then ok "$1"; else bad "$1 - exit was $status, wanted $2"; fi; }

# Everything in this file is only as trustworthy as those three lines, and each is one edit away from
# passing unconditionally: delete the catch-all branch from `seen`, or write `[ "$status" ]` for
# `[ "$status" -eq 1 ]`, and every assertion below reports success whatever the guard did. Counting the
# assertions does not catch either, and the reason is worth stating -- on a green run nothing was going to
# fail, so a helper that has lost its ability to report a failure behaves identically to one that has not.
#
# The only way to know a helper still works is to hand it a failure and watch it record one. Their output
# is discarded so the planted failures cannot be mistaken for real ones; only the verdict is printed.
harness_self_check() {
  local before=$failures saved_checks=$checks after_seen after_exited
  output="a line that the needle below cannot possibly match"
  status=0

  seen   "planted" "THIS NEEDLE IS DELIBERATELY ABSENT" >/dev/null
  after_seen=$failures
  exited "planted" 1 >/dev/null                      # status is 0, so this must be recorded as a failure
  after_exited=$failures

  failures=$before
  checks=$saved_checks
  if [ "$after_seen" -eq $((before + 1)) ] && [ "$after_exited" -eq $((before + 2)) ]; then
    ok "the assertion helpers still report a failure when handed one"
  else
    bad "the assertion helpers did NOT report a planted failure, so every check below would be meaningless"
  fi
}

# Every probe the C and D runs need. Called twice; the D run stages some of them afterwards.
plant() {
  mkdir -p "$PROBE"
  printf 'not a real key\n'                                 > "$PROBE/planted.pem"   # 1a committable
  printf 'not a real key\n'                                 > "$PROBE/planted.key"   # 1a gitignored
  printf 'not a real key\n'                                 > "$PROBE/planted.p12"   # 1a committable
  printf 'not a real key\n'                                 > "$PROBE/planted.pfx"   # 1a gitignored
  printf 'not a real key\n'                                 > "$PROBE/UPPER.PEM"     # 1a case-insensitive
  printf '%s\nnot a real key\n' "$PEM_RSA"                  > "$PROBE/secret.txt"    # 1b PEM, algorithm named
  printf '%s\nnot a real key\n' "$PEM_PKCS8"                > "$PROBE/secret8.txt"   # 1b PEM, PKCS#8
  printf 'MZ not a real binary\n'                           > "$PROBE/planted.exe"   # 2
  printf 'see %s\n' "$MACHINE_PATH"                         > "$PROBE/paths.md"      # 3
  printf '%s\n' "$IDENTITY_IN_FILE"                         > "$PROBE/identity.md"   # 4
  printf '# planted\n\nSee [nothing](./no-such-file.md).\n' > "$PROBE/planted.md"    # 5
  printf 'A note about %s.\n' "$NEIGHBOUR_A"                > "$PROBE/neighbour1.md" # 6, one per alternative
  printf 'A note about %s.\n' "$NEIGHBOUR_B"                > "$PROBE/neighbour2.md"
  printf 'A note about %s.\n' "$NEIGHBOUR_C"                > "$PROBE/neighbour3.md"
  printf '%s\nours\n%s\ntheirs\n%s\n' "$MARK_OURS" "$MARK_SPLIT" "$MARK_THEIRS" > "$PROBE/conflict.md" # 8

  # The three converted scans again, under a name git C-quotes. Each was blind to this until `-z`.
  printf 'not a real key\n'                                 > "$PROBE/$ACCENTED.pem"
  printf 'MZ not a real binary\n'                           > "$PROBE/$ACCENTED.exe"
  printf '# planted\n\nSee [nothing](./no-such-file.md).\n' > "$PROBE/$ACCENTED.md"
}

# The rendered line each probe must produce. Shared by runs C and D so the two cannot drift apart.
#   $1 = how many failures the summary must count
#   $2 = what the two GITIGNORED key extensions must do: "warn" untracked, "fail" once staged
assert_every_scan_reported() {
  local ignored_key ignored_pfx verdict
  if [ "$2" = "warn" ]; then
    verdict="WARNS"
    ignored_key="$WARNMARK private key material on disk, gitignored so not committable as things stand: $PROBE/planted.key"
    ignored_pfx="$WARNMARK private key material on disk, gitignored so not committable as things stand: $PROBE/planted.pfx"
  else
    verdict="FAILS"
    ignored_key="$FAILMARK private key file: $PROBE/planted.key"
    ignored_pfx="$FAILMARK private key file: $PROBE/planted.pfx"
  fi

  seen "1a. a committable key extension FAILS"       "$FAILMARK private key file: $PROBE/planted.pem"
  seen "1a. .p12 is in the extension list too"       "$FAILMARK private key file: $PROBE/planted.p12"
  seen "1a. the match is case-insensitive"           "$FAILMARK private key file: $PROBE/UPPER.PEM"
  seen "1a. a gitignored .key is seen, and $verdict" "$ignored_key"
  seen "1a. a gitignored .pfx is seen, and $verdict" "$ignored_pfx"
  seen "1b. a PEM block naming its algorithm FAILS"  "$FAILMARK PEM private-key block: $PROBE/secret.txt"
  seen "1b. a PKCS#8 block FAILS (no algorithm)"     "$FAILMARK PEM private-key block: $PROBE/secret8.txt"
  seen "2.  a build artefact FAILS"                  "$FAILMARK build artefact / backup in the tree: $PROBE/planted.exe"
  seen "3.  a machine-specific absolute path FAILS"  "$FAILMARK machine-specific absolute path: $PROBE/paths.md:1:"
  seen "4.  an armed identity scan FAILS, any case"  "$FAILMARK personal-identity reference: $PROBE/identity.md:1:"
  seen "5.  a dangling link in Markdown FAILS"       "$FAILMARK dangling relative link: $PROBE/planted.md->./no-such-file.md"
  seen "6.  an unagreed third-party name FAILS"      "$FAILMARK third-party project named outside the agreed places: $PROBE/neighbour1.md"
  seen "6.  ...for every name in the list"           "$FAILMARK third-party project named outside the agreed places: $PROBE/neighbour2.md"
  seen "6.  ...including the third"                  "$FAILMARK third-party project named outside the agreed places: $PROBE/neighbour3.md"
  seen "8.  an opening conflict marker FAILS"        "$FAILMARK unresolved merge-conflict marker: $PROBE/conflict.md:1:"
  seen "8.  ...and so does the closing one"          "$FAILMARK unresolved merge-conflict marker: $PROBE/conflict.md:5:"
  seen "1a. ...and sees an accented filename"        "$FAILMARK private key file: $PROBE/$ACCENTED.pem"
  seen "2.  ...and sees an accented filename"        "$FAILMARK build artefact / backup in the tree: $PROBE/$ACCENTED.exe"
  seen "5.  ...and sees an accented filename"        "$FAILMARK dangling relative link: $PROBE/$ACCENTED.md->./no-such-file.md"

  # The count is what catches a `note` lost to a subshell: the line is still printed, the increment is not.
  seen "the summary counts every failure"            "check-tree-hygiene: FAILED"
  seen "...and the count is exactly $1"              "$1 item(s) above."
  exited "the guard exits 1 when something refusable is present" 1
}

echo "check-tree-hygiene.selftest: handing the guard things it must refuse..."
echo
echo "0. this file's own assertion helpers"
harness_self_check

echo
echo "A. the tree as it stands"

# THE RUN THAT MAKES THE REST MEAN ANYTHING, and it goes first so a dirty tree is diagnosed as a dirty tree
# rather than as a scatter of mysterious failures. A guard that refused every tree it was shown would
# satisfy every assertion below this one.
run_guard '' ''
if [ "$status" -eq 0 ]; then
  ok "a tree with no probes in it passes"
else
  bad "this tree does not pass the guard on its own, so nothing below could be attributed to a probe."
  printf '%s\n' "$output" | sed 's/^/      /'
  echo
  echo "check-tree-hygiene.selftest: ABORTED - clean the working tree, then re-run."
  exit 1
fi
# An unset identity pattern must announce itself. The guard's own note says a check that quietly does
# nothing is worse than no check, because it still reads green; this is that note, asserted.
seen "an unarmed identity scan says so rather than passing silently" "identity scan SKIPPED"
seen "a clean tree says so in as many words" "check-tree-hygiene: OK"

# The same argument one level down. Armed and finding nothing, the scan used to print nothing, which is the
# same empty space it would leave if it had been deleted -- and the SKIPPED line above cannot help, because
# its absence is what the reader would have to notice. So the clean pass is stated too, and asserted here
# with a pattern that matches nothing here.
run_guard "$NO_IDENTITY" ''
seen "an armed identity scan that finds nothing says so too" "identity scan clean"
absent "...and does not also claim it was skipped" "identity scan SKIPPED"
exited "...and the tree still passes" 0

# Back to the unarmed run, because the assertions below read the output of the run above them.
run_guard '' ''

echo
echo "B. only material that should WARN"

# The warn half of the split, which no assertion in a failing run can reach: once anything fails, the exit
# status is 1 whatever the key scan decided. A maintainer legitimately holds the dev signing key on disk
# (HexIDE.Desktop.csproj gates on it), so an IGNORED key must warn and still pass.
mkdir -p "$PROBE"
printf 'not a real key\n' > "$PROBE/planted.key"
printf '# planted\n\nDerived from %s6, with thanks.\n' "$UPSTREAM" > "$PROBE/attribution.md"
# A setext heading. Its underline is the bare middle marker of a conflict block, and it must not fail.
printf 'Planted heading\n%s\n\nText.\n' "$MARK_SPLIT" > "$PROBE/setext.md"

run_guard '' ''
seen   "a gitignored key is seen and WARNS" "$WARNMARK private key material on disk, gitignored so not committable as things stand: $PROBE/planted.key"
seen   "the upstream attribution is seen and WARNS" "$WARNMARK $UPSTREAM mentioned"
absent "nothing in a warn-only tree is reported as a failure" "$FAILMARK"
absent "a setext heading underline is not taken for a conflict marker" "unresolved merge-conflict marker"
seen   "the summary still says OK" "check-tree-hygiene: OK"
exited "a tree whose only findings are warnings still passes" 0

rm -rf "${PROBE:?}"

echo
echo "C. one probe per scan, untracked"

plant
run_guard "$IDENTITY" ''
assert_every_scan_reported "$FAILURES_UNTRACKED" warn

echo
echo "D. the same probes, staged"

# The index half of both listings. `--cached` is what sees a key that has been `git add`ed -- one commit
# from the push this guard exists to gate -- and nothing above exercises it, because everything above is
# untracked. Staged into a private index, so the repository's own is untouched.
cp "$(git rev-parse --git-path index)" "$STAGED_INDEX"
GIT_INDEX_FILE="$STAGED_INDEX" git add -f -- "$PROBE" >/dev/null 2>&1

run_guard "$IDENTITY" "$STAGED_INDEX"
# "fail", not "warn": the severity flip is the assertion. A gitignored key that has been staged is one
# commit from the push this guard gates, so it must stop warning and start failing -- which is also why
# check-ignore is asked WITHOUT --no-index, since that flag would ignore the index and keep it a warning.
assert_every_scan_reported "$FAILURES_STAGED" fail

cleanup

echo
echo "E. a tree git cannot read"

# The preflight, which every run above passes through without exercising: in this repository git can always
# read the tree. So the guard is copied somewhere git cannot, and run there. GIT_CEILING_DIRECTORIES stops
# git searching upward from the copy, so a temp directory that happens to sit inside some other repository
# still reads as "not a repository" (#533).
UNREADABLE="$(mktemp -d -t hygiene-selftest-norepo.XXXXXX)"
mkdir -p "$UNREADABLE/plain/scripts" "$UNREADABLE/ignored/scripts" "$UNREADABLE/clean/scripts"
cp "$GUARD" "$UNREADABLE/plain/scripts/"
cp "$GUARD" "$UNREADABLE/ignored/scripts/"
cp "$GUARD" "$UNREADABLE/clean/scripts/"
ceiling="$UNREADABLE"

output="$(cd "$UNREADABLE/plain" && GIT_CEILING_DIRECTORIES="$ceiling" bash scripts/check-tree-hygiene.sh 2>&1)"
status=$?
exited "outside any repository, the guard refuses to run (exit 2)" 2
seen   "...and says git cannot read a work tree" "git cannot read a work tree"
absent "...and never claims the tree is clean" "check-tree-hygiene: OK"

# A repository whose .gitignore ignores everything, the guard included: git answers, and lists nothing.
( cd "$UNREADABLE/ignored" && git init -q . && printf '*\n' > .gitignore )
output="$(cd "$UNREADABLE/ignored" && GIT_CEILING_DIRECTORIES="$ceiling" bash scripts/check-tree-hygiene.sh 2>&1)"
status=$?
exited "a repository that lists no files is refused too (exit 2)" 2
seen   "...and says git lists no files" "git lists no files"
absent "...and never claims the tree is clean" "check-tree-hygiene: OK"
# And a tree with nothing to report at all: only the guard, which its own content scans exclude. This one
# is the only tree here with ZERO warnings -- the repository itself always carries the upstream
# attribution -- so it is the only place the warning count's zero case can be seen.
( cd "$UNREADABLE/clean" && git init -q . )
output="$(cd "$UNREADABLE/clean" && GIT_CEILING_DIRECTORIES="$ceiling" bash scripts/check-tree-hygiene.sh 2>&1)"
status=$?
seen   "a readable tree with nothing to report says OK" "check-tree-hygiene: OK"
absent "...and prints no warning count when there are none" "warning(s)"
rm -rf "${UNREADABLE:?}"

echo
# Without this, deleting the catch-all branch from seen() makes every assertion above pass vacuously and
# this file still prints its success banner.
if [ "$checks" -ne "$EXPECTED_CHECKS" ]; then
  echo "check-tree-hygiene.selftest: FAILED - ran $checks assertions, expected $EXPECTED_CHECKS."
  echo "      Either an assertion stopped running, or one was added without updating EXPECTED_CHECKS."
  exit 1
fi

if [ "$failures" -eq 0 ]; then
  echo "check-tree-hygiene.selftest: OK - $checks assertions; every scan sees tracked and untracked files"
  echo "                             alike, and refuses them as documented."
  exit 0
fi

# Without this a CI failure reads "nothing matched: X private key file: ..." with nothing to diagnose from,
# which is a poor way to find out that git was not on PATH.
echo "check-tree-hygiene.selftest: FAILED - $failures of $checks check(s) above. Last guard run (exit $status):"
printf '%s\n' "$output" | sed 's/^/      /'
exit 1
