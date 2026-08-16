#!/usr/bin/env bash
# check-tree-hygiene.sh — the guards that used to run only at copy-in time.
#
# HISTORY, because it explains the design. This repository used to be produced by a
# script in a separate private repository: it exported that tree, pruned the internal
# parts, and linted the result. Every check below lived in that script, so the rule was
# "a mistake is caught on its way out". Development now happens here directly, there is
# no copy-in, and that whole gate went with it. These are the checks that still make
# sense when the tree IS the deliverable, ported so they run on every push instead.
#
# Fails (exit 1) on: private key material, committed build artefacts, dangling relative
# links in Markdown, machine-specific absolute paths, personal-identity references, and
# a third-party project named outside the places we agreed it may be. Warns (exit 0) on
# the upstream-attribution mention, which is expected and must not block.
#
# Run from anywhere; it operates on the git-tracked tree.
set -uo pipefail
cd "$(dirname "$0")/.." || exit 2

# This script and its sibling necessarily CONTAIN the very strings they search for — a
# literal "PRIVATE KEY", a Windows user path, the third-party names. Excluding both from
# the *content* scans is what stops them flagging themselves. They stay inside the
# *filename* scans below, so a stray key or binary dropped in scripts/ is still caught.
EXCLUDE=( ':!scripts/check-tree-hygiene.sh' ':!scripts/check-no-gpl.sh' )

fail=0
warn=0
note() { printf '  \xE2\x9C\x97 %s\n' "$1"; fail=1; }
warned() { printf '  \xE2\x9A\xA0 %s\n' "$1"; warn=$((warn + 1)); }

echo "check-tree-hygiene: scanning the tracked tree…"

# 1. Private key material — by extension, and by PEM block content.
while IFS= read -r f; do
  [ -n "$f" ] && note "private key file: $f"
done < <(git ls-files | grep -Ei '\.(key|pem|pfx|p12)$')

while IFS= read -r hit; do
  [ -n "$hit" ] && note "PEM private-key block: $hit"
done < <(git grep -lI 'PRIVATE KEY' -- . "${EXCLUDE[@]}")

# 2. Committed build artefacts and editor backups. A binary in a public repository is a
#    thing people are right to distrust: they cannot diff it and cannot tell what is in it.
while IFS= read -r f; do
  [ -n "$f" ] && note "build artefact / backup committed: $f"
done < <(git ls-files | grep -Ei '\.(exe|dll|pdb|bak)$')

# 3. Machine-specific absolute paths. A path from the author's own disk is both useless
#    to a reader and a small identity leak.
while IFS= read -r hit; do
  [ -n "$hit" ] && note "machine-specific absolute path: $hit"
done < <(git grep -nIF 'C:\Users\' -- . "${EXCLUDE[@]}")

# 4. Personal-identity references. The pattern is supplied from outside the repository
#    and is deliberately NOT written here: a literal in this file would publish the exact
#    string the check exists to find, which is the opposite of the point. CI passes it in
#    from a repository secret; set HEXIDE_IDENTITY_PATTERN (an extended-regex) locally to
#    run it by hand. Unset, the check announces that it did not run rather than passing
#    silently — a guard that quietly does nothing is worse than no guard, because it
#    still reads green.
if [ -n "${HEXIDE_IDENTITY_PATTERN:-}" ]; then
  while IFS= read -r hit; do
    [ -n "$hit" ] && note "personal-identity reference: $hit"
  done < <(git grep -nIE "$HEXIDE_IDENTITY_PATTERN" -- . "${EXCLUDE[@]}")
else
  printf '  \xE2\x97\x8B identity scan SKIPPED (HEXIDE_IDENTITY_PATTERN not set)\n'
fi

# 5. Dangling relative links in Markdown. Resolve each link target for real, rather than
#    assuming a prefix is broken — most of them resolve, and the few that do not are
#    exactly what this is for.
while IFS= read -r md; do
  dir="$(dirname "$md")"
  grep -oE '\]\([^)]+\)' "$md" 2>/dev/null | sed 's/^](//;s/)$//' | while IFS= read -r target; do
    case "$target" in http://*|https://*|\#*|mailto:*|"") continue;; esac
    target="${target%%#*}"
    [ -z "$target" ] && continue
    if [ ! -e "$dir/$target" ] && [ ! -e "$target" ]; then
      printf '%s->%s\n' "$md" "$target"
    fi
  done
done < <(git ls-files '*.md') > /tmp/hexide-linkcheck.$$ 2>/dev/null
while IFS= read -r hit; do
  [ -n "$hit" ] && note "dangling relative link: $hit"
done < /tmp/hexide-linkcheck.$$
rm -f /tmp/hexide-linkcheck.$$

# 6. Third-party naming. The rule: this repository never asserts anything about another
#    project's PLANS — no "X will be the backend", no "X's lane". Describing the
#    replaceable-backend seam generically is the default, because filling that blank in
#    is theirs to agree to, not ours to assume. A record MAY name a project where the
#    decision was genuinely about that project and has already happened. Three classes
#    are allowed, a file at a time:
#      * attribution — the Battleship demo derives from an MIT project and credits its
#        author; the LSP test README records why GPL-derived test inputs were not carried
#        over. Removing either would be a licence and honesty problem.
#      * licence provenance — vb6-grammar-fixes records which GPLv3 grammar is quarantined
#        and why; lsp-parity-matrix is the record of replacing the server built on it.
#        Naming the grammar is what makes the clean-room claim checkable.
#      * the routed wall — the interpreter README names twinBASIC to route users *to* it.
#        That is a deferral, not a comparison, and it needs the name to be actionable.
#    Anything else is a mention nobody decided on, so this fails closed and asks for a
#    decision. Adding a file here is a decision; a new mention inside a listed file still
#    deserves a read.
ALLOWED='^(IDE/HexIDE\.Runtime\.Tests/BattleshipChallenge\.cs|IDE/HexIDE\.Runtime/Interpreter/README\.md|LspServer/HexIDE\.VbLspServer\.Tests/README\.md|demo/battleship/README\.md|docs/vb6-grammar-fixes\.md|docs/lsp-parity-matrix\.md)$'
while IFS= read -r f; do
  [ -n "$f" ] && note "third-party project named outside the agreed places: $f"
done < <(git grep -lIiE 'twinbasic|rdcore|rubberduck' -- . "${EXCLUDE[@]}" | grep -vE "$ALLOWED")

# 7. Upstream attribution — expected, never a failure. Flagged only so a stale code
#    reference cannot hide among the legitimate licence mentions.
while IFS= read -r f; do
  [ -n "$f" ] && warned "AvaloniaVisualBasic mentioned (fine as upstream attribution; confirm it is not a stale code reference): $f"
done < <(git grep -lI 'AvaloniaVisualBasic' -- . "${EXCLUDE[@]}")

echo
if [ "$fail" -eq 0 ]; then
  echo "check-tree-hygiene: OK${warn:+ — $warn warning(s), none blocking}"
else
  echo "check-tree-hygiene: FAILED — fix the items above."
fi
exit "$fail"
