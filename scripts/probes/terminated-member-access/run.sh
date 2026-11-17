#!/usr/bin/env bash
# Run from the repository root. Reports stay in the clone; corpus workers stream hits before they are killed.
set -euo pipefail
export PATH="$(printf '%s' "$PATH" | tr ':' '\n' | grep -v '^/mnt/' | paste -sd:)"
export UseSharedCompilation=false
project=${1:?test project directory required}
shift
report=${HEXIDE_TERMINATED_ACCESS_REPORT:?set an absolute report directory inside the clone}
probe="$project/TerminatedAccessProbe4375.cs"
if [[ -e "$probe" ]]; then echo "Probe already exists: $probe" >&2; exit 1; fi
listing="$project/ProviderTestSort.md"
backup="$listing.4375-backup"
if [[ -e "$backup" ]]; then echo "Listing backup already exists: $backup" >&2; exit 1; fi
cleanup() {
  rm -f "$probe"
  if [[ -e "$backup" ]]; then
    cp "$backup" "$listing"
    rm -f "$backup"
  fi
}
trap cleanup EXIT
# The source inventory must include the temporary provider-bound file while it is built (#3663).
# This label was produced by ProviderTestSortTests; restore the original inventory after the run.
if [[ -f "$listing" ]]; then
  cp "$listing" "$backup"
  printf '| `TerminatedAccessProbe4375.cs` | provider |\n' >> "$listing"
fi
cp scripts/probes/terminated-member-access/Probe.cs "$probe"
export HEXIDE_TERMINATED_ACCESS_REPORT="$report"
# A stale build might contain no hook at all and would silently report zero. Always compile the copied hook.
args=()
for argument in "$@"; do
  if [[ "$argument" == --no-build ]]; then
    echo "The temporary measurement hook requires a build; ignoring --no-build."
  else
    args+=("$argument")
  fi
done
status=0
dotnet test "$project" "${args[@]}" || status=$?
python3 - "$report" <<'PY'
import collections, json, pathlib, sys
rows = [json.loads(line) for p in pathlib.Path(sys.argv[1]).glob('*.jsonl') for line in p.read_text().splitlines()]
hits = [r for r in rows if r['kind'] == 'hit']
print('#4375 total terminated member accesses:', len(hits))
for site, count in collections.Counter((r['Module'], r['Member'], r['Line'], r['test'], r['project']) for r in hits).items():
    print(count, site)
print('Processes started:', sum(r['kind'] == 'start' for r in rows), 'ended:', sum(r['kind'] == 'end' for r in rows))
print('Deliberately forced diagnostic hits excluded:', sum(r['kind'] == 'forced' for r in rows))
PY

exit "$status"
