#!/usr/bin/env bash
# Hands ci-skippable.sh known answers and fails if it gets one wrong.
#
# Runs BEFORE the classifier is used, for the reason the hygiene guard's self-test runs before the
# hygiene guard: a check whose failure mode is a false green cannot be trusted on the strength of
# having exited zero. Each `build` row below names why that path is not prose — delete a row only
# by first deleting the thing that makes it true.
set -uo pipefail
cd "$(dirname "$0")/.."
S=scripts/ci-skippable.sh
fail=0
check() {
  if bash "$S" "$1" >/dev/null 2>&1; then got=skip; else got=build; fi
  if [ "$got" != "$2" ]; then
    printf '  WRONG  %-40s got=%-5s want=%-5s  %s\n' "$1" "$got" "$2" "$3"; fail=1
  else
    printf '  ok     %-40s %-5s  %s\n' "$1" "$got" "$3"
  fi
}

echo "Prose — safe to skip the build:"
check 'docs/issue-labels.md'                 skip  'a document'
check 'docs/archive/labels-decided.md'       skip  'a nested document'
check 'docs/img/diagram.png'                 skip  'a document image'
check 'CLAUDE.md'                            skip  'root markdown'
check 'CONTRIBUTING.md'                      skip  'root markdown'
check '.github/ISSUE_TEMPLATE/bug.yml'       skip  'an issue form'
check 'screenshots/designer.png'             skip  'a screenshot'
check 'LICENSE'                              skip  'the licence'

echo
echo "Markdown that is really a build input:"
check 'openspec/specs/ide-shell/spec.md'     build 'DesignRecordTests reads openspec/specs'
check 'openspec/changes/x/tasks.md'          build 'DesignRecordTests reads openspec/changes'
check 'openspec/AGENTS.md'                   build 'all of openspec/ is build-visible'
check 'docs/command-line.md'                 build 'CommandLineDocumentationTests reads it'
check 'docs/lsp-client.md'                   build 'ProtocolCoverageDocTests reads it'
check 'docs/lsp-server-features.md'          build 'listed in HexIDE.slnx'
check 'docs/ROADMAP.md'                      build 'listed in HexIDE.slnx'

echo
echo "Code, config, and the pattern-ordering traps:"
check 'IDE/HexIDE/Foo.cs'                    build 'source'
check 'IDE/HexIDE/Notes.md'                  build 'markdown beside source — * spans / in a case'
check 'LspServer/a/b/c.md'                   build 'deeply nested markdown beside source'
check 'docs/tool.py'                         build 'docs/ is only prose for known extensions'
check '.github/workflows/build.yml'          build 'the workflow itself'
check 'global.json'                          build 'build configuration'
check 'Directory.Packages.props'             build 'build configuration'
check 'HexIDE.slnx'                          build 'the solution'
check 'corpus/conformance/a.bas'             build 'the conformance corpus'
check 'scripts/check-licences.sh'            build 'a guard script'
check '.gitignore'                           build 'unrecognised root file defaults to building'

echo
if [ "$fail" -ne 0 ]; then
  echo "::error::ci-skippable.sh gave a wrong answer. A wrong 'skip' reports Success to a required check without building."
  exit 1
fi
echo "ci-skippable.sh agrees on all rows."
