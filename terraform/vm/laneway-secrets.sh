#!/bin/bash
set -euo pipefail
umask 077

fail() {
  echo "laneway-secrets: $1" >&2
  exit 1
}

oci_cli_digest=${1:-}
if [[ ! $oci_cli_digest =~ ^sha256:[0-9a-f]{64}$ ]]; then
  echo "usage: laneway-secrets <oci-cli digest>" >&2
  exit 2
fi

. /etc/laneway/vm.env
dir=/var/lib/laneway/secrets
install -d -m 0700 -o root -g root "$dir"

vault_secret() {
  docker run --rm --network host "ghcr.io/oracle/oci-cli@$oci_cli_digest" \
    --auth instance_principal --region "$REGION" \
    secrets secret-bundle get --secret-id "$1" \
    --query 'data."secret-bundle-content".content' --raw-output \
    2> >(systemd-cat -t laneway-secrets) | base64 -d
}

doppler_config() {
  DOPPLER_TOKEN=$1 doppler secrets download --no-file --format json --no-fallback --no-check-version \
    2> >(systemd-cat -t laneway-secrets)
}

db_token=$(vault_secret "$LANEWAY_DB_DOPPLER_TOKEN_SECRET_ID") || fail "could not read the laneway-db token from Vault"
api_token=$(vault_secret "$LANEWAY_API_DOPPLER_TOKEN_SECRET_ID") || fail "could not read the laneway-api token from Vault"
db=$(doppler_config "$db_token") || fail "could not download laneway-db prd"
api=$(doppler_config "$api_token") || fail "could not download laneway-api prd"
unset db_token api_token

# Compose expands $ in unquoted env_file values but takes single-quoted values literally,
# so no value may hold a single quote.
compose_format='to_entries[]
  | if (.key | test("^[A-Za-z_][A-Za-z0-9_]*$") | not) or (.value | test("[\n\r'"'"']")) then error else . end
  | "\(.key)='"'"'\(.value)'"'"'"'

# docker run --env-file takes each value raw, quotes included.
raw_format='to_entries[]
  | if .value | test("[\n\r]") then error else . end
  | "\(.key)=\(.value)"'

declare -A rendered=()
trap 'rm -f "${rendered[@]}"' EXIT

render() {
  local name=$1 config=$2 select=$3 format=$4 temp
  temp=$(mktemp "$dir/.$name.XXXXXX")
  rendered[$name]=$temp
  jq --raw-output "$select | $format" <<<"$config" >"$temp" 2>/dev/null || fail "could not render $name"
}

render postgres.env "$db" '{POSTGRES_PASSWORD, MIGRATOR_PASSWORD, APP_PASSWORD} | if any(.[]; . == null) then error else . end' "$compose_format"
render backup.env "$db" 'with_entries(select(.key | startswith("R2_")))' "$compose_format"
render migrator.env "$db" '{CONNECTIONSTRINGS__DEFAULTCONNECTION: (.MIGRATOR_CONNECTION // error)}' "$raw_format"
render api.env "$api" 'with_entries(select(.key != "TUNNEL_TOKEN" and (.key | test("^(ALLOY|DOPPLER)_") | not)))' "$compose_format"
render cloudflared.env "$api" '{TUNNEL_TOKEN: (.TUNNEL_TOKEN // error)}' "$compose_format"
render alloy.env "$api" 'with_entries(select(.key | startswith("ALLOY_")))' "$compose_format"

for name in "${!rendered[@]}"; do
  mv -f "${rendered[$name]}" "$dir/$name"
done

echo "secrets=rendered"
