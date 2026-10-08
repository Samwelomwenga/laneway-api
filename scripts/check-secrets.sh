#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

fail=0

mapfile -t tracked < <(git ls-files | grep -iE '(^|/)(secrets\.json|\.env)($|\.)' | grep -vE '(^|/)\.env\.example$' || true)
if [[ ${#tracked[@]} -gt 0 ]]; then
  echo "Secret files are tracked by git. Remove them with git rm --cached and rotate what they held:"
  printf '  %s\n' "${tracked[@]}"
  fail=1
fi

while IFS= read -r file; do
  if grep -qE '"ConnectionStrings"' "$file"; then
    echo "$file has a ConnectionStrings section. Connection strings live in Doppler, not appsettings."
    fail=1
  fi
  if grep -qiE 'Password=' "$file"; then
    echo "$file has a Password= value. Passwords live in Doppler, not appsettings, placeholders included."
    fail=1
  fi
done < <(git ls-files ':(glob)**/appsettings*.json')

if [[ "$fail" -ne 0 ]]; then
  echo "check-secrets failed."
  exit 1
fi
echo "check-secrets passed."
