#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

root=${1:?usage: terraform-plan.sh <root>}
dir="terraform/$root"

terraform -chdir="$dir" init -input=false -lockfile=readonly
terraform -chdir="$dir" plan -input=false -lock-timeout=5m -out=tfplan > /dev/null

changes=$(terraform -chdir="$dir" show -json tfplan |
  jq -r '.resource_changes[]? | select(.change.actions != ["no-op"]) | "\(.change.actions | join("+")) \(.address)"' |
  sort)

if [[ -z "$changes" ]]; then
  echo "No changes in $root."
  digest=none
else
  echo "Changes in $root:"
  echo "$changes"
  digest=$(sha256sum <<< "$changes" | cut -d' ' -f1)
fi

if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
  {
    echo "### Terraform plan for $root"
    echo
    if [[ -z "$changes" ]]; then
      echo "No changes."
    else
      echo '```'
      echo "$changes"
      echo '```'
    fi
  } >> "$GITHUB_STEP_SUMMARY"
fi

if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  echo "digest=$digest" >> "$GITHUB_OUTPUT"
fi
