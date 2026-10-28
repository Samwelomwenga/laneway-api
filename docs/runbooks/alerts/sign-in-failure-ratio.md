# Sign-in failure ratio

Tier: notify. The threshold is a starting guess. Retune it once real traffic exists.

## What fired

Over half of the sign-in attempts in the last 15 minutes failed with `wrong_password` or `unknown_account`, out of at least 20 attempts. The source is the `laneway.auth.sign_in.attempts` counter in production.

## The first query to run

In Explore, pick `grafanacloud-lanewayapi-logs`, switch to Code, and count failures by address:

```logql
sum by (clientIp) (count_over_time({service_name="laneway-api", deployment_environment_name="production"} | scope_name="Laneway.Auth" | event_name="SignInFailed" [15m]))
```

Then look at the ratio over a longer window in `grafanacloud-lanewayapi-prom`:

```promql
sum by (result) (increase(laneway_auth_sign_in_attempts_total{deployment_environment_name="production"}[1h]))
```

## Telling noise from attack

Noise looks like a handful of addresses, each with a few failures, and `sub` set on most lines. That is people who forgot a password, often after a deploy that signed everyone out.

An attack looks like many `accountFound: false` lines, which means someone is trying emails we don't have. It also looks like one address, or one /64, behind most of the failures, or many `sub` values each failing once or twice. That last one is credential stuffing.

## What to do

1. If one address or a small set dominates, the rate limiter already slows it. Check `laneway_auth_rate_limit_rejections_total` climbed with it.
2. If accounts are being guessed one at a time from many addresses, check `SignInBlockStarted` lines. Sign-in blocks stop a single account after 5 wrong passwords.
3. If a real account signed in during the run from an address that also failed against other accounts, treat it as taken over. Ask the owner to reset the password. A reset ends every session.
4. Write down the addresses, the time range, and what you did.

## When to escalate

Escalate to an incident when a success follows a run of failures from the same address against a different account, or when the run lasts past an hour with the limiter already refusing. Neither has a page today, so the operator decides.
