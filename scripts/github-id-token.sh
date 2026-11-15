#!/usr/bin/env bash
set -euo pipefail

usage="usage: github-id-token.sh <audience> <file>"
audience=${1:?$usage}
file=${2:?$usage}

: "${ACTIONS_ID_TOKEN_REQUEST_URL:?the job needs permissions: id-token: write}"

umask 077
curl -fsS \
  -H "Authorization: bearer $ACTIONS_ID_TOKEN_REQUEST_TOKEN" \
  "$ACTIONS_ID_TOKEN_REQUEST_URL&audience=$(jq -rn --arg a "$audience" '$a | @uri')" |
  jq -er .value > "$file"
