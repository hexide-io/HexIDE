#!/usr/bin/env bash
# Refuses to go on when the tag's release is already published (#5424).
#
# Dispatching publish.yml again for a published release would replace its downloads, the signed add-ins and the
# installer among them, with a build of whatever commit the new run is on, under the old tag, and nothing would record
# the swap. A draft is fine: it can be re-run as often as needed before it is published. Immutable releases are on for
# this repository (read 2026-11-25), so GitHub refuses changes to a release published from then on, but only at the
# upload, after the whole run has built. This stops the run before anything is built, and also covers the releases
# published before then, which are not immutable (v0.7.0 and earlier, read the same day).
#
# Usage: check-release-unpublished.sh <tag>. Needs gh, GH_TOKEN and GITHUB_REPOSITORY. A token that can read drafts
# (contents: write) is needed to tell a draft from no release, though either lets the run go on. If the releases can't
# be read, it refuses: a refused re-run costs one dispatch, a replaced release can't be taken back.
set -euo pipefail

tag="${1:?usage: check-release-unpublished.sh <tag>}"
repo="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is not set}"

if ! releases=$(gh api --paginate "repos/$repo/releases?per_page=100" --jq '.[] | [.tag_name, (.draft | tostring)] | @tsv'); then
    echo "::error title=Can't read the releases::Couldn't list $repo's releases, so can't tell whether $tag is already published. Nothing was built."
    exit 1
fi

# A release for the tag counts as a draft only when its draft field reads exactly "true". Anything else, a changed output
# shape included, counts as published, so a parse that goes wrong refuses rather than lets a published release through.
state=$(printf '%s\n' "$releases" | tr -d '\r' | awk -F '\t' -v tag="$tag" '
    $1 == tag && $2 == "true" { draft = 1; next }
    $1 == tag                 { published = 1 }
    END { print published ? "published" : draft ? "draft" : "none" }')

case "$state" in
    published)
        echo "::error title=Release already published::$tag is already published. Running again would replace its downloads with a build of ${GITHUB_SHA:-this commit} under the old tag. Release a new version instead. A draft can be re-run; a published release can't." >&2
        exit 1 ;;
    draft)
        echo "$tag has a draft release: this run replaces its assets, which is fine until it is published." ;;
    *)
        echo "$tag has no release yet." ;;
esac
