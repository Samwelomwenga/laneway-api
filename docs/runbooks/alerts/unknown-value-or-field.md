# Unknown value or field

Tier: notify. The threshold is a starting guess. Retune it once real traffic exists.

## What fired

A `RequestCompleted` line in production had `unknownValue` or `unknownField` in its `errorCodes`. The API answered 400. `unknownField` means the body, the query string, or a multipart form named a field the endpoint doesn't have. `unknownValue` means an enum, a position, or a query value fell outside its fixed set. Our clients send only what the API defines, so something else built that request.

## The first query to run

```logql
sum by (route, clientIp, sub, errorCodes) (count_over_time({service_name="laneway-api", deployment_environment_name="production"} | scope_name="Laneway.Request" | event_name="RequestCompleted" | errorCodes=~`.*"(unknownValue|unknownField)".*` [1h]))
```

The line never says which field or value it was. Only the code is logged.

## Telling noise from attack

Noise looks like one route, from many addresses and users, starting right after a deploy of the API or a client. The client and the API disagree on a field name or an enum value.

An attack looks like many routes from one `clientIp`, or one `sub`, in a short burst. Someone is guessing at fields, such as a role or an owner id on a body, to see what the API accepts.

## What to do

1. If it started with a deploy, compare the client's request for that route with the API's DTO. Fix whichever side drifted.
2. If one address or one `sub` is guessing, the 400s mean nothing got through. Check the [clientIp burst](client-ip-burst.md) query for the same address, since a prober usually trips other warnings too.
3. If one `sub` is behind it, check that account's recent `Laneway.Access` and `Laneway.Auth` lines for changes it shouldn't have made.

## When to escalate

Escalate to an incident when the same address or `sub` also shows an access change or a 2xx on the routes it was probing. That means a guess may have landed.
