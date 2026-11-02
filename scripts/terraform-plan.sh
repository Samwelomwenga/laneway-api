#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

usage="usage: terraform-plan.sh <root> [--drift]"
root=${1:?$usage}
case ${2:-} in
  "") drift=false ;;
  --drift) drift=true ;;
  *)
    echo "$usage" >&2
    exit 1
    ;;
esac
dir="terraform/$root"

for name in "${!TF_VAR_@}"; do
  suffix=${name#TF_VAR_}
  export "TF_VAR_${suffix,,}=${!name}"
done

plan_args=(-input=false -lock-timeout=5m -out=tfplan)
if $drift; then
  plan_args+=(-detailed-exitcode)
fi

terraform -chdir="$dir" init -input=false -lockfile=readonly
status=0
terraform -chdir="$dir" plan "${plan_args[@]}" > /dev/null || status=$?
if [[ $status == 1 ]]; then
  exit 1
fi

changes=$(terraform -chdir="$dir" show -json tfplan |
  jq -r '
    (.resource_changes[]? | select(.change.actions != ["no-op"]) | "\(.change.actions | join("+")) \(.address)"),
    (.output_changes // {} | to_entries[] | select(.value.actions != ["no-op"]) | "\(.value.actions | join("+")) output.\(.key)")
  ' |
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
    if $drift; then
      echo "### Terraform drift check for $root"
    else
      echo "### Terraform plan for $root"
    fi
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

if $drift && [[ $status == 2 ]]; then
  echo "::error::$root has drifted from main, or main holds changes no apply has made yet"
  exit 1
fi
