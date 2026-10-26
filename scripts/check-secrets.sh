#!/usr/bin/env bash
# Scan every commit in the supplied range, including secrets later deleted.
set -euo pipefail
if [ "$#" -ne 2 ]; then
  echo 'Usage: bash scripts/check-secrets.sh <base> <head>' >&2
  exit 2
fi
# gitleaks can exit zero after git rejects a missing revision: verify it ourselves.
if ! git rev-parse --verify --quiet "$1^{commit}" >/dev/null ||
   ! git rev-parse --verify --quiet "$2^{commit}" >/dev/null; then
  echo 'Secrets NOT scanned: base or head commit is unavailable.' >&2
  exit 1
fi
root=$(git rev-parse --show-toplevel)
exec gitleaks git --config "$root/.gitleaks.toml" --redact --no-banner \
  --ignore-gitleaks-allow --log-opts="$1..$2" "$root"
