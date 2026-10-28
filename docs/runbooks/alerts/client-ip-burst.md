# One clientIp burst

Tier: notify. The threshold is a starting guess. Retune it once real traffic exists.

## What fired

One `clientIp` was on 30 or more `Laneway.*` lines at Warning in production in 10 minutes. The alert's `clientIp` label names the address. Warnings include failed sign-ins, CSRF and rate limit refusals, denied access, rejected webhooks, and requests slower than a second.

## The first query to run

```logql
sum by (scope_name, event_name) (count_over_time({service_name="laneway-api", deployment_environment_name="production"} | clientIp="<clientIp>" | severity_text="Warning" [1h]))
```

## Telling noise from attack

Noise looks like one event name only. That can be `RequestCompleted` warnings on slow requests during a database hiccup, or `RateLimited` from one office behind a NAT.

An attack looks like a mix of `AccessDenied` across many ids, `SignInFailed` across many accounts, and `CsrfRejected` from a script with no browser headers. Someone is mapping the API.

## What to do

1. Read the event mix from the first query. Open the matching playbook if one event dominates, such as [sign-in failure ratio](sign-in-failure-ratio.md).
2. Check for any `Information` lines from the same address after the burst, such as `SignInSucceeded` or an access change. Those mean it got in somewhere.
3. If it is hostile and still going, block the address at the proxy once hosting has one.

## When to escalate

Escalate to an incident when the address shows a success after the burst, or the same pattern moves across many addresses at once.
