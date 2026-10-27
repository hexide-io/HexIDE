#!/usr/bin/env bash
# Exercise the same commit scan CI runs, and the real staged-change hook.
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
scanner=$(command -v gitleaks)
mkdir -p "$root/artifacts"
fixture=$(mktemp -d "$root/artifacts/secrets-selftest.XXXXXX")
trap 'rm -rf "$fixture"' EXIT
cd "$fixture"
git init -q -b clean
git config user.email fixture@example.invalid
git config user.name Fixture
cp "$root/.gitleaks.toml" .
mkdir scripts
cp "$root/scripts/pre-commit-secrets.sh" scripts/
git add .
git -c core.hooksPath=/dev/null commit -qm base
base=$(git rev-parse HEAD)
git switch -qc fake-token
# MIT gitleaks v8.30.1 cmd/generate/config/rules/aws.go's deliberate test key.
# Split so this test's source is not itself a credential fixture.
token='AKIA'
token+='LALEMEL33243OLIB'
printf 'aws_access_key = %s\n' "$token" > credential.txt
git add credential.txt
checks=0
failures=0
expect() {
  local want=$1 label=$2 got=0
  shift 2
  "$@" > result.log 2>&1 || got=$?
  if [ "$got" -ne "$want" ]; then
    echo "FAIL: $label (exit $got, expected $want)"; failures=$((failures + 1)); return
  fi
  if grep -Fq "$token" result.log; then echo "FAIL: secret was not redacted"; exit 1; fi
  checks=$((checks + 1))
  echo "ok: $label"
}
# The worktree contains no token, but the index still does.
printf 'removed\n' > credential.txt
expect 1 'staged token rejected and redacted' "$root/scripts/hooks/pre-commit"
grep -q 'leaks found: 1' result.log
git -c core.hooksPath=/dev/null commit -qm token
git add credential.txt
git -c core.hooksPath=/dev/null commit -qm removal
# Run the workflow's actual shell block with a new-branch push event.
awk '/^      - name: Guard tokens in commits$/ { step=1; next }
     step && /^        run: \|$/ { body=1; next }
     body && /^          / { sub(/^          /, ""); print; next }
     body { exit }' "$root/.github/workflows/build.yml" > guard.sh
cp "$root/scripts/check-secrets.sh" scripts/
git update-ref refs/remotes/origin/main HEAD
git -c core.hooksPath=/dev/null commit --allow-empty -qm 'new branch change'
expect 0 'new branch excludes historical token on main' env PR_BASE= PR_HEAD= PUSH_BASE=0000000000000000000000000000000000000000 PUSH_HEAD=HEAD bash -e guard.sh
git update-ref -d refs/remotes/origin/main
expect 1 'new branch without main refuses to scan' env PR_BASE= PR_HEAD= PUSH_BASE= PUSH_HEAD=HEAD bash -e guard.sh
if ! grep -q 'NOT scanned' result.log; then
  echo 'FAIL: unavailable main did not report NOT scanned'; failures=$((failures + 1))
fi
git branch main HEAD~1
git remote add origin "$fixture"
expect 0 'new branch fetches main when its tracking ref is absent' env PR_BASE= PR_HEAD= PUSH_BASE= PUSH_HEAD=HEAD bash -e guard.sh
unrelated=$(printf 'unrelated\n' | git commit-tree "$(git mktree </dev/null)")
git update-ref refs/remotes/origin/main "$unrelated"
expect 1 'unrelated main history refuses to scan' env PR_BASE= PR_HEAD= PUSH_BASE= PUSH_HEAD=HEAD bash -e guard.sh
if ! grep -q 'NOT scanned' result.log; then
  echo 'FAIL: unavailable merge base did not report NOT scanned'; failures=$((failures + 1))
fi
# The digest must be a committed constant, never downloaded alongside the archive.
if ! grep -q 'GITLEAKS_SHA256: 551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb' "$root/.github/workflows/build.yml"; then
  echo 'FAIL: publisher checksum is not pinned in the workflow'; failures=$((failures + 1))
else
  checks=$((checks + 1)); echo 'ok: publisher checksum pinned in workflow'
fi
expect 1 'CI catches a token even when a later commit removes it' bash "$root/scripts/check-secrets.sh" "$base" HEAD
grep -q 'leaks found: 1' result.log
git reset --soft "$base"
git -c core.hooksPath=/dev/null commit -qm 'clean replacement'
expect 0 'CI passes once token commit is removed from the branch' bash "$root/scripts/check-secrets.sh" "$base" HEAD
expect 1 'unavailable history fails closed' bash "$root/scripts/check-secrets.sh" missing-ref HEAD
grep -q 'NOT scanned' result.log
expect 0 'clean index passes' "$root/scripts/hooks/pre-commit"
mkdir no-scanner
ln -s "$(command -v git)" no-scanner/git
expect 1 'missing scanner blocks commit and reports no scan' env PATH="$fixture/no-scanner" /bin/bash "$root/scripts/pre-commit-secrets.sh"
grep -q 'NOT scanned' result.log
# Keep the binary dependency explicit; an unavailable scanner must never count as a passed test.
test -x "$scanner"
echo "$checks secret-scan checks passed."
test "$failures" -eq 0
