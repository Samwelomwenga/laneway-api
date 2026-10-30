# Migrations

How a schema change reaches production, and the rules a migration follows so a deploy can't break the release that is still serving.

## Roll forward only

Production never runs a `Down`. A deploy applies the pending migrations, then starts the new app. If a migration fails, EF Core 10 has already committed the ones before it, since each migration runs in its own transaction. The deploy stops there and the old app keeps serving. The fix ships as a new release with a new migration.

`Down` methods stay in the generated code. They are for undoing a migration on your own machine before it merges.

## Every migration works with the previous release

The new schema goes live while the previous release is still running, and it stays live if the new app fails to start. So the previous release must keep working against it.

- A new column on an existing table is nullable or has a default. A `NOT NULL` column with no default breaks every insert from the previous release, since that code doesn't set it.
- Dropping a column or table takes two releases. The first stops using it in code. The second drops it.
- A rename takes two releases too. The first adds the new name and writes to both. The second drops the old one. A rename in one migration is a drop the previous release can't survive.
- Changing a column's type follows the same path as a rename.

No tool checks this. Review does. When you review a PR with a migration, read its `Up` against the code on `main`.

## Database roles

Two roles connect to production.

- `laneway_migrator` owns the schema. Only the migration bundle uses it.
- `laneway_app` is the role the app runs as. It gets `SELECT`, `INSERT`, `UPDATE` and `DELETE` on tables and `USAGE` and `SELECT` on sequences, through `ALTER DEFAULT PRIVILEGES FOR ROLE laneway_migrator`. So every table a migration creates is usable by the app with no extra grant.

Each role's connection string is its own key in Doppler project `laneway-api`, config `prd`.

## The bundle

Every image holds a framework-dependent EF migration bundle at `/app/efbundle`. It comes from the `migrations` stage of the `Dockerfile` and runs the same migrations as the app in that image. A deploy runs it from the image it is about to start:

```sh
docker run --rm \
  -e ConnectionStrings__DefaultConnection="$APP_CONNECTION" \
  --entrypoint /app/efbundle \
  ghcr.io/<owner>/laneway-api@sha256:<digest> \
  --connection "$MIGRATOR_CONNECTION"
```

The bundle builds the app's host to find `ApplicationDbContext`, so `Program.cs` runs its startup checks. `ConnectionStrings__DefaultConnection` holds the app role's string so the missing-connection-string guard passes. `--connection` holds the migrator role's string, and that is the one the bundle migrates with.

Leave `OTEL_EXPORTER_OTLP_ENDPOINT` unset. The bundle builds the host but never runs it, so exporters may never flush. It logs to stdout, and its exit code is the signal. Zero means every migration applied.

The bundle prints `libgssapi_krb5.so.2: cannot open shared object file` once. Npgsql looks for Kerberos, the chiseled image doesn't ship it, and Npgsql carries on with the password. On a first run it also logs a failed query on `__EFMigrationsHistory`, because the table doesn't exist yet. Neither changes the exit code.

## Reading the SQL

Releases carry no SQL script. To read what a migration will run, generate it locally. `dotnet ef` needs a connection string to build the host, so run it through Doppler:

```sh
doppler run -- dotnet ef migrations script <FromMigration> <ToMigration> --project Laneway.Api.csproj
```
