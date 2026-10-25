#!/usr/bin/env bash
# Folds every changelog.d/ fragment into CHANGELOG.md's [Unreleased] section, then deletes the fragments.
#
#   changelog-gather.sh            gather, and delete what was gathered
#   changelog-gather.sh --dry-run  print the CHANGELOG.md that would result, and change nothing
#   changelog-gather.sh --check    validate the fragments only: exit 1 naming a malformed one
#
# Run it before tagging a release: publish.yml links each release's notes to CHANGELOG.md at the tag, so an
# entry still sitting in changelog.d/ there is missing from the notes.
#
# WHY FRAGMENTS (#1375). Every pull request used to add its entry at the top of the same section of
# CHANGELOG.md, and protect-main requires a branch to be up to date before it merges, so each merge put every
# other open pull request in conflict on that one file. One file per change means no two pull requests touch
# the same lines. The fragment rules are checked on every build by ChangelogFragmentTests as well as here.
set -euo pipefail
cd "$(dirname "$0")/.."

SECTIONS="Added Changed Deprecated Removed Fixed Security"
mode="${1:-gather}"
case "$mode" in gather|--dry-run|--check) ;; *) echo "usage: changelog-gather.sh [--dry-run|--check]" >&2; exit 2 ;; esac

shopt -s nullglob
fragments=()
for f in changelog.d/*.md; do [ "$(basename "$f")" = README.md ] || fragments+=("$f"); done

# Newest first, as CHANGELOG.md lists them: by the issue number a name starts with, highest first; names
# without one after those, alphabetically.
mapfile -t fragments < <(for f in "${fragments[@]}"; do
  b=$(basename "$f" .md); n=${b%%-*}
  if [[ "$n" =~ ^[0-9]+$ ]]; then printf '0 %012d %s\n' "$((999999999999 - n))" "$f"; else printf '1 %s %s\n' "$b" "$f"; fi
done | sort | cut -d' ' -f3-)

bad=0
for f in "${fragments[@]}"; do
  if ! [[ "$(basename "$f")" =~ ^([0-9]+(-[a-z0-9-]+)?|[a-z0-9-]+)\.md$ ]]; then
    echo "::error::$f: name it <issue>.md or <issue>-<word>.md, in lower case"; bad=1; continue
  fi
  # A Windows checkout gives every file CRLF, so a trailing CR is ignored throughout.
  first=$(tr -d '\015' < "$f" | grep -m1 -v '^[[:space:]]*$' || true)
  if ! [[ "$first" =~ ^###\ (Added|Changed|Deprecated|Removed|Fixed|Security)$ ]]; then
    echo "::error::$f: the first line must be a section heading: ### Added, Changed, Deprecated, Removed, Fixed or Security"; bad=1
  fi
  while IFS= read -r line; do
    line=${line%$'\015'}
    if [[ "$line" =~ ^#{1,3}\  ]] && ! [[ "$line" =~ ^###\ (Added|Changed|Deprecated|Removed|Fixed|Security)$ ]]; then
      echo "::error::$f: '$line' is not one of the changelog's section headings"; bad=1
    fi
  done < "$f"
  tr -d '\015' < "$f" | grep -q '^- ' || { echo "::error::$f: holds no entry (a line starting '- ')"; bad=1; }
done
[ "$bad" -eq 0 ] || exit 1
if [ "$mode" = --check ]; then echo "changelog.d: ${#fragments[@]} fragment(s), all well formed."; exit 0; fi
if [ "${#fragments[@]}" -eq 0 ]; then echo "changelog.d holds no fragments; nothing to gather."; exit 0; fi

# One block per section: each fragment's lines under that heading, trimmed, with a blank line after each.
tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
for s in $SECTIONS; do : > "$tmp/$s"; done
for f in "${fragments[@]}"; do
  awk -v dir="$tmp" '
    { sub(/\r$/, "") }
    /^### / { flush(); section = substr($0, 5); next }
    { buf[++n] = $0 }
    function flush(   i, a, z) {
      if (section == "") { n = 0; return }
      a = 1; z = n
      while (a <= z && buf[a] ~ /^[[:space:]]*$/) a++
      while (z >= a && buf[z] ~ /^[[:space:]]*$/) z--
      for (i = a; i <= z; i++) print buf[i] >> (dir "/" section)
      if (z >= a) print "" >> (dir "/" section)
      n = 0
    }
    END { flush() }' "$f"
done

# Each block goes at the top of its section under [Unreleased]; a section the release has not used yet is
# added at the end of [Unreleased], in Keep a Changelog's order.
awk -v dir="$tmp" -v sections="$SECTIONS" '
  function block(s,   line, file, any) {
    file = dir "/" s; any = 0
    while ((getline line < file) > 0) { print line; any = 1 }
    close(file); return any
  }
  function rest(   i, n, list) {
    n = split(sections, list, " ")
    for (i = 1; i <= n; i++) if (!(list[i] in done) && has[list[i]]) { print "### " list[i]; print ""; block(list[i]); done[list[i]] = 1 }
  }
  BEGIN {
    n = split(sections, list, " ")
    for (i = 1; i <= n; i++) { f = dir "/" list[i]; has[list[i]] = ((getline line < f) > 0); close(f) }
  }
  $0 == "## [Unreleased]" { inside = 1; print; next }
  inside && /^## / { rest(); inside = 0 }
  inside && /^### / { s = substr($0, 5); print; if (has[s] && !(s in done)) { print ""; block(s); done[s] = 1; skip = 1 }; next }
  skip && /^[[:space:]]*$/ { skip = 0; next }
  { skip = 0; print }
  END { if (inside) rest() }' <(tr -d '\015' < CHANGELOG.md) > "$tmp/CHANGELOG.md"
# Written back in the line endings it was read in.
if [ "$(tr -cd '\015' < CHANGELOG.md | wc -c)" -gt 0 ]; then awk '{ printf "%s\r\n", $0 }' "$tmp/CHANGELOG.md" > "$tmp/crlf" && mv "$tmp/crlf" "$tmp/CHANGELOG.md"; fi

if [ "$mode" = --dry-run ]; then cat "$tmp/CHANGELOG.md"; exit 0; fi
cp "$tmp/CHANGELOG.md" CHANGELOG.md
for f in "${fragments[@]}"; do
  if git ls-files --error-unmatch "$f" >/dev/null 2>&1; then git rm -q "$f"; else rm "$f"; fi
done
echo "Gathered ${#fragments[@]} fragment(s) into CHANGELOG.md and removed them from changelog.d/."
