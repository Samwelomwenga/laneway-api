# Grafana Cloud

Production ships logs, traces, and metrics over OTLP to one Grafana Cloud stack on the free tier, in an EU region. [ADR 0012](../adr/0012-grafana-cloud-for-telemetry-without-sentry.md) records why. The [logging inventory](../security/logging-inventory.md#backend) lists the rules this page puts in place. Local runs ship to the Aspire Dashboard instead, as the README says.

Alert rules and their routing live in the repo as Terraform. [The alerting runbook](alerting.md) covers them. Everything else on this page lives in Grafana Cloud and Doppler, not in the repo. If you change a setting by hand, change it here too.

## Create the stack

The stack is `lanewayapi`, at `lanewayapi.grafana.net`, in `prod-eu-west-2`.

1. Sign up at grafana.com with an email and a password, not Google or GitHub. MFA needs a password, and an org that enforces MFA exempts social logins from it.
2. Turn on MFA: in the left sidebar under User Settings, click MFA, scan the code, and save the recovery codes in a password manager.
3. In the Cloud Portal, open Org Settings, tick Enforce MFA, and click Update.
4. Signup makes a stack with a random name in a region you didn't pick, and the free tier allows one stack. Check its region: on the stack's OpenTelemetry card, click Configure, then View connection details. The endpoint host names the region. Anything without `eu` in it, `gb` included, is the wrong region.
5. To replace it, open the stack's page and click Settings. Turn off Enable Stack Deletion Protection, then delete the stack.
6. Click Add Stack. Name it with lowercase letters and digits only, and pick an EU region. A stack can't change region later.

The free tier keeps logs and traces 14 days and holds 3 users. Past a monthly cap, Grafana drops data for the rest of the month.

## The app's token

The app's token can only write. It can't read or delete anything.

1. In the Cloud Portal, under Security, open Access Policies and create a policy named `laneway-api-otlp`. Set its realm to the `lanewayapi` stack. An org-wide policy made in the portal lands in the US.
2. Give it `logs:write`, `traces:write`, and `metrics:write`. Nothing else, not even read.
3. Add a token to it named `laneway-api-prd`. Copy it now, since Grafana shows it once.
4. On the stack's OpenTelemetry card, click Configure, then View connection details. Copy the OTLP endpoint and the instance id. The endpoint is `https://otlp-gateway-prod-eu-west-2.grafana.net/otlp`.
5. Build the auth value from the instance id and the token. `read -s` keeps both out of shell history:

   ```sh
   read -s "?Instance ID: " id; echo
   read -s "?Token: " tok; echo
   printf '%s' "$id:$tok" | base64 -w0; echo
   unset id tok
   ```

   A 7-digit id and a 156-character token give exactly 220 characters, ending in one `=`. Copy only those. zsh marks output that has no trailing newline with a `%`, and copying it along breaks the header.

6. In the Doppler dashboard, project `laneway-api`, config `prd`, add:

   ```sh
   OTEL_EXPORTER_OTLP_ENDPOINT=<endpoint from step 4>
   OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
   OTEL_EXPORTER_OTLP_HEADERS=Authorization=Basic%20<value from step 5>
   OTEL_SERVICE_NAME=laneway-api
   OTEL_RESOURCE_ATTRIBUTES=deployment.environment.name=production
   ```

The dashboard keeps the token out of shell history. Never put these in appsettings, and never in the `ci` config, which syncs every secret to GitHub. The exporter URL-decodes the header, so `%20` becomes the space Grafana expects.

Don't use the token the OTLP page offers to generate. Its policy may carry more scopes than these three.

No standing token holds `logs:delete`. The [purge](#purge-a-leaked-item) section makes one when it's needed and revokes it after.

## Readers

Each reader is a named person with their own Grafana.com login. There is no shared login.

1. Make sure they sign in with a password, not Google or GitHub, so the org's Enforce MFA setting covers them.
2. In the Cloud Portal, open Org Settings, then Members, and click Invite New Member. Pick the Viewer role. The operator is the only Admin.
3. List each reader here, with the date they got access.

The free tier allows 3 users, the operator included. The free tier doesn't record who ran which query, and [ADR 0004](../adr/0004-auth-targets-asvs-l2-with-exceptions.md#1642-logs-protected-from-access-and-edits-l2) lists that as an exception.

Readers:

- The operator, Admin, from 2026-10-28.

## Check the data arrives

Hosting isn't decided, so the first check runs from a laptop. It sends real data under `deployment.environment.name=production`, so it shows up next to real production data.

1. Check the token before running the app. The exporter logs nothing when Grafana rejects an export, so this is the only place a bad header shows. Each line should answer 200 or 204:

   ```sh
   ep=$(doppler secrets get OTEL_EXPORTER_OTLP_ENDPOINT --project laneway-api --config prd --plain)
   b64=$(doppler secrets get OTEL_EXPORTER_OTLP_HEADERS --project laneway-api --config prd --plain | sed 's/^Authorization=Basic%20//')
   for s in logs traces metrics; do
     curl -s -o /dev/null -w "$s %{http_code}\n" -X POST -H "Authorization: Basic $b64" \
       -H 'Content-Type: application/json' -d '{}' "$ep/v1/$s"
   done
   ```

   A 401 that says `no credentials provided` means the base64 is malformed, usually a stray character after the `=`.

2. Start a Postgres you can break, and migrate it:

   ```sh
   docker run -d --rm --name laneway-grafana -e POSTGRES_PASSWORD=check -p 55432:5432 postgres:18
   dotnet ef database update --connection "Host=localhost;Port=55432;Database=laneway;Username=postgres;Password=check"
   ```

3. Run the API as Production, with the `prd` telemetry values and the `dev` secrets around them:

   ```sh
   for v in OTEL_EXPORTER_OTLP_ENDPOINT OTEL_EXPORTER_OTLP_PROTOCOL OTEL_EXPORTER_OTLP_HEADERS OTEL_SERVICE_NAME OTEL_RESOURCE_ATTRIBUTES; do
     export "$v=$(doppler secrets get "$v" --project laneway-api --config prd --plain)"
   done
   export CONNECTIONSTRINGS__DEFAULTCONNECTION="Host=localhost;Port=55432;Database=laneway;Username=postgres;Password=check"
   export ASPNETCORE_URLS=http://localhost:5099
   export BREVO__APIKEY=unused-local-check BREVO__SENDEREMAIL=check@example.com
   export FORWARDEDHEADERS__KNOWNPROXIES__0=127.0.0.1 FORWARDEDHEADERS__KNOWNPROXIES__1=::1
   doppler run --config dev --preserve-env -- dotnet run --no-launch-profile
   ```

   `--preserve-env` keeps the exported values over `dev`'s. No Doppler config holds a Brevo API key, and the check sends no mail, so placeholders boot the app. Production answers plain HTTP with 400 `httpsRequired`, so the known proxies let `X-Forwarded-Proto: https` through.

4. Send a request that touches the database, and a sign-in that the CSRF check refuses:

   ```sh
   curl -H 'X-Forwarded-Proto: https' 'http://localhost:5099/api/v1/auth/username-available?username=grafanacheck'
   curl -H 'X-Forwarded-Proto: https' -H 'Content-Type: application/json' \
     -d '{"email":"a@example.com","password":"x"}' http://localhost:5099/api/v1/auth/sign-in
   ```

5. Force a 500 on a live database, so Npgsql records the failed command. Rename a table, then send the first request again. The body quotes a `traceId`:

   ```sh
   docker exec laneway-grafana psql -U postgres -d laneway -c 'ALTER TABLE "Users" RENAME TO "UsersGone";'
   ```

6. In Explore, pick `grafanacloud-lanewayapi-logs`, switch to Code, and run `{service_name="laneway-api"} | trace_id="<traceId>"`. The `RequestCompleted` line with status 500 and the `UnexpectedError` line are both there.
7. Pick `grafanacloud-lanewayapi-traces`, choose TraceQL, and paste the trace id. The trace shows the server span and the failed database span.
8. Pick `grafanacloud-lanewayapi-prom` and query `laneway_auth_csrf_rejections_total`. Grafana turns the dots in `laneway.auth.csrf.rejections` into underscores and adds `_total`.
9. In Drilldown, then Traces, switch the filter from Root spans to All spans, and open the Exceptions tab. It lists `Npgsql.PostgresException`.
10. Stop the API, and run `docker stop laneway-grafana`.

On 2026-10-28 every step passed.

### What the Exceptions tab shows

The tab reads `exception` events on spans. Our code never calls `RecordException`, so only Npgsql's event on a failed database command reaches it, and only with All spans selected. A 500 from anything else, a refused database connection included, shows under Errored traces and as an `UnexpectedError` log line, not under Exceptions. The `UnexpectedError` line is the complete record of 500s.

Application Observability is on. It bills by host hours, and the free tier includes 2,232 a month. Its knowledge graph wouldn't build, and nothing here needs it.

## Purge a leaked item

Do this only when something on the inventory's never-logged list reached Grafana, such as a password or an email address. It deletes log lines. Grafana Cloud offers no delete for traces or metrics, so a leak there stays until retention removes it, 14 days on the free tier.

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
