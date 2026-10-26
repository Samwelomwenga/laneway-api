# Serilog in front of the OpenTelemetry SDK

Laneway logs through Serilog, for its one-line request log and its per-request enrichers. Serilog doesn't export anything itself. `AddSerilog(writeToProviders: true)` hands every event to the OpenTelemetry SDK's logging provider, and one `UseOtlpExporter()` ships logs, traces, and metrics. The OTel SDK sets `EventId.Name` as the OTLP `EventName`, and alerting keys on category plus event name. The research for this effort advised plain `ILogger` with no Serilog. We chose Serilog anyway, for the request log.

## Considered options

- Plain `ILogger` with the OTel provider. Fewer moving parts, and the server span already holds method, route, status, and duration. We wanted the request log as a log entry, not only a span.
- `Serilog.Sinks.OpenTelemetry`. ChalkApp API used it. It is a second OTLP client for logs alone, with its own resource. It ignores the per-signal `OTEL_EXPORTER_OTLP_LOGS_*` variables. Version 4.2.0 sends the event id as an `{Id, Name}` attribute and never sets `EventName`.

## Consequences

- `ClearProviders()` runs first. Otherwise the default console and EventSource providers get every event too, and the EventSource one leaks memory.
- Serilog ignores `Logging:LogLevel` and `builder.Logging.AddFilter`. Levels and overrides live under `Serilog:MinimumLevel`, read through `ReadFrom.Configuration`.
- The OTel provider gets Serilog's properties as flat state. It never sees a real `BeginScope`, structured values arrive as strings, and `EventId` and `SourceContext` come along as extra attributes.
- No console sink runs in any environment. Only the bootstrap logger, before the host builds, and Serilog's `SelfLog` write to stderr.
- Service name, environment, and host come from OTel resource attributes, not Serilog enrichers.
- Call sites use the built-in `[LoggerMessage]` generator. The `Microsoft.Extensions.Telemetry` generator's redaction lives in a logger factory that Serilog replaces, so it would do nothing here.
- Tests run the real Serilog pipeline and read the fake log collector where the OTel provider sits, so they see what ships.
