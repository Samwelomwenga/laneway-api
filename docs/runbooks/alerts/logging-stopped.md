# Logging stopped

Tier: page. The threshold is a starting guess. Retune it once real traffic exists.

## What fired

Production sent no `dotnet.process.cpu.count` samples for 10 minutes. The app exports its .NET runtime metrics every 60 seconds even at zero traffic, through the same exporter as its logs and traces. So this fires when the app is down, the exporter broke, or Grafana refuses the token. The rule stays paused until `production_hosted` is true in the Terraform, since nothing runs in production before hosting exists.

## The first query to run

In `grafanacloud-lanewayapi-prom`:

```promql
dotnet_process_cpu_count{deployment_environment_name="production"}
```

Then check whether logs stopped at the same moment:

```logql
sum(count_over_time({service_name="laneway-api", deployment_environment_name="production"} [5m]))
```

## Telling noise from attack

Noise looks like a deploy or a host restart that ran longer than 10 minutes. The data stops and comes back.

An attack looks like logs going quiet while the app still answers requests. Someone who could change the app's config or its token could hide what they do next. Grafana can't tell the two apart, so check the app from outside.

## What to do

1. Send a request to the API. If it doesn't answer, the app is down. Start it and read its console output.
2. If it answers, the export is failing. Run the token check from [the Grafana runbook](../grafana.md#check-the-data-arrives). A 401 means the token was revoked or rotated.
3. Check the `OTEL_` values in Doppler's `prd` config haven't changed.
4. Once data flows again, the gap stays a gap. Write down the time range with no logs.

## When to escalate

Escalate to an incident when the app answered requests during the gap and nobody changed the token or config. Treat the gap as possibly hidden activity.
