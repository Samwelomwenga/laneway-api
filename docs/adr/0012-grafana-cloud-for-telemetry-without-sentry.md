# Grafana Cloud for telemetry, without Sentry

Laneway ships logs, traces, and metrics over OTLP to one Grafana Cloud stack on the free tier, in an EU region. Errors go to Grafana's Exceptions tab, not Sentry. The budget allows only a free tier or self-hosting. Of the free tiers, only Grafana Cloud takes all three signals in the EU with enough alert rules. Self-hosting needs a 4 GB box that the undecided hosting doesn't give us.

## Considered options

- Axiom. 30 days of retention for free, but the free plan is US only with one user.
- New Relic. 100 GB free and a real errors inbox, but EU storage is a paid add-on and traces keep only 8 days.
- Honeycomb. It allows 2 triggers on free and keeps 60 days, past our 30-day ceiling.
- Better Stack. It keeps logs and traces 3 days.
- SigNoz, self-hosted. The most complete option, but it needs five containers and at least 4 GB of RAM.
- Sentry next to Grafana. ChalkApp API ran it. Its inbox beats Grafana's, but it's a second vendor and a second SDK. Its defaults attach query strings, headers with the user agent, and bodies, and the logging inventory bans all of those.

## Consequences

- A Grafana stack can't change region. Moving means a new stack and losing history.
- The free tier keeps logs and traces 14 days, under the inventory's 30-day ceiling. Pro, at $19 a month, keeps 30. We move to Pro on the first paying customer, or on the first incident that needed older logs.
- Past a free monthly cap, Grafana drops data for the rest of the month. An alert fires at 80% of any cap.
- Grafana's Exceptions tab has no "first seen" column and no new-error alert. An alert on any `UnexpectedError` covers that.
- The app's token carries only the logs, traces, and metrics write scopes. No standing token can delete logs. A delete token is created only to purge a never-logged item that leaked, then revoked. That is access control, not cryptographic tamper evidence, and ADR 0004's exceptions say so.
- Free allows 3 users. Each reader is named, with MFA. The free tier doesn't record who ran which query, so ASVS 16.4.2's read audit is a recorded exception.
- Local dev ships to the Aspire Dashboard, not a local Grafana.
