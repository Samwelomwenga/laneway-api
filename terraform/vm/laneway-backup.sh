#!/bin/bash
set -euo pipefail
umask 077

case ${1:-} in
  nightly)
    prefix=daily
    caller_holds_deploy_lock=false
    ;;
  predeploy)
    prefix=predeploy
    caller_holds_deploy_lock=true
    ;;
  *)
    echo "usage: laneway-backup nightly|predeploy" >&2
    exit 2
    ;;
esac

state=/var/lib/laneway
result=
work=

say() {
  printf '%s\n' "$1"
  printf '%s\n' "$1" | systemd-cat -t laneway-backup
}

trap 'rm -rf "$work"; [ -n "$result" ] || say "result=failed"' EXIT

abort() {
  say "error=$1"
  exit 1
}

output() {
  systemd-cat -t laneway-backup-output "$@"
}

if [ "$caller_holds_deploy_lock" = false ]; then
  exec 9>/run/laneway-deploy.lock
  flock --wait 1800 9 || abort locked
fi

. /etc/laneway/vm.env
if [ -z "${R2_ACCESS_KEY_ID:-}" ]; then
  [ -f "$state/secrets/backup.env" ] || abort no-backup-env
  set -a
  . "$state/secrets/backup.env"
  set +a
fi
for key in R2_ACCESS_KEY_ID R2_SECRET_ACCESS_KEY R2_ENDPOINT R2_BUCKET BACKUP_AGE_RECIPIENT; do
  [ -n "${!key:-}" ] || abort "missing=$key"
done
if [ -z "${OCI_CLI_DIGEST:-}" ] && [ -f "$state/images.env" ]; then
  OCI_CLI_DIGEST=$(sed -n 's/^OCI_CLI_DIGEST=//p' "$state/images.env")
fi
[[ ${OCI_CLI_DIGEST:-} =~ ^sha256:[0-9a-f]{64}$ ]] || abort no-oci-cli-digest

now=$(date -u +%Y%m%dT%H%M%SZ)
day_of_month=${now:6:2}
name=laneway-$now.dump.age
objects=("$prefix/$name")
if [ "$prefix" = daily ] && [ "$day_of_month" = 01 ]; then
  objects+=("monthly/$name")
fi

work=$(mktemp -d "$state/.backup.XXXXXX")
plain=$work/dump
install -d -m 0755 "$work/sealed"
sealed=$work/sealed/$name

docker exec laneway-postgres-1 pg_dump --format=custom --username=postgres --dbname=laneway \
  >"$plain" 2> >(systemd-cat -t laneway-backup-output) || abort dump-failed
docker exec --interactive laneway-postgres-1 pg_restore --list <"$plain" >/dev/null \
  2> >(systemd-cat -t laneway-backup-output) || abort dump-unreadable
output age --encrypt --recipient "$BACKUP_AGE_RECIPIENT" --output "$sealed" "$plain" || abort encrypt-failed
rm -f "$plain"
chmod 0644 "$sealed"

export RCLONE_CONFIG_R2_TYPE=s3
export RCLONE_CONFIG_R2_PROVIDER=Cloudflare
export RCLONE_CONFIG_R2_REGION=auto
export RCLONE_CONFIG_R2_NO_CHECK_BUCKET=true
export RCLONE_CONFIG_R2_ENDPOINT=$R2_ENDPOINT
export RCLONE_CONFIG_R2_ACCESS_KEY_ID=$R2_ACCESS_KEY_ID
export RCLONE_CONFIG_R2_SECRET_ACCESS_KEY=$R2_SECRET_ACCESS_KEY

failed=0
for object in "${objects[@]}"; do
  # --no-overwrite skips the HEAD the CLI otherwise sends, which needs OBJECT_READ.
  # Multipart uploads need OBJECT_OVERWRITE. The VM has only OBJECT_CREATE and OBJECT_INSPECT.
  output docker run --rm --network host --volume "$work/sealed:/backup:ro" \
    "ghcr.io/oracle/oci-cli@$OCI_CLI_DIGEST" \
    --auth instance_principal --region "$REGION" \
    os object put --namespace "$OBJECT_STORAGE_NAMESPACE" --bucket-name "$BACKUP_BUCKET" \
    --name "$object" --file "/backup/$name" --no-multipart --no-overwrite ||
    { say "error=oci-upload-failed object=$object"; failed=1; }
  output rclone --config /dev/null copyto "$sealed" "r2:$R2_BUCKET/$object" ||
    { say "error=r2-upload-failed object=$object"; failed=1; }
done
[ "$failed" -eq 0 ] || exit 1

result=ok
say "result=ok object=$prefix/$name"
