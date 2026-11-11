# Production Postgres runs in a container on the VM

Laneway's production database is Postgres 18 in a Docker container on the same Oracle Cloud VM as the app, not a managed service. The budget allows only free tiers, and this is the only free option that runs all month. We back it up ourselves: a nightly and a pre-migration `pg_dump`, encrypted with `age` on the VM and copied to OCI Object Storage and Cloudflare R2.

## Considered options

- Neon free. Its compute suspends after 5 idle minutes, but the delivery and copy job workers poll every 5 seconds, so it never idles. That uses about 182 of the 100 free CU-hours a month, and Neon stops the compute around day 16.
- Supabase free, next to the attachments project. It stays awake and fits in 500 MB. But free projects get no backups, it runs Postgres 17 while CI runs 18, the direct connection is IPv6 only, and new `public` tables are exposed through its Data API unless that is turned off.

## Consequences

- Backups, restore tests, upgrades, and Postgres logs are ours. Nothing outside the VM watches the database except the 26-hour dead man's switch on the backup job.
- The VM and the database share 2 cores and 12 GB. A heavy query slows the API.
- There is no point-in-time recovery. A restore loses up to a day of writes, or everything since the last deploy's pre-migration dump. Point-in-time recovery with pgBackRest is the next step once real users exist.
- Moving to a managed service later is a dump, a restore, and a connection string change, plus downtime.
