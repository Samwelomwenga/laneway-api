# Delivery failed at Error

Tier: page. The threshold is a starting guess. Retune it once real traffic exists.

## What fired

A `DeliveryFailed` line logged at Error in production. Brevo answered 401, 402, or 403. The API key, the account's credits, or the key's permissions broke, so every send fails until someone fixes it. That includes verification and password reset mail.

## The first query to run

```logql
{service_name="laneway-api", deployment_environment_name="production"} | scope_name="Laneway.Message" | event_name="DeliveryFailed" | severity_text="Error"
```

Read `status` and `code` on the lines.

## Telling noise from attack

There is no noise version. A 401, 402, or 403 never fixes itself.

A 401 after nobody touched the key can mean someone revoked or rotated it in the Brevo dashboard. Check the Brevo account's security log for a login you don't recognize.

## What to do

1. 401: the key is wrong or revoked. Make a new key in Brevo, put it in Doppler's `prd` config as `BREVO__APIKEY`, and redeploy. [The Brevo runbook](../brevo.md) has the dashboard steps.
2. 402: the account ran out of credits. Top up or move plans.
3. 403: the key lacks a permission, or Brevo blocked the account. Read the `code` and Brevo's message in its dashboard.
4. Deliveries that failed stay failed. Users can ask again, by resending verification or requesting a new reset.

## When to escalate

Escalate to an incident if Brevo blocked the account, or the Brevo security log shows a login nobody made. Both mean more than a broken key.
