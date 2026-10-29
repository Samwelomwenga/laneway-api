# Refresh token reused

Tier: page. The threshold is a starting guess. Retune it once real traffic exists.

## What fired

`laneway.auth.refresh` counted at least one `reused` result in production in the last 5 minutes. A retired refresh token came back after the 10-second grace window. The API already ended that session, and a `SessionEnded` with `endReason` `TokenReused` follows the reuse.

## The first query to run

```logql
{service_name="laneway-api", deployment_environment_name="production"} | scope_name="Laneway.Auth" | event_name=~"RefreshTokenReused|SessionEnded"
```

Note the `sub`, `sid`, and `clientIp` on the `RefreshTokenReused` line. Then pull every `Laneway.Auth` line for that `sub` over the last day:

```logql
{service_name="laneway-api", deployment_environment_name="production"} | scope_name="Laneway.Auth" | sub="<sub>"
```

## Telling noise from attack

Noise looks like a single reuse from the same `clientIp` the session always used, often a browser tab restored from sleep, or two tabs racing past the grace window.

An attack looks like a reuse from an address the session never used, or a run of reuses across several users. Someone holds stolen refresh tokens.

## What to do

1. Check whether the reuse came from a new address. If it did, treat the account as compromised. Ask the owner to reset the password, which ends every session.
2. If several users show reuse from one address, look for how the tokens left. Check the CORS and cookie settings were not changed in the last deploy.
3. Write down the `sub`, the addresses, and what you did.

## When to escalate

Escalate to an incident at once when reuses span more than one user, or one user's reuse comes from an address that also touched other accounts. That points at a leak, not a race.
