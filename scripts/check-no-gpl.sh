#!/usr/bin/env bash
# check-no-gpl.sh — guards HexIDE's "100% MIT" guarantee. Runs in CI on every push.
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
EXCLUDE=( ':!scripts/check-no-gpl.sh' ':!scripts/prepare-public-copy.sh' )

fail=0
note() { printf '  \xE2\x9C\x97 %s\n' "$1"; fail=1; }

echo "check-no-gpl: scanning for GPL-licensed artifacts…"

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

if [ "$fail" -eq 0 ]; then
  echo "check-no-gpl: OK — no GPL-licensed artifacts in the tree."
else
  echo "check-no-gpl: FAILED — remove the artifacts above; the tree must stay 100% MIT."
fi
exit "$fail"
