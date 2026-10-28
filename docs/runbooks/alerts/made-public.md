# Made public

Tier: notify. The threshold is a starting guess. Retune it once real traffic exists.

## What fired

A `VisibilityChanged` line in production with `visibility` `Public`. A workspace or a board became public, so anyone on the internet can read it. `previousVisibility` says what it was before.

## The first query to run

```logql
{service_name="laneway-api", deployment_environment_name="production"} | scope_name="Laneway.Access" | event_name="VisibilityChanged" | visibility="Public"
```

Read `sub`, `targetType`, `targetId`, and `clientIp`. Then see what else that `sub` did in the hour before:

```logql
{service_name="laneway-api", deployment_environment_name="production"} | scope_name=~"Laneway[.](Access|Auth)" | sub="<sub>"
```

## Telling noise from attack

Noise looks like a long-standing admin making a board public on purpose, from an address they've used before. Public boards are a real feature.

An attack looks like a change right after an [admin granted](admin-granted.md) alert, a password or email change, or a sign-in from a new address on the same `sub`. Someone may be exposing a private board's contents. A change from `Private` straight to `Public` exposes a board nobody else in the workspace could read, so ask its admins first.

## What to do

1. If it looks normal, do nothing.
2. If not, ask the target's other admins whether they meant it. If they didn't, an admin sets it back to private.
3. If the account is taken over, ask the owner to reset the password, which ends every session. Then set the visibility back.

## When to escalate

Escalate to an incident when a board that held private data was public and nobody meant it. Anything read while it was public counts as exposed, and the owners need to know.
