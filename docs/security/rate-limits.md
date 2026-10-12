# Rate limits

Every request falls under at least one written limit. This meets ASVS 2.1.3 and 2.3.2. `Features/Auth/RateLimits.cs` holds the numbers. Change this file in the same diff as any of them.

| Policy | Applies to | Counts by | Limit |
|---|---|---|---|
| `sign-in` | `POST /api/v1/auth/sign-in` | IP | 10 per minute |
| `sign-up` | `POST /api/v1/auth/sign-up` | IP | 10 per hour |
| `refresh` | `POST /api/v1/auth/refresh` | IP | 60 per minute |
| `password-check` | `change-password`, `change-email`, `delete-user` | `sub` | 5 per minute |
| `signed-in` | every request with a valid token | `sub` | 300 per minute |
| `anonymous` | every other request with no valid token | IP | 60 per minute |

Each policy is a sliding window with 6 segments. The limiter runs after authentication, so a valid token decides between `signed-in` and `anonymous`. A request with a missing or bad token counts as `anonymous`, unless it goes to sign-in, sign-up, or refresh. Those three count only under their own policy. `password-check` counts on top of `signed-in`.

## Client IP

The IP is the socket address, or the forwarded one when the caller is a proxy in `ForwardedHeaders:KnownProxies` or `ForwardedHeaders:KnownNetworks`. IPv6 counts by its /64 prefix. A request with no IP shares one bucket with every other request that has none.

Counters live in process memory. With more than one instance, each counts on its own.

## A rejected request

A client over a limit gets 429 with the `tooManyRequests` error code and a `Retry-After` in seconds. When the limiter gives no retry time, `Retry-After` is one segment: 10 seconds for a 1-minute window, 600 for the hour. Each rejection logs `RateLimited` and adds to `laneway.auth.rate_limit.rejections`, as the [logging inventory](logging-inventory.md) says.

No setting turns the limits off. Tests run with the production numbers.
