# Log usage

Tier: notify. The threshold is a starting guess. Retune it once real traffic exists.

## What fired

The stack took in more than 1.33 GB of log lines in the last day. That pace, kept up for 30 days, passes 80% of the free tier's 50 GB monthly cap. Past the cap, Grafana drops logs for the rest of the month. The rule counts every stream in the stack, not only production, since staging logs count toward the same cap.

`bytes_over_time` counts line bytes, not labels or structured metadata, so billed usage runs higher than this number. The Usage page in the Cloud Portal has the billed figure.

## The first query to run

```logql
sum by (service_name, deployment_environment_name, scope_name) (bytes_over_time({service_name=~".+"} [1d]))
```

## Telling noise from attack

Noise looks like a category whose volume grew with traffic, or a level set to Debug and left there.

An attack looks like `RequestCompleted` or Warning volume from a few addresses. Someone is flooding the API, and the logs bill for it. Run the first query from the [clientIp burst](client-ip-burst.md) playbook.

## What to do

1. Find the category or stream from the first query.
2. If a level was lowered, put it back to Information in config and redeploy.
3. If traffic grew for real, plan the move to Pro, which bills logs past the cap instead of dropping them.
4. If one source floods, block it at the proxy once hosting has one.

## When to escalate

Escalate when the month will pass the cap before anyone can act. Dropped logs leave a gap in the security record, as the logging stopped alert does.
