# Grafana Cloud

Production ships logs, traces, and metrics over OTLP to one Grafana Cloud stack on the free tier, in an EU region. [ADR 0012](../adr/0012-grafana-cloud-for-telemetry-without-sentry.md) records why. The [logging inventory](../security/logging-inventory.md#backend) lists the rules this page puts in place. Local runs ship to the Aspire Dashboard instead, as the README says.

Everything on this page lives in Grafana Cloud and Doppler, not in the repo. If you change a setting by hand, change it here too.

## Create the stack

1. Sign up at grafana.com on the free plan, and turn on two-factor auth in your Grafana.com profile before anything else.
2. Create one stack named `laneway`. Pick an EU region. A stack can't change region later, so moving means a new stack and losing history.
3. In the stack, open Application Observability and turn it on. It builds the service views and the Exceptions tab from traces.

The free tier keeps logs and traces 14 days and holds 3 users. Past a monthly cap, Grafana drops data for the rest of the month.

## The app's token

The app's token can only write. It can't read or delete anything.

1. In the Grafana Cloud portal, open Access Policies and create a policy named `laneway-api-otlp`, scoped to the `laneway` stack.
2. Give it `logs:write`, `traces:write`, and `metrics:write`. Nothing else, not even read.
3. Add a token to it named `laneway-api-prd`. Copy it now, since Grafana shows it once.
4. On the stack's OpenTelemetry (OTLP) page, copy the OTLP endpoint and the instance id. The endpoint looks like `https://otlp-gateway-prod-eu-west-2.grafana.net/otlp`.
5. Build the auth value from the instance id and the token:

   ```sh
   printf '%s' '<instance id>:<token>' | base64 -w0
   ```

6. In Doppler's `laneway-api` project, config `prd`, set:

   ```sh
   OTEL_EXPORTER_OTLP_ENDPOINT=<endpoint from step 4>
   OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
   OTEL_EXPORTER_OTLP_HEADERS=Authorization=Basic%20<value from step 5>
   OTEL_SERVICE_NAME=laneway-api
   OTEL_RESOURCE_ATTRIBUTES=deployment.environment.name=production
   ```

Never put these in appsettings, and never in the `ci` config, which syncs every secret to GitHub. The exporter URL-decodes the header, so `%20` becomes the space Grafana expects.

Don't use the token the OTLP page offers to generate. Its policy may carry more scopes than these three.

No standing token holds `logs:delete`. The [purge](#purge-a-leaked-item) section makes one when it's needed and revokes it after.

## Readers

Each reader is a named person with their own Grafana.com login. There is no shared login.

1. Invite them to the org from the portal's Members page with the Viewer role. The operator is the only Admin.
2. Before you invite them, make sure they've turned on two-factor auth in their Grafana.com profile, or sign in through a Google or GitHub account that has it. Remove anyone who turns it off.
3. List each reader here, with the date they got access.

The free tier allows 3 users, the operator included. The free tier doesn't record who ran which query, and [ADR 0004](../adr/0004-auth-targets-asvs-l2-with-exceptions.md#1642-logs-protected-from-access-and-edits-l2) lists that as an exception.

Readers:

- The operator, Admin, from the day the stack was made.

## Check the data arrives

Hosting isn't decided, so the first check runs from a laptop. It sends real data under `deployment.environment.name=production`, so it shows up next to real production data.

1. Start a local Postgres you can stop without losing anything, and point `CONNECTIONSTRINGS__DEFAULTCONNECTION` at it.
2. Pull the five `OTEL_` values from `prd` into your shell, and run the API with the `dev` secrets around them:

   ```sh
   for v in OTEL_EXPORTER_OTLP_ENDPOINT OTEL_EXPORTER_OTLP_PROTOCOL OTEL_EXPORTER_OTLP_HEADERS OTEL_SERVICE_NAME OTEL_RESOURCE_ATTRIBUTES; do
     export "$v=$(doppler secrets get "$v" --project laneway-api --config prd --plain)"
   done
   doppler run --config dev -- dotnet run --no-launch-profile
   ```

   `dev` holds no `OTEL_` values, so the exported ones pass through.

3. Send a request that touches the database, such as `GET /api/v1/auth/username-available?username=grafana-check`.
4. In Explore, pick the Loki data source and run `{service_name="laneway-api"} | SourceContext="Laneway.Request"`. Open the line and copy its trace id.
5. Paste the trace id into the Tempo data source. The trace shows the server span and its database span.
6. Send a sign-in with a wrong password. In the Prometheus data source, query `laneway_auth_sign_in_attempts_total`. Grafana turns the dots in `laneway.auth.sign_in.attempts` into underscores and adds `_total`.
7. Force a 500. Stop the database with the API still running, and send the request from step 3 again. The body quotes a `traceId`.
8. Find that trace id in Loki with `{service_name="laneway-api"} | trace_id="<traceId>"`. The `UnexpectedError` line is there. Find it in Tempo as the failed trace.
9. Open Application Observability, then the `laneway-api` service, then Traces, then Exceptions, and note whether the 500 is listed.

The tab may stay empty. Our spans never call `RecordException`, so only Npgsql's own `exception` event on a failed database command reaches Tempo. A stopped database may fail at connection open, before any command span starts. If the tab is empty, the `UnexpectedError` line in Loki is the record of a 500. Write down what you saw, since the ticket expects the tab to show it.

A 401 or 403 in the API's stderr means the header is wrong. Check the base64 holds `<instance id>:<token>` with no newline.

## Purge a leaked item

Do this only when something on the inventory's never-logged list reached Grafana, such as a password or an email address. It deletes log lines. Grafana Cloud offers no delete for traces or metrics, so a leak there stays until retention removes it, 14 days on free.

1. Fix the code that logged it first, and ship the fix. Otherwise the item comes back.
2. Find the lines in Explore with a LogQL query narrow enough to match only them, and note the time range.
3. In Access Policies, create a policy named `laneway-purge-<date>` with Delete on logs and nothing else. Add a token that expires within a day.
4. Read the Loki host and user id from the stack's Loki data source settings.
5. Send the delete:

   ```sh
   curl -G -X POST 'https://<loki host>/loki/api/v1/delete' \
     --data-urlencode 'query=<the LogQL query from step 2>' \
     --data-urlencode 'start=<unix seconds>' \
     --data-urlencode 'end=<unix seconds>' \
     -u '<loki user id>:<purge token>'
   ```

6. List requests with `curl 'https://<loki host>/loki/api/v1/delete' -u '<loki user id>:<purge token>'`. You can cancel a wrong one with `DELETE` and `?request_id=<id>` until it runs.
7. Check the lines no longer show in Explore. Grafana hides them at once and removes them in a batch within 24 hours.
8. Delete the purge policy and its token. No delete token outlives the purge.
9. Write down what leaked, the time range, and the date, in the incident notes.

Deletion is permanent. It doesn't lower usage, since Grafana already counted the bytes.
