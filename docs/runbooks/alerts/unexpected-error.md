# UnexpectedError

Tier: page. The threshold is a starting guess. Retune it once real traffic exists.

## What fired

The API answered at least one 500 in production in the last 5 minutes. `ApiExceptionHandler` logs each one as `UnexpectedError` with the exception and the `traceId` it quotes in the 500 body. The notification policy groups for 5 minutes, so a storm sends one page.

## The first query to run

```logql
{service_name="laneway-api", deployment_environment_name="production"} | scope_name="Laneway.Api.ApiExceptionHandler" | event_name="UnexpectedError"
```

Take a `traceId` and open it in `grafanacloud-lanewayapi-traces` with TraceQL. The `RequestCompleted` line for the same trace names the route.

## Telling noise from attack

Most 500s are bugs or a broken dependency, such as a database that refused connections or a migration that didn't run. Those come as a burst across many routes, or every call to one route.

A 500 can also be an attack that found an input we don't handle. That looks like one address hitting one route with many 500s and odd inputs. Look for a `clientIp` on the `RequestCompleted` lines of the same traces.

## What to do

1. Read the exception type and the route. If the database is down, every route fails at once. Check the database first.
2. If the last deploy caused it, roll back.
3. If one route fails on one input, write a failing test for it and fix it.
4. A user who quotes a `traceId` from a 500 body finds the same trace.

## When to escalate

Escalate to an incident when 500s cover most routes for more than 15 minutes, or a 500 trace shows data that should never leave the database.
