# Laneway API

## CI

CI runs no tests, so run `dotnet test` before you push. The suite needs Docker for Testcontainers and the local Supabase stack from `supabase start`.

[docs/ci.md](docs/ci.md) lists each check and how to fix it.

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

Service name, environment, and host come only from `OTEL_SERVICE_NAME` and `OTEL_RESOURCE_ATTRIBUTES`. The service version comes from the assembly's informational version, unless `OTEL_RESOURCE_ATTRIBUTES` sets `service.version`. Log levels live under `Serilog:MinimumLevel` in `appsettings.json`. Serilog ignores `Logging:LogLevel`. [ADR 0011](docs/adr/0011-serilog-in-front-of-the-otel-sdk.md) explains the pipeline.

Production ships to Grafana Cloud. [docs/runbooks/grafana.md](docs/runbooks/grafana.md) covers the stack, the tokens, the readers, and purging a leaked item. [docs/runbooks/alerting.md](docs/runbooks/alerting.md) covers the alert rules, their playbooks, and applying the Terraform in `terraform/alerting`.

## Email

The app won't start without `BREVO__APIKEY`, `BREVO__SENDEREMAIL`, and `BREVO__WEBHOOKSIGNINGKEY` in Doppler. Local runs send real mail through the shared dev Brevo account. Tests swap Brevo for a stub and never reach it.

## Signing keys

The app won't start without at least one access token signing key. Doppler holds them as an ordered list, `AUTH__SIGNINGKEYS__{n}__ID` and `AUTH__SIGNINGKEYS__{n}__KEY`. Key 0 signs, and every listed key validates. The id is a year and month like `2026-10`. The key is 32 random bytes in base64:

```sh
openssl rand -base64 32
```

To rotate a key without signing anyone out, add the new key at position 1 and deploy. Then swap it to position 0 and deploy. Remove the old key 16 minutes later and deploy again.
