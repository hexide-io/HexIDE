#!/usr/bin/env bash
# Enable with: git config core.hooksPath scripts/hooks
set -euo pipefail
if ! command -v gitleaks >/dev/null 2>&1; then
  echo 'Secrets NOT scanned: install gitleaks (https://github.com/gitleaks/gitleaks#installing) and retry the commit.' >&2
  exit 1
fi
root=$(git rev-parse --show-toplevel)
exec gitleaks git --config "$root/.gitleaks.toml" --redact --no-banner \
  --ignore-gitleaks-allow --pre-commit --staged "$root"
