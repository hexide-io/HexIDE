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
# Fails (exit 1) on: a private key git would currently let you commit, build artefacts and
# editor backups in the tree, dangling relative links in Markdown, machine-specific absolute
# paths, personal-identity references, a third-party project named outside the places we
# agreed it may be, and unresolved merge-conflict markers. Warns (exit 0) on TWO things, both
# expected and neither blocking: key material .gitignore already covers, and the
# upstream-attribution mention.
#
# EVERY SCAN SEES UNTRACKED FILES, and that is load-bearing rather than tidy. Git's own
# listings see only TRACKED files, so a brand-new file is invisible until it is staged --
# which means running this before `git add`, exactly as the instructions say to, returns a
# false green. That has already let a forbidden third-party name reach CI twice.
#
# THREE mechanisms, and the third is the one worth knowing about. The content scans get it
# from `git grep --untracked`. The build-artefact and Markdown scans get it from `tree_files`
# below, because `git ls-files` needs asking twice; both of those honour .gitignore, so build
# output and artifacts/ stay out of scope. The private-key FILENAME scan uses `key_candidates`
# instead, which deliberately does NOT honour .gitignore -- the note above it says why.
#
# Both listings are NUL-delimited, which is not decoration. `git ls-files` C-quotes any path
# holding a byte above 0x7F: an accented name comes back as a QUOTED, backslash-escaped string,
# so the trailing quote defeats any extension regex anchored on `$` and the file is never seen.
# All three scans converted here were blind to one until `-z` went in end to end. This comment
# stays deliberately ASCII, because a scanner's own note about non-ASCII names is the last place
# an encoding accident should be able to hide.
#
# THREE SCANS USED PLAIN `git ls-files` AND THE PARAGRAPH ABOVE CLAIMED OTHERWISE -- the
# private-key FILENAME scan, the build-artefact scan and the dangling-link scan (#437). CI
# never noticed, and could not: everything is tracked by the time a runner checks out. The
# run that was lying is the local pre-push one, which is the only one that happens before
# bytes leave the machine, and they leave it towards a public repository. A DER or PKCS#12
# key carries no `PRIVATE KEY` text, so the filename scan is the only thing that would ever
# have seen it.
#
# Run from anywhere; it operates on the working tree, tracked and untracked alike.
set -uo pipefail
cd "$(dirname "$0")/.." || exit 2

# This script and its sibling necessarily CONTAIN the very strings they search for — a
# literal "PRIVATE KEY", a Windows user path, the third-party names. Excluding both from
# the *content* scans is what stops them flagging themselves. They stay inside the
# *filename* scans below, so a stray key or binary dropped in scripts/ is still caught.
EXCLUDE=( ':!scripts/check-tree-hygiene.sh' ':!scripts/check-licences.sh' )
#
# `check-tree-hygiene.selftest.sh` is deliberately ABSENT from that list, though it plants every
# kind of material scanned for below. It assembles each forbidden literal at run time
# (`printf 'twin%s' BASIC` and friends) precisely so it need not be excluded: excusing it would
# exempt the one file most likely to grow a real path or a real key by accident.

# Every file in the working tree, tracked or not, minus anything .gitignore excludes. This is
# what `git grep --untracked` already does for the content scans; `git ls-files` needs both
# --cached and --others to answer the same question, and defaults to the first alone.
tree_files() { git ls-files --cached --others --exclude-standard -z -- "$@"; }

# Everything tree_files sees, PLUS files .gitignore excludes, minus the trees where build output
# and downloaded dependencies live. Only the key scan uses it: for every other check an ignored
# file is genuinely out of scope. The exclusions belong to THIS scan rather than being inherited
# from anywhere -- dropping --exclude-standard un-hides every build output and the whole of
# artifacts/, which on a built tree is thousands of files to walk for somewhere a key has no
# reason to be. The bare `bin/**` and `obj/**` are not redundant with the `**/` pair: a pathspec
# `**/bin/**` requires a segment in front, so a repository-ROOT bin/ would otherwise be walked.
#
# One exclusion is not written here and is worth knowing: `git ls-files --others` does not descend
# into a nested git repository, so everything under `.claude/worktrees/*` is invisible to this scan.
# Each of those is its own checkout and runs its own guard, so that is tolerable rather than chosen.
key_candidates() {
  git ls-files --cached --others -z -- . \
    ':!:**/bin/**' ':!:**/obj/**' ':!:bin/**' ':!:obj/**' ':!:artifacts/**' ':!:**/node_modules/**'
}

# COUNTED, not a flag, and the count is printed. A scan whose `note` runs in a subshell -- one
# `cmd | while read` where a `< <(cmd)` was -- still prints its line and then loses the increment, so the
# guard reports the problem and exits 0. Nothing in the output distinguishes that from a scan with nothing
# to say, which is why the total goes in the summary: it is the one number a selftest can pin, and it is
# how a lost note, a deleted scan and a genuinely clean tree stop looking alike.
fails=0
warn=0
note() { printf '  \xE2\x9C\x97 %s\n' "$1"; fails=$((fails + 1)); }
warned() { printf '  \xE2\x9A\xA0 %s\n' "$1"; warn=$((warn + 1)); }

echo "check-tree-hygiene: scanning the working tree…"

# 1. Private key material — by extension, and by PEM block content.
# TWO listings here, because .gitignore is half the problem. `*.key` (.gitignore:417) and
# `*.pfx` (:242) are ignored, so a real key is invisible to ANY listing that honours it --
# including `git grep --untracked`, which means the PEM content scan below is blind to one
# too. `.pem` and `.p12` are not ignored, so the same check sees those and misses the two
# extensions it most needs. Scanning ignored files as well is the only way to close that.
#
# But it cannot simply fail on them: the add-in packer gates on a real
# IDE/HexIDE.AddinPacker/firstparty/firstparty.key being present (HexIDE.Desktop.csproj:113,117),
# so a maintainer holding the dev key would fail this on every run and learn to ignore it --
# which is how a guard stops being read. An ignored key WARNS and a committable one FAILS.
# The split is exactly "is git currently the only thing standing between this and a push".
while IFS= read -r -d '' f; do
  [ -z "$f" ] && continue
  if git check-ignore -q -- "$f" 2>/dev/null; then
    warned "private key material on disk, gitignored so not committable as things stand: $f"
  else
    note "private key file: $f"
  fi
done < <(key_candidates | grep -zEi '\.(key|pem|pfx|p12)$')

while IFS= read -r hit; do
  [ -n "$hit" ] && note "PEM private-key block: $hit"
done < <(git grep --untracked -lI 'PRIVATE KEY' -- . "${EXCLUDE[@]}")

# 2. Build artefacts and editor backups anywhere in the tree. A binary in a public repository
#    is a thing people are right to distrust: they cannot diff it and cannot tell what is in it.
#    The message says "in the tree" rather than "committed" because since #437 most hits are
#    NOT committed -- catching one before `git add` is the whole point -- and a guard that
#    misdescribes what it found is the defect this change exists to fix.
while IFS= read -r -d '' f; do
  [ -n "$f" ] && note "build artefact / backup in the tree: $f"
done < <(tree_files | grep -zEi '\.(exe|dll|pdb|bak)$')

# 3. Machine-specific absolute paths. A path from the author's own disk is both useless
#    to a reader and a small identity leak.
while IFS= read -r hit; do
  [ -n "$hit" ] && note "machine-specific absolute path: $hit"
done < <(git grep --untracked -nIF 'C:\Users\' -- . "${EXCLUDE[@]}")

# 4. Personal-identity references. The pattern is supplied from outside the repository
#    and is deliberately NOT written here: a literal in this file would publish the exact
#    string the check exists to find, which is the opposite of the point. CI passes it in
#    from a repository secret; set HEXIDE_IDENTITY_PATTERN (an extended-regex) locally to
#    run it by hand. Unset, the check announces that it did not run rather than passing
#    silently — a guard that quietly does nothing is worse than no guard, because it
#    still reads green.
#
#    Matched CASE-INSENSITIVELY, so write the pattern in plain lower case. Encoding case
#    by hand as [Mm][Aa]... is easy to get subtly wrong, and getting it wrong fails open:
#    an all-caps copyright header or a generated file would slip past a pattern that only
#    anticipated title case. Keep the pattern to the distinctive parts — a surname and an
#    employer, not a first name, which collides with unrelated third-party authors in
#    THIRD-PARTY-NOTICES.md and trains everyone to ignore the check.
#    Armed and finding nothing, it says so too. Otherwise the one case this check exists
#    for — it ran, and the tree is clean — prints nothing at all, which is byte-identical
#    to the check having been deleted. The SKIPPED line was added so a scan that did not
#    run could not read as a pass; without this line the reader has to notice an ABSENT
#    line to tell the two apart, which is the same trap one level down.
if [ -n "${HEXIDE_IDENTITY_PATTERN:-}" ]; then
  identity_hits=0
  while IFS= read -r hit; do
    if [ -n "$hit" ]; then
      note "personal-identity reference: $hit"
      identity_hits=$((identity_hits + 1))
    fi
  done < <(git grep --untracked -nIiE "$HEXIDE_IDENTITY_PATTERN" -- . "${EXCLUDE[@]}")
  if [ "$identity_hits" -eq 0 ]; then
    printf '  \xE2\x9C\x93 identity scan clean\n'
  fi
else
  printf '  \xE2\x97\x8B identity scan SKIPPED (HEXIDE_IDENTITY_PATTERN not set)\n'
fi

# 5. Dangling relative links in Markdown. Resolve each link target for real, rather than
#    assuming a prefix is broken — most of them resolve, and the few that do not are
#    exactly what this is for.
while IFS= read -r -d '' md; do
  dir="$(dirname "$md")"
  grep -oE '\]\([^)]+\)' "$md" 2>/dev/null | sed 's/^](//;s/)$//' | while IFS= read -r target; do
    case "$target" in http://*|https://*|\#*|mailto:*|"") continue;; esac
    target="${target%%#*}"
    [ -z "$target" ] && continue
    if [ ! -e "$dir/$target" ] && [ ! -e "$target" ]; then
      printf '%s->%s\n' "$md" "$target"
    fi
  done
done < <(tree_files '*.md') > /tmp/hexide-linkcheck.$$ 2>/dev/null
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
#      * an interop fixture — the spring-tide demo attaches RDCore's language server as a foreign
#        server and says how. It records a connection that has been made and measured, at the
#        request of that project's maintainer, and cannot be followed without the name. It must
#        not describe that project's plans; see the rule above.
#    Anything else is a mention nobody decided on, so this fails closed and asks for a
#    decision. Adding a file here is a decision; a new mention inside a listed file still
#    deserves a read.
ALLOWED='^(IDE/HexIDE\.Runtime\.Tests/BattleshipChallenge\.cs|IDE/HexIDE\.Runtime/Interpreter/README\.md|LspServer/HexIDE\.VbLspServer\.Tests/README\.md|demo/battleship/README\.md|demo/spring-tide/README\.md|docs/vb6-grammar-fixes\.md|docs/lsp-parity-matrix\.md)$'
while IFS= read -r f; do
  [ -n "$f" ] && note "third-party project named outside the agreed places: $f"
done < <(git grep --untracked -lIiE 'twinbasic|rdcore|rubberduck' -- . "${EXCLUDE[@]}" | grep -vE "$ALLOWED")

# 7. Upstream attribution — expected, never a failure. Flagged only so a stale code
#    reference cannot hide among the legitimate licence mentions.
while IFS= read -r f; do
  [ -n "$f" ] && warned "AvaloniaVisualBasic mentioned (fine as upstream attribution; confirm it is not a stale code reference): $f"
done < <(git grep --untracked -lI 'AvaloniaVisualBasic' -- . "${EXCLUDE[@]}")

# 8. Unresolved merge-conflict markers. One reached main inside a Markdown file on 2026-09-22: a merge of
#    main into a branch reported two conflicts, one was resolved, and the squash carried the other in (#614).
#    CI and this script both passed it, because nothing here looked. The pattern is the opening and closing
#    markers WITH their trailing space. It never matches a bare `=======`, which is also a Markdown setext
#    heading underline and would fail every document that uses one.
while IFS= read -r hit; do
  [ -n "$hit" ] && note "unresolved merge-conflict marker: $hit"
done < <(git grep --untracked -nIE '^(<<<<<<<|>>>>>>>) ' -- . "${EXCLUDE[@]}")

echo
if [ "$fails" -eq 0 ]; then
  echo "check-tree-hygiene: OK${warn:+ — $warn warning(s), none blocking}"
  exit 0
fi
echo "check-tree-hygiene: FAILED — $fails item(s) above."
exit 1
