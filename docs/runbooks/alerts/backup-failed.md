# Backup failed

Tier: notify. The threshold is a starting guess. Retune it once real nights have run.

## What fired

`laneway-backup` on the VM wrote `result=failed`. The script writes that line whenever a run ends without uploading the dump to both buckets. The nightly run starts at 03:00 UTC. The predeploy run starts inside `laneway-deploy` before the migration, and a failed predeploy dump stops that deploy with `error=predeploy-dump-failed` under `laneway-deploy`. The rule stays paused until `production_hosted` is true in the Terraform.

## The first query to run

In `grafanacloud-lanewayapi-logs`, over the last 6 hours:

```logql
{service_name="laneway-backup", deployment_environment_name="production"}
```

The `error=` line before `result=failed` names the step. [The backup runbook](../backup.md#logs) lists every value. Tool output under `laneway-backup-output` stays on the VM, so read it over Tailscale SSH:

```sh
journalctl -t laneway-backup -t laneway-backup-output --since today
```

## Telling noise from attack

Noise looks like `error=locked` during a long deploy, or one `r2-upload-failed` that the next run clears. Cloudflare and OCI both drop uploads now and then.

An attack looks like an upload refused for permission when nobody touched the policy or the token. Someone may have changed the OCI policy, revoked the R2 token, or replaced the bucket. `encrypt-failed` after nobody changed `vm.env` means the age recipient may have changed. Check `BACKUP_AGE_RECIPIENT` against `TF_VAR_BACKUP_AGE_RECIPIENT` in Doppler `laneway-vm`.

## What to do

1. Read the `error=` line and match it against the table in [the backup runbook](../backup.md#logs).
2. Fix the cause. `dump-failed` usually means `laneway-postgres-1` isn't running, so check `docker ps` first.
3. Take a dump now with `sudo systemctl start laneway-backup.service`, then check the log for `result=ok`.
4. If the failure stopped a deploy, run the deploy again once a dump succeeds.
5. An upload failure leaves one bucket holding a dump the other lacks. That's fine once a later run uploads to both.

## When to escalate

Escalate to an incident when uploads fail on permission and nobody changed the policy or the token, or when the recipient changed in Doppler without a planned apply. Escalate too when no run has succeeded for a day, since the [no backup in 26 hours](no-backup-in-26-hours.md) alert fires next and the newest good dump keeps getting older.
