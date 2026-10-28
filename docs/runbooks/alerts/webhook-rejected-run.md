# WebhookRejected run

Tier: notify. The threshold is a starting guess. Retune it once real traffic exists.

## What fired

5 or more `WebhookRejected` lines in 15 minutes in production. Each one is a call to `POST /api/v1/webhooks/brevo` with a missing or bad webhook token, answered 401.

## The first query to run

```logql
sum by (reason, clientIp) (count_over_time({service_name="laneway-api", deployment_environment_name="production"} | scope_name="Laneway.Message" | event_name="WebhookRejected" [1h]))
```

## Telling noise from attack

Noise looks like `expired` or `invalidSignature` from Brevo's own addresses, right after a token rotation. Brevo is still sending the old token.

An attack looks like `missing` or `malformed` from addresses that aren't Brevo's, or many reasons from one address. Someone found the webhook and is probing it.

## What to do

1. If the reasons point at a rotation, finish it. [The Brevo runbook](../brevo.md#rotate-the-token) has the steps.
2. If the calls come from somewhere else, nothing gets through without a valid token. Note the addresses. If they keep coming, block them at the proxy once hosting has one.
3. If `expired` lines come from Brevo and nobody rotated, the token reached its expiry. Mint a new one.

## When to escalate

Escalate when a probing address also shows `WebhookEventUnmatched` lines. That means it holds a valid token, which is a leak. Follow "If a token leaks" in the Brevo runbook.
