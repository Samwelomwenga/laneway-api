# Alerting

Grafana alerts the operator in two tiers. Page goes to Grafana IRM and Telegram. IRM pushes to the mobile app, then emails after 15 minutes without an ack. Notify goes to email and Telegram. No SMS and no phone calls. The rota is the operator alone.

`terraform/alerting` holds the contact points, the notification policy tree, the IRM escalation, and the rules. The repo is the source of truth. Provenance stays on, so the Grafana UI locks what Terraform made, and a change made there is drift. Each rule links its playbook in [alerts/](alerts/) from its `runbook_url`.

A merge to `main` that touches `terraform/alerting` plans it in CI. The apply waits for the operator to approve the `alerting` environment. [The Terraform runbook](terraform.md) covers the state, the secrets, and the tokens every root shares. The three IRM notification rules are the one part Terraform doesn't manage. The operator sets them by hand.

## Rules

| Rule | Source | Fires when | Tier |
|---|---|---|---|
| [Sign-in failure ratio](alerts/sign-in-failure-ratio.md) | `laneway.auth.sign_in.attempts` | over 50% `wrong_password` or `unknown_account` in 15 min, with at least 20 attempts | notify |
| [Refresh token reused](alerts/refresh-token-reused.md) | `laneway.auth.refresh{result=reused}` | any in 5 min | page |
| [Delivery failed at Error](alerts/delivery-failed.md) | LogQL | any in 5 min | page |
| [WebhookRejected run](alerts/webhook-rejected-run.md) | LogQL | 5 or more in 15 min | notify |
| [One clientIp burst](alerts/client-ip-burst.md) | LogQL, `Laneway.*` Warnings by `clientIp` | 30 or more from one address in 10 min | notify |
| [UnexpectedError](alerts/unexpected-error.md) | LogQL | any in 5 min | page |
| [Unknown value or field](alerts/unknown-value-or-field.md) | LogQL on `errorCodes` | any in 15 min | notify |
| [Id scan](alerts/id-scan.md) | `laneway.authorization.denials.unknown_target` | 20 or more in 10 min | notify |
| [Admin granted](alerts/admin-granted.md) | LogQL, `MemberAdded` or `MemberRoleChanged` with `role` Admin | any in 5 min | notify |
| [Made public](alerts/made-public.md) | LogQL, `VisibilityChanged` to Public | any in 5 min | notify |
| [Logging stopped](alerts/logging-stopped.md) | No Data on `dotnet.process.cpu.count` | no data for 10 min | page |
| [Log usage](alerts/log-usage.md) | LogQL `bytes_over_time`, whole stack | over 1.33 GB in a day | notify |

Every rule filters on `deployment_environment_name="production"`, except log usage. The cap covers the whole stack, staging included, so that rule counts every stream. Every threshold is a starting guess.

A rule whose query fails sends Grafana's `DatasourceError` alert, which routes to notify whatever the rule's tier. The notification policy groups by rule for 5 minutes, so a storm of 500s sends one page.

Log usage uses the fallback rule, not the Cost Management usage alert. The Terraform provider has no resource for Cost Management alerts, so the built-in one would live only in the UI. The rule counts line bytes, so it reads lower than the billed figure.

Logging stopped stays paused while `production_hosted` is false. Nothing runs in production before hosting exists, so the rule would page all day.

## Before the first apply

### The service account

1. In the stack, open Administration, then Users and access, then Service accounts. Add one named `terraform-alerting` with the Viewer role. The provider reads IRM's plugin settings through Viewer's `plugins.app:access`.
2. Under its permissions, add `fixed:alerting.provisioning:writer`, `fixed:folders:creator`, and the IRM app's Editor role. The alerting role alone can't create the rules' folder or the IRM integration. Once you pick the IRM role, write its exact name here.
3. Add a token that expires in 90 days at most. Copy it.

### IRM

1. Sign in to IRM once as the operator, so it knows the user.
2. Install the Grafana IRM mobile app, sign in, and connect it from the operator's IRM profile.
3. On the operator's IRM profile, delete the default notification rules and add three in this order: notify by mobile app, wait 15 minutes, notify by email. Terraform used to own these, but the provider manages personal rules only with an IRM API token. Keeping them by hand drops that token.

### Telegram

The CI bot can post these too. Make a separate chat for alerts, add the bot, and read the chat id from `https://api.telegram.org/bot<token>/getUpdates` after one message.

### Doppler

Make a Doppler project `laneway-alerting` with one config, `prd`. Keep it apart from `laneway-api`, so the app never holds a token that can change alerting. Add these in the dashboard, not the shell:

```sh
TF_TOKEN_app_terraform_io=<HCP owners team token>
TF_VAR_grafana_auth=<service account token>
TF_VAR_operator_username=<the operator's Grafana username>
TF_VAR_operator_email=<the operator's email>
TF_VAR_telegram_bot_token=<bot token>
TF_VAR_telegram_chat_id=<chat id>
```

[The Terraform runbook](terraform.md) says how to make the HCP token, the service token, and the two GitHub environments.

### Data source ids

The rules query `grafanacloud-logs` and `grafanacloud-prom`, the uids Grafana Cloud gives its default data sources. A uid isn't the display name. Explore and the playbooks show the names, `grafanacloud-lanewayapi-logs` and `grafanacloud-lanewayapi-prom`. Open Connections, then Data sources, and check each one's uid in its URL. If they differ, set `TF_VAR_logs_datasource_uid` and `TF_VAR_metrics_datasource_uid` in Doppler.

Both uids are the defaults. The operator checked the prom one on 2026-10-29 and the logs one on 2026-10-30, so Doppler needs neither override.

### Check the labels the rules use

Every rule but logging stopped treats no data as OK. A wrong label name makes a rule return nothing and never fire. So check the labels against real data first. Run the arrival check in [the Grafana runbook](grafana.md#check-the-data-arrives), then in Explore:

1. In `grafanacloud-lanewayapi-logs`, run `{service_name="laneway-api", deployment_environment_name="production"} | event_name="RequestCompleted" | scope_name="Laneway.Request"`. Lines must come back. If none do, open one line's details and read the field names Loki gave the event name and the category, then fix `rules.tf` to match.
2. Run the same with `| severity_text="Warning"` after a slow or refused request, and check a `clientIp` field shows.
3. In `grafanacloud-lanewayapi-prom`, run `dotnet_process_cpu_count{deployment_environment_name="production", job="laneway-api"}` and `laneway_auth_csrf_rejections_total{deployment_environment_name="production"}`. Both must return a series. If the label is missing, Grafana didn't promote `deployment.environment.name`, and it sits on `target_info` only.
4. Force each field the newer rules filter on, then find it in Explore:
   - Send `POST /api/v1/workspaces` with a body that has an extra field, such as `{"name": "Test", "bogus": 1}`. Its `RequestCompleted` line must show `errorCodes` as `["unknownField"]`, quotes included. If Loki stores it another way, fix the `errorCodes` regex in `rules.tf`.
   - Make a member of a test board admin. The `MemberRoleChanged` line must show `role` `Admin`.
   - Make the test board public. The `VisibilityChanged` line must show `visibility` `Public`.
   - Request a board by a random id. `laneway_authorization_denials_unknown_target_total{deployment_environment_name="production"}` must return a series within a minute.
5. Paste each rule's `expr` from `rules.tf` into Explore. Each must parse.

On 2026-10-29 every step passed. All twelve rule queries ran in Explore without an error. The unknown value or field, id scan, admin granted, and made public queries each returned a value after their event was forced, so `errorCodes` keeps its quotes in Loki and the regex matches.

## Apply

1. If you want, plan locally first, as [the Terraform runbook](terraform.md#run-terraform-locally) shows.
2. Merge the PR. `Terraform alerting` starts on `main`.
3. Read the change list in the `Plan` job's summary. It shows each address and its action, and nothing else.
4. In the run, click Review deployments, tick `alerting`, and approve. `Apply` plans again, checks the list didn't move, and applies.

## Check a test alert arrives

1. In Alerting, open Contact points. On `Laneway page`, click Test. The phone gets a push, and Telegram gets a message. Leave the IRM alert group unacknowledged for 15 minutes, and the email arrives. Then acknowledge it.
2. On `Laneway notify`, click Test. Email and Telegram each get a message.
3. Write the date each one passed here.

## Once production is hosted

Open a PR that sets the `production_hosted` default to `true` in `terraform/alerting/variables.tf`. The first production deploy does this once its health check passes. Once the PR merges and the apply goes through, logging stopped starts evaluating. The value stays out of Doppler.

## Changing a rule

Edit `terraform/alerting/rules.tf`, its playbook, its row in the Rules table above, and its row in the Alerting section of [the logging inventory](../security/logging-inventory.md#alerting) in the same diff. Every rule names a `playbook`, and `AlertPlaybookTests` fails when the file is missing, lacks the five headings, or the inventory row's title, tier, or playbook disagrees with `rules.tf`. Merge the PR, read the change list, and approve the gate.
