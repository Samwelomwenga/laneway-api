# Id scan

Tier: notify. The threshold is a starting guess. Retune it once real traffic exists.

## What fired

`laneway.authorization.denials.unknown_target` rose by 20 or more in 10 minutes in production. Each count is a request that named an id that doesn't exist. The API answers it the same way as a hidden object: a 404 for a route id, a 400 for a body or filter id. `DenialDrainer` counts these and never logs them, so the random ids stay out of the logs. The `targetType` tag says which kind of object the ids claimed to be.

## The first query to run

```promql
sum by (targetType) (increase(laneway_authorization_denials_unknown_target_total{deployment_environment_name="production"}[1h]))
```

Then find who sent them. Misses log nothing, but each request still logs its `RequestCompleted` line. A route id miss shows as a 404 with no `errorCodes`. A body or filter id miss shows as a 400 with `notFound` in `errorCodes`:

```logql
sum by (clientIp, sub, route) (count_over_time({service_name="laneway-api", deployment_environment_name="production"} | scope_name="Laneway.Request" | event_name="RequestCompleted" | StatusCode="404" or errorCodes=~`.*"notFound".*` [1h]))
```

The 404s include requests to routes that don't exist, which show `route` `unmatched`.

## Telling noise from attack

Noise looks like a small climb on one `targetType` after a bulk delete. Open tabs and cached links still point at the deleted cards or boards.

An attack looks like a steady climb across one or more types, with most of the misses from one `clientIp` or one `sub`. Someone is walking ids to find objects they can't see. Our ids are random GUIDs, so a scan should find nothing.

## What to do

1. Read the `targetType` split. Note the address and `sub` from the second query.
2. Check for `AccessDenied` lines from the same address. Those are ids that exist but that the caller can't reach, so the scan found something real.
3. If it is hostile and still going, block the address at the proxy once hosting has one. If one `sub` is behind it, the account may be taken over. Ask the owner to reset the password, which ends every session.

## When to escalate

Escalate to an incident when the same caller gets a 2xx on an id it hadn't been given, or ids that weren't random show up. Either means the ids are leaking somewhere.
