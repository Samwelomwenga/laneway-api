# No backup in 26 hours

Tier: notify. The threshold is a starting guess. Retune it once real nights have run.

## What fired

Loki has no `result=ok` line from `laneway-backup` in the last 26 hours. The nightly dump runs at 03:00 UTC, so one missed night plus 2 hours of slack fires it. This is the dead man's switch. Failed runs fire it, and so do a timer that never ran, a VM that's down, and an Alloy that stopped shipping logs. A failed run fires [backup failed](backup-failed.md) first. This one catches the runs that never wrote a line at all. systemd fails the nightly unit before the script starts when `/var/lib/laneway/secrets/backup.env` is missing, so that case shows up here and nowhere else. The rule runs hourly and stays paused until `production_hosted` is true in the Terraform.

## The first query to run

In `grafanacloud-lanewayapi-logs`, over the last 2 days:

```logql
{service_name="laneway-backup", deployment_environment_name="production"}
```

No lines at all means the job never started, logs stopped reaching Loki, or the VM is down. The [logging stopped](logging-stopped.md) alert firing too points at the VM, since the app ships its metrics without Alloy. `result=failed` lines mean the backup ran and failed, so follow the backup failed playbook.

## Telling noise from attack

Noise looks like the VM rebooting across 03:00 UTC. `Persistent=true` runs the missed dump after boot, so the alert clears on the next hourly check. Alloy losing its Loki token is noise too, but it stays until someone fixes the token. Logging stopped won't fire for it, since the app ships its own telemetry. Right after the flip, the rule can fire before the first nightly run if no predeploy dump reached Loki in the last 26 hours.

An attack looks like the timer disabled, or Alloy stopped, and nobody on the rota did it. Someone who wants a change to stay unrecoverable stops the backups first. Check the [logging stopped](logging-stopped.md) alert too, since an attacker who stops one may stop both.

## What to do

1. Over Tailscale SSH, run `systemctl list-timers laneway-backup.timer`. The last run and the next one must both show.
2. Run `journalctl -t laneway-backup --since "2 days ago"`. Lines here but not in Loki mean Alloy isn't shipping. Check `systemctl status alloy`.
3. No lines here either means the job never started. `systemctl status laneway-backup.service` shows why. `/var/lib/laneway/images.env` must exist, since the service skips the run until the first deploy writes it, and so must `/var/lib/laneway/secrets/backup.env`.
4. Take a dump now with `sudo systemctl start laneway-backup.service` and check for `result=ok` in Loki.
5. Find the newest `daily/` dump in R2 with the `lsf` line from step 1 of [the monthly restore](../backup.md#the-monthly-restore). Its name holds its UTC time, so you know how old the newest good dump is.

## When to escalate

Escalate to an incident when someone stopped the timer or Alloy and nobody on the rota did it. Escalate too when the newest dump in R2 is over 48 hours old and the next run still fails.
