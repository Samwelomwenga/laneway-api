# Local development

## Prerequisites

- The .NET SDK that `global.json` pins
- Docker, for Testcontainers and the Supabase stack
- The [Supabase CLI](https://supabase.com/docs/guides/local-development/cli/getting-started)
- The [Doppler CLI](https://docs.doppler.com/docs/install-cli), with access to the `laneway-api` project

## Run the API

Doppler's `dev` config holds every variable the app reads at startup:

- `CONNECTIONSTRINGS__DEFAULTCONNECTION`
- `SUPABASE__URL` and `SUPABASE__SERVICEROLEKEY`
- `BREVO__APIKEY`, `BREVO__SENDEREMAIL`, and `BREVO__WEBHOOKSIGNINGKEY`
- `AUTH__SIGNINGKEYS__0__ID` and `AUTH__SIGNINGKEYS__0__KEY`

Without Doppler access, export the same names in your shell. [The Brevo runbook](runbooks/brevo.md) covers the email keys, and [the signing keys runbook](runbooks/signing-keys.md) covers the token keys.

```sh
doppler setup --project laneway-api --config dev
supabase start
doppler run -- dotnet ef database update --project Laneway.Api.csproj
doppler run -- dotnet run
```

The API listens on http://localhost:5062. The API reference is at http://localhost:5062/scalar/v1.

## Run the tests

The suite needs Docker for Testcontainers and the local Supabase stack. [docs/ci.md](ci.md#mainyml) covers the user secrets the tests read and the polling variable.

```sh
supabase start
DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test Laneway.Api.sln
```

## Telemetry

The API ships logs, traces, and metrics over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, and ships nothing when it isn't. Only startup failures and Serilog's own errors go to stderr. Locally, it ships to the Aspire Dashboard:

```sh
docker run --rm -d --name aspire-dashboard \
  -p 127.0.0.1:18888:18888 -p 127.0.0.1:4317:18889 \
  -e ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true \
  mcr.microsoft.com/dotnet/aspire-dashboard:13.6
```

Then run the API with these variables, from Doppler's `dev` config or your shell, and open http://localhost:18888:

```sh
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
OTEL_SERVICE_NAME=laneway-api
OTEL_RESOURCE_ATTRIBUTES=deployment.environment.name=local
```

Service name, environment, and host come only from `OTEL_SERVICE_NAME` and `OTEL_RESOURCE_ATTRIBUTES`. The service version comes from the assembly's informational version, unless `OTEL_RESOURCE_ATTRIBUTES` sets `service.version`. Log levels live under `Serilog:MinimumLevel` in `appsettings.json`. Serilog ignores `Logging:LogLevel`. [ADR 0011](adr/0011-serilog-in-front-of-the-otel-sdk.md) explains the pipeline.

Production ships to Grafana Cloud. [docs/runbooks/grafana.md](runbooks/grafana.md) covers the stack, the tokens, the readers, and purging a leaked item. [docs/runbooks/alerting.md](runbooks/alerting.md) covers the alert rules, their playbooks, and applying the Terraform in `terraform/alerting`.
