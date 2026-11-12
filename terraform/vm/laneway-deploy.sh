#!/bin/bash
set -euo pipefail

state=/var/lib/laneway
images=$state/images.env
previous=$state/previous.env
api_repo=ghcr.io/samwelomwenga/laneway-api
result=

say() {
  printf '%s\n' "$1"
  printf '%s\n' "$1" | systemd-cat -t laneway-deploy
}

finish() {
  local code=$1
  result=$2
  say "result=$result"
  exit "$code"
}

trap '[ -n "$result" ] || say "result=failed"' EXIT

abort() {
  say "error=$1"
  exit 1
}

output() {
  systemd-cat -t laneway-deploy-output "$@"
}

compose() {
  output docker compose --file /etc/laneway/compose.yaml --env-file "$images" "$@"
}

[ $# -eq 4 ] || abort "usage expected=<api> <postgres> <cloudflared> <oci-cli>"
for digest in "$@"; do
  [[ $digest =~ ^sha256:[0-9a-f]{64}$ ]] || abort invalid-digest
done

new=(
  "API_DIGEST=$1"
  "POSTGRES_DIGEST=$2"
  "CLOUDFLARED_DIGEST=$3"
  "OCI_CLI_DIGEST=$4"
)
own='^(API|POSTGRES|CLOUDFLARED|OCI_CLI)_DIGEST='

exec 9>/run/laneway-deploy.lock
flock --wait 300 9 || abort locked

say "step=secrets"
rendered=$(/usr/local/sbin/laneway-secrets "$4") || finish 11 secrets-failed
say "$rendered"

say "step=alloy"
systemctl restart alloy.service || say "warning=alloy-restart-failed"

say "step=pull"
for image in "$api_repo@$1" "postgres@$2" "cloudflare/cloudflared@$3" "ghcr.io/oracle/oci-cli@$4"; do
  output docker pull --quiet "$image" || abort pull-failed
done

old=()
if [ -f "$images" ]; then
  mapfile -t old < <(grep -E "$own" "$images" | sort)
  say "step=postgres"
  compose up --detach --wait --wait-timeout 300 postgres || abort postgres-not-healthy
else
  say "step=postgres first-deploy"
  env "${new[@]}" systemd-cat -t laneway-deploy-output \
    docker compose --file /etc/laneway/compose.yaml up --detach --wait --wait-timeout 300 postgres ||
    abort postgres-not-healthy
fi

say "step=predeploy-dump"
OCI_CLI_DIGEST=$4 /usr/local/sbin/laneway-backup predeploy || abort predeploy-dump-failed

say "step=migrate"
if ! output docker run --rm --network laneway --env-file "$state/secrets/migrator.env" \
  --entrypoint /app/efbundle "$api_repo@$1"; then
  finish 10 migration-failed
fi

write_images() {
  local temp
  temp=$(mktemp "$state/.images.env.XXXXXX") || return 1
  if ! {
    if [ -f "$images" ]; then grep -Ev "$own" "$images" || true; fi
    printf '%s\n' "$@"
  } >"$temp" || ! chmod 0644 "$temp" || ! mv -f "$temp" "$images"; then
    rm -f "$temp"
    return 1
  fi
}

swap() {
  write_images "$@" || return 1
  compose up --detach postgres cloudflared api || return 1
  local deadline=$((SECONDS + 60))
  while [ "$SECONDS" -lt "$deadline" ]; do
    if curl --fail --silent --output /dev/null --max-time 2 http://127.0.0.1:8080/health/ready; then
      return 0
    fi
    sleep 2
  done
  return 1
}

say "step=swap"
if swap "${new[@]}"; then
  mapfile -t current < <(printf '%s\n' "${new[@]}" | sort)
  if [ ${#old[@]} -gt 0 ] && [ "${old[*]}" != "${current[*]}" ]; then
    printf '%s\n' "${old[@]}" >"$previous"
  fi

  keep=("$1")
  if [ -f "$previous" ]; then
    keep+=("$(sed -n 's/^API_DIGEST=//p' "$previous")")
  fi
  docker image ls --digests --format '{{.Digest}} {{.ID}}' "$api_repo" |
    while read -r digest id; do
      [[ " ${keep[*]} " == *" $digest "* ]] || output docker image rm "$id" || true
    done || say "warning=image-cleanup-failed"

  finish 0 deployed
fi

say "step=health-failed"
if [ ${#old[@]} -eq 0 ]; then
  finish 30 rollback-failed
fi

say "step=rollback"
if swap "${old[@]}"; then
  finish 20 rolled-back
fi
finish 30 rollback-failed
