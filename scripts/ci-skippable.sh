#!/usr/bin/env bash
# Decides whether ONE changed path is prose that cannot affect a build.
#
#   ci-skippable.sh <path>          exit 0 = skippable, exit 1 = build it
#   ci-skippable.sh --check-tree    fails if the tree reads a doc the classifier would skip
#   ci-skippable.sh --list-build-inputs
#
# Used per changed file by the `changes` job in .github/workflows/build.yml. Both other modes run
# first, in that job, before any answer is trusted. That is not ceremony: this script's failure mode
# is a false green — a wrong `skip` makes two required checks report Success without compiling
# anything, and nothing downstream would notice. A check like that has to be handed known answers
# and watched getting them right.
#
# The list enumerates what is SAFE TO SKIP, never what is code. Anything unrecognised — including a
# whole new top-level directory — builds.
set -euo pipefail
cd "$(dirname "$0")/.."

# Markdown that the build READS. These are code wearing a .md extension:
#   docs/command-line.md         IDE/HexIDE.Tests/Infrastructure/CommandLineDocumentationTests.cs
#   docs/lsp-client.md           IDE/HexIDE.Tests/LspClient/ProtocolCoverageDocTests.cs
#   docs/lsp-server-features.md  listed in HexIDE.slnx
#   docs/ROADMAP.md              listed in HexIDE.slnx
# --check-tree derives this set from the tree rather than trusting the list, because a hand-kept
# list rots silently — into builds that were skipped and should not have been.
DOC_BUILD_INPUTS="docs/ROADMAP.md
docs/command-line.md
docs/lsp-client.md
docs/lsp-server-features.md"

classify() {
  if printf '%s\n' "$DOC_BUILD_INPUTS" | grep -qxF "$1"; then return 1; fi
  case "$1" in
    # Listed explicitly rather than left to the catch-all, because reaching `build` through a
    # catch-all is indistinguishable from an oversight, and the next person to optimise this will
    # read it as one. DesignRecordTests (IDE/HexIDE.Tests/Infrastructure/DesignRecordTests.cs) reads
    # openspec/changes and openspec/specs at runtime: it fails the build when a change has every
    # task ticked and is still sitting in changes/, and when an archived change's requirements are
    # absent from the capability it targeted. An openspec-only pull request really can go red.
    # Moving this into the allowlist below would disable that guard for precisely the pull requests
    # it exists to check, and the build would go green saying nothing. Both other modes pin it.
    openspec/*)                                            return 1 ;;

    docs/*.md|docs/*.png|docs/*.svg|docs/*.jpg|docs/*.gif)  return 0 ;;
    screenshots/*)                                         return 0 ;;
    .github/ISSUE_TEMPLATE/*)                              return 0 ;;
    LICENSE)                                               return 0 ;;
    */*)                                                   return 1 ;;
    *.md)                                                  return 0 ;;
    *)                                                     return 1 ;;
  esac
}

# Every markdown path the build resolves at runtime, derived rather than remembered.
derive_read_docs() {
  # (a) Two or more literals whose FIRST is a directory that exists here. The first literal and the
  #     final filename are enough to ask the question, which is what lets this cover a dynamic
  #     middle: DesignRecordTests builds openspec/specs/<capability>/spec.md. Requiring the
  #     directory to exist is what separates a real read from a temp-tree fixture — several tests
  #     build Path.Combine(_dir, "README.md") to exercise path handling and never open the repo's.
  { grep -rhoE 'Path\.Combine\([^"].*\.md"\)' --include='*.cs' IDE LspServer 2>/dev/null || true; } \
  | awk '{ n=0; s=$0; delete t; while (match(s, /"[^"]*"/)) { t[++n]=substr(s,RSTART+1,RLENGTH-2); s=substr(s,RSTART+RLENGTH) }
           if (n>1) print t[1] "/" t[n] }' \
  | while IFS= read -r c; do if [ -d "${c%%/*}" ]; then printf '%s\n' "$c"; fi; done
  # (b) Exactly one literal, resolved against something that names the repo root. Root-level *.md is
  #     skippable, so a future Path.Combine(RepoRoot(), "README.md") must be caught — and (a) cannot
  #     tell that from a fixture, since both are one literal with no directory.
  { grep -rhoE 'Path\.Combine\([A-Za-z_.]*(RepoRoot|Root)\(\),[[:space:]]*"[^"]+\.md"\)' \
      --include='*.cs' IDE LspServer 2>/dev/null || true; } | sed -E 's/.*"([^"]+\.md)"\)/\1/'
  # (c) Docs the solution resolves.
  grep -oE 'docs/[^"]+\.md' HexIDE.slnx 2>/dev/null || true
}

case "${1:-}" in
  --list-build-inputs) printf '%s\n' "$DOC_BUILD_INPUTS"; exit 0 ;;
  --check-tree)
    bad=0
    while IFS= read -r c; do
      [ -z "$c" ] && continue
      if classify "$c"; then
        echo "::error::$c is read by the build, but the classifier would skip the build for it."
        bad=1
      else
        echo "  ok, builds: $c"
      fi
    done < <(derive_read_docs | sort -u)
    if [ "$bad" -ne 0 ]; then
      echo "::error::Declare it in DOC_BUILD_INPUTS, or stop reading it. Skipping the build for a"
      echo "::error::file the build reads is a green tick on a tree nothing checked."
      exit 1
    fi
    echo "Every markdown file the build reads is classified as a build input."
    exit 0 ;;
  "") echo "usage: ci-skippable.sh <path> | --check-tree | --list-build-inputs" >&2; exit 2 ;;
esac

classify "$1"
