# Backups

The VM dumps the `laneway` database every night at 03:00 UTC and before every migration. The script checks each dump, encrypts it with `age`, and uploads it twice: once to the OCI bucket `laneway-backups` and once to the R2 bucket. This page covers where the dumps go, how to read the job's logs, the monthly restore on the laptop, and password rotation.

## What runs

`/usr/local/sbin/laneway-backup` does the work. cloud-init writes it from `terraform/vm/laneway-backup.sh`, so a change to the script replaces the VM.

1. `docker exec laneway-postgres-1 pg_dump --format=custom` writes the dump to a root-only temp directory under `/var/lib/laneway`.
2. `pg_restore --list` reads it back. A dump it can't read fails the run.
3. `age` encrypts it to `BACKUP_AGE_RECIPIENT` from `/etc/laneway/vm.env`, and the script deletes the plain dump. The VM holds only the public key, so it can't decrypt its own backups.
4. The OCI CLI container uploads to OCI with the instance principal. The VM may create and list objects, never read, overwrite, or delete them.
5. rclone uploads the same file to R2 with the `R2_*` keys in `/var/lib/laneway/secrets/backup.env`.

| Mode | Started by | Prefix |
|---|---|---|
| `nightly` | `laneway-backup.timer`, at 03:00 UTC | `daily/`, plus `monthly/` on the 1st |
| `predeploy` | `laneway-deploy`, before the migration | `predeploy/` |

Names look like `daily/laneway-20261112T030000Z.dump.age`, in UTC. Both buckets delete `daily/` and `predeploy/` after 30 days and `monthly/` after 12 months.

The timer skips the run until the first deploy writes `/var/lib/laneway/images.env`. A nightly run and a deploy take the same lock, so a migration never waits on a nightly dump's table locks. `Persistent=true` runs a missed night's dump after the VM boots.

## Logs

Each run ends with one line under the `laneway-backup` tag, `result=ok object=<name>` or `result=failed`. Grafana's two backup rules read that tag. An `error=` line before `result=failed` names the step:

| `error=` | What failed |
|---|---|
| `locked` | a deploy held `/run/laneway-deploy.lock` for 30 minutes |
| `no-backup-env` | `/var/lib/laneway/secrets/backup.env` doesn't exist yet |
| `missing=<key>` | that key is empty in `backup.env` or `vm.env` |
| `no-oci-cli-digest` | `OCI_CLI_DIGEST` was neither in the environment nor in `images.env` |
| `dump-failed` | `pg_dump`, usually because `laneway-postgres-1` isn't running |
| `dump-unreadable` | `pg_restore --list` on the new dump |
| `encrypt-failed` | `age`, usually a bad recipient in `vm.env` |
| `oci-upload-failed object=<name>` | the OCI upload of that object |
| `r2-upload-failed object=<name>` | the R2 upload of that object |

An upload failure doesn't stop the other upload, so one bucket can hold a dump the other lacks. Tool output goes under `laneway-backup-output`. Neither tag ever holds the dump, a connection string, or a key.

Over Tailscale SSH:

```sh
journalctl -t laneway-backup -t laneway-backup-output --since today
systemctl list-timers laneway-backup.timer
sudo systemctl start laneway-backup.service
```

The last line takes a nightly dump now. On the 1st it also writes a second `monthly/` copy.

## The monthly restore

Restore the newest `daily/` dump on the laptop once a month. It proves the backups decrypt and load, and it times a real restore. Never run it in Actions. It needs the `age` private key.

You need Docker, `age`, `rclone`, and the Doppler CLI logged in with access to `laneway-db` and `laneway-backup`.

1. Fetch the newest dump from R2:

   ```sh
   mkdir -p ~/laneway-restore && cd ~/laneway-restore
   doppler run --project laneway-db --config prd -- sh -c '
     export RCLONE_CONFIG_R2_TYPE=s3 RCLONE_CONFIG_R2_PROVIDER=Cloudflare RCLONE_CONFIG_R2_REGION=auto
     export RCLONE_CONFIG_R2_ENDPOINT="$R2_ENDPOINT"
     export RCLONE_CONFIG_R2_ACCESS_KEY_ID="$R2_ACCESS_KEY_ID"
     export RCLONE_CONFIG_R2_SECRET_ACCESS_KEY="$R2_SECRET_ACCESS_KEY"
     latest=$(rclone --config /dev/null lsf "r2:$R2_BUCKET/daily/" | sort | tail -n 1)
     rclone --config /dev/null copyto "r2:$R2_BUCKET/daily/$latest" "./$latest"
     echo "$latest"'
   ```

   Write down the name it prints. The dump's age is the time between its timestamp and now.

2. Decrypt it. The key goes from Doppler to `age` through a pipe and never touches disk:

   ```sh
   age --decrypt --identity <(doppler secrets get AGE_SECRET_KEY --project laneway-backup --config prd --plain) \
     --output laneway.dump laneway-*.dump.age
   ```

3. Start a throwaway Postgres and time the restore. `--host 127.0.0.1` waits past the image's first-start server, which listens on the socket only:

   ```sh
   docker run --detach --name laneway-restore --env POSTGRES_PASSWORD=restore --env POSTGRES_DB=laneway postgres:18
   until docker exec laneway-restore pg_isready --quiet --host 127.0.0.1 --username postgres; do sleep 1; done
   time docker exec --interactive laneway-restore pg_restore --username=postgres --dbname=laneway \
     --no-owner --no-privileges --exit-on-error <laneway.dump
   ```

   `--no-owner --no-privileges` skips the `laneway_migrator` and `laneway_app` grants, since those roles don't exist here.

4. Check what came back:

   ```sh
   docker exec laneway-restore psql --username=postgres --dbname=laneway \
     --command='SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1' \
     --command='SELECT (SELECT count(*) FROM "Users") AS users, (SELECT count(*) FROM "Boards") AS boards, (SELECT count(*) FROM "Cards") AS cards, (SELECT count(*) FROM "DataProtectionKeys") AS keys'
   ```

   The migration should be the newest one in `Migrations/` at the release that's live. The counts should be above zero and at or above last month's, unless users deleted data.

5. Clean up. The plain dump holds every user's data:

   ```sh
   docker rm --force laneway-restore
   rm ~/laneway-restore/laneway.dump ~/laneway-restore/laneway-*.dump.age
   ```

6. Add a row to the log below.

### Restore log

| Date | Dump | Dump age | Restore time | Newest migration | Notes |
|---|---|---|---|---|---|

## Rotating a database password

The app's connection string in `laneway-api` `prd` reads `${laneway-db.prd.APP_PASSWORD}`, so each password lives in `laneway-db` `prd` alone. Doppler resolves the reference when `laneway-secrets` downloads the config.

Postgres reads `POSTGRES_PASSWORD` only when it creates an empty data directory. After that, the role's password changes only through `ALTER ROLE`, so a rotation changes Postgres first and Doppler second.

1. Generate a password with no single quote and no newline. `laneway-secrets` refuses either.
2. Tailscale SSH to the VM and set it on the role. `\password` prompts for it and sends only a hash, so it never lands in shell history or the Postgres log:

   ```sh
   sudo docker exec --interactive --tty laneway-postgres-1 psql --username=postgres --dbname=laneway --command='\password laneway_app'
   ```

   Use `laneway_migrator` for `MIGRATOR_PASSWORD` and `postgres` for `POSTGRES_PASSWORD`.
3. In the Doppler dashboard, set the same value on `APP_PASSWORD`, `MIGRATOR_PASSWORD`, or `POSTGRES_PASSWORD` in `laneway-db` `prd`. If `MIGRATOR_CONNECTION` holds the password as text instead of `${MIGRATOR_PASSWORD}`, change it too.
4. Redeploy the live digests so `laneway-secrets` renders the new env files and Compose recreates the containers whose env changed:

   ```sh
   sudo laneway-deploy $(. /var/lib/laneway/images.env && echo "$API_DIGEST $POSTGRES_DIGEST $CLOUDFLARED_DIGEST $OCI_CLI_DIGEST")
   ```

   Between steps 2 and 4, the API's new connections to Postgres fail. Its open pooled connections keep working, so do the three steps back to back. `postgres.env` holds all three passwords, so any rotation also restarts `laneway-postgres-1` during the deploy, before the predeploy dump. The API drops its connections for those few seconds.
5. Check `result=deployed`, then `curl --fail http://127.0.0.1:8080/health/ready` on the VM.
