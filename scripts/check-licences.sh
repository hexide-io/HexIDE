#!/usr/bin/env bash
# check-licences.sh — guards HexIDE's "100% MIT" guarantee. Runs in CI on every push.
#
# Fails (exit 1) if any GPL-*licensed artifact* reappears in the tree: the Rubberduck
# VBA grammar, a GPL-named licence file, a GPL SPDX header, or the embedded GNU GPL
# licence body. Plain-prose mentions of "GPL" (historical notes, and legitimate
# references to external GPL projects) are ALLOWED — this checks for real GPL
# artifacts, not the word.
#
# NOTE: there is deliberately NO path-based "GPL server directory" check. The MIT
# server now legitimately lives at LspServer/HexIDE.VbLspServer/ — it reclaimed that
# name after the GPL server was deleted — so GPL provenance is detected by CONTENT
# and by the Rubberduck grammar filenames, never by directory name.
#
# Run from anywhere; it operates on the git-tracked tree. The guard-mit CI job calls it
# with no arguments from the repository root.
set -uo pipefail
cd "$(dirname "$0")/.." || exit 2

# The guard scripts themselves necessarily contain GPL pattern strings / a GPL-ish name;
# exclude both from every scan so they can't self-flag.
EXCLUDE=( ':!scripts/check-licences.sh' ':!scripts/prepare-public-copy.sh' ':!scripts/package-licences.tsv' )

fail=0
note() { printf '  \xE2\x9C\x97 %s\n' "$1"; fail=1; }

echo "check-licences: scanning for GPL and unknown-licence artifacts…"

# 1. The Rubberduck VBA grammar (the origin of the GPL obligation) must not reappear.
#    (Loops use process substitution so `fail` persists — a piped `while` runs in a subshell.)
while IFS= read -r f; do
  [ -n "$f" ] && note "Rubberduck GPL grammar file present: $f"
done < <(git ls-files -- . "${EXCLUDE[@]}" | grep -Ei '(VBALexer|VBAParser|VBAConditionalCompilationParser)\.g4$')

# 2. No GPL-named licence files (basename contains "gpl": COPYING.GPL, LICENSE-GPLv3, gpl-3.0.txt…).
#    A bare COPYING (a BSD/MIT dep may ship one) is intentionally NOT flagged by name — check 4
#    catches any actual GPL licence file by its embedded text instead.
while IFS= read -r f; do
  [ -n "$f" ] && note "GPL-named licence file present: $f"
done < <(git ls-files -- . "${EXCLUDE[@]}" | grep -Ei 'gpl[^/]*$')

# 3. No GPL SPDX headers in tracked source (matches GPL / AGPL / LGPL variants).
while IFS= read -r hit; do
  [ -n "$hit" ] && note "GPL SPDX header: $hit"
done < <(git grep -nIE 'SPDX-License-Identifier:[[:space:]]*[A-Za-z0-9.+-]*GPL' -- . "${EXCLUDE[@]}")

# 4. No embedded GNU GPL licence body.
while IFS= read -r hit; do
  [ -n "$hit" ] && note "GNU GPL licence text: $hit"
done < <(git grep -nI 'GNU GENERAL PUBLIC LICENSE' -- . "${EXCLUDE[@]}")

# 5. No downloaded foreign language server may become tracked.
#    The foreign-backend tests fetch real third-party servers at run time into a gitignored cache. One of
#    them is GPL-licensed, which is fine to DRIVE — a separate process across a protocol boundary is not a
#    derivative work, and nothing is redistributed — and not fine to SHIP. That distinction rests entirely
#    on the binary never entering the tree, so it is enforced here rather than left to .gitignore, which a
#    `git add -f` or a re-scoped ignore rule would quietly defeat.
while IFS= read -r f; do
  [ -n "$f" ] && note "downloaded language server is tracked (it must never be committed): $f"
done < <(git ls-files -- artifacts/ 'IDE/HexIDE.Tests/[Tt]ools/' | grep -Ei '(rumdl|texlab|clangd)|\.(exe|tar\.gz|zip)$')

# 6. Every centrally-managed package must have a RECORDED, PERMITTED licence.
#
#    "No GPL" was the old shape of this guard and it only ever caught the last problem. An unlicensed
#    dependency -- NOASSERTION, or a licence nobody resolved -- walks straight past a GPL grep while being
#    strictly worse: GPL terms are at least known. The nearest neighbour project is NOASSERTION across its
#    whole repository, so this is a live case rather than a hypothetical one.
PERMITTED='^(MIT|Apache-2\.0|BSD-2-Clause|BSD-3-Clause|ISC|0BSD|Unlicense|MS-PL)$'
MANIFEST=scripts/package-licences.tsv

if [ ! -f "$MANIFEST" ]; then
  note "licence manifest missing: $MANIFEST"
else
  while IFS= read -r pkg; do
    [ -n "$pkg" ] || continue
    lic=$(awk -F'	' -v p="$pkg" '$1==p{print $2}' "$MANIFEST" | head -n1)
    if [ -z "$lic" ]; then
      note "package has no recorded licence: $pkg (add it to $MANIFEST)"
    elif ! printf '%s' "$lic" | grep -Eq "$PERMITTED"; then
      note "package licence is not permitted: $pkg -> $lic"
    fi
  done < <(grep -o 'PackageVersion Include="[^"]*"' Directory.Packages.props | sed 's/.*Include="//;s/"$//')
fi

# 7. Code flows OUT to a licence-ambiguous neighbour, never back in.
#
#    Contributing to a project whose CLA allows relicensing is a decision the maintainer can make freely.
#    Importing from a repository whose licence no tool can resolve is not reversible, and it would break a
#    promise made to everyone downstream. So the direction of travel is one-way, and it is enforced here
#    rather than remembered: anything wanted in both places is authored in THIS tree first (MIT), and a
#    copy is contributed outward.
while IFS= read -r hit; do
  [ -n "$hit" ] && note "reference to a licence-ambiguous origin (code must not travel inward): $hit"
done < <(git grep -nIE '(using|namespace)[[:space:]]+RDCore(\.|;|[[:space:]])' -- . "${EXCLUDE[@]}")

if [ "$fail" -eq 0 ]; then
  echo "check-licences: OK — every dependency has a recorded, permitted licence."
else
  echo "check-licences: FAILED — resolve the findings above; the tree must stay 100% MIT."
fi
exit "$fail"
