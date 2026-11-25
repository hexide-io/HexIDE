#!/usr/bin/env bash
# Self-test for check-release-unpublished.sh, with a stand-in gh that answers from a fixture (#5424).
set -euo pipefail
script="$(cd "$(dirname "$0")" && pwd)/check-release-unpublished.sh"
bin="$(mktemp -d)"
trap 'rm -rf "$bin"' EXIT

# The stand-in prints FAKE_RELEASES as gh api --jq would, or fails when FAKE_GH_FAILS is set.
cat > "$bin/gh" <<'GH'
#!/usr/bin/env bash
[ -n "${FAKE_GH_FAILS:-}" ] && { echo "HTTP 502" >&2; exit 1; }
printf '%b' "${FAKE_RELEASES:-}"
GH
chmod +x "$bin/gh"
export PATH="$bin:$PATH" GITHUB_REPOSITORY=hexide-io/HexIDE GITHUB_SHA=0000000

allows() {
    if ! FAKE_RELEASES="$2" bash "$script" "$1" > /dev/null 2>&1; then
        echo "refused $1 with releases: $2" >&2
        exit 1
    fi
}
refuses() {
    if FAKE_RELEASES="$2" FAKE_GH_FAILS="${3:-}" bash "$script" "$1" > /dev/null 2>&1; then
        echo "allowed $1 with releases: $2 ${3:+(gh failing)}" >&2
        exit 1
    fi
}

allows v0.8.0 'v0.7.0\tfalse\n'                        # no release for the tag yet
allows v0.8.0 'v0.8.0\ttrue\nv0.7.0\tfalse\n'          # a draft can be re-run
allows v0.8.0 ''                                       # a repository with no releases
refuses v0.7.0 'v0.8.0\ttrue\nv0.7.0\tfalse\n'         # published
refuses v0.7.0 'v0.7.0\ttrue\nv0.7.0\tfalse\n'         # published, beside a stray draft of the same tag
allows v0.7.1 'v0.7.10\tfalse\nv0.7.1x\tfalse\n'       # only the exact tag counts
refuses v0.8.0 '' failing                              # can't read the releases: refuse
refuses v0.7.0 'v0.7.0\tfalse\r\n'                     # a carriage return doesn't hide a published release
refuses v0.7.0 'v0.7.0\tnull\n'                        # a draft field that isn't exactly true counts as published
refuses v0.7.0 'v0.7.0\n'                              # so does a row with no draft field
allows v0.8.0 'v0.8.0\ttrue\r\n'                       # a draft with a carriage return is still a draft
echo "check-release-unpublished self-test: OK"
