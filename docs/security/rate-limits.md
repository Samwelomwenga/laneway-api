# Rate limits

Every request except a CORS preflight falls under at least one written limit. This meets ASVS 2.1.3 and 2.3.2. `Features/Auth/RateLimits.cs` holds the numbers. Change this file in the same diff as any of them.

| Policy | Applies to | Counts by | Limit |
|---|---|---|---|
| `sign-in` | `POST /api/v1/auth/sign-in` | IP | 10 per minute |
| `sign-up` | `POST /api/v1/auth/sign-up` | IP | 10 per hour |
| `refresh` | `POST /api/v1/auth/refresh` | IP | 60 per minute |
| `username-check` | `GET /api/v1/auth/username-available` | IP | 30 per minute |
| `email-request` | `POST /api/v1/auth/resend-verification`, `POST /api/v1/auth/forgot-password` | IP | 10 per hour |
| `password-check` | `change-password`, `change-email`, `delete-user` | `sub` | 5 per minute |
| `add-member` | `POST /api/v1/workspaces/{id}/members`, `POST /api/v1/boards/{id}/members` | `sub` | 20 per minute |
| `webhook` | `POST /api/v1/webhooks/brevo` | IP | 600 per minute |
| `signed-in` | every request with a valid token | `sub` | 300 per minute |
| `anonymous` | every other request with no valid token | IP | 60 per minute |

Each policy is a sliding window with 6 segments. The limiter runs after authentication, so a valid token decides between `signed-in` and `anonymous`. A request with a missing or bad token counts as `anonymous`, unless it goes to sign-in, sign-up, refresh, the username checker, resend verification, forgot password, or the Brevo webhook. Those seven count only under their own policy. Email verification and password reset have no policy of their own, so they count as `anonymous`. `password-check` and `add-member` count on top of `signed-in`. The two member routes share one `add-member` bucket.

The webhook's bearer is a webhook token, not an access token, so the webhook never counts under `signed-in`. Brevo retries a 429 at 10 minutes, 1 hour, 2 hours, and 8 hours. A 429 delays an event without losing it.

The limiter checks every policy a request falls under before it takes a permit from any of them. A refused request spends nothing, so a 429 from `password-check` leaves the caller's `signed-in` count alone.

`email-request` covers the anonymous routes that can mail a stranger. Resend verification and forgot password share one bucket. The per-address cap sits behind it: no user gets more than 5 token emails in 24 hours, whatever IP asks. `VerifyEmail` and `ResetPassword` count together toward those 5. A capped request answers exactly as it would have, and logs `MessageSkipped` with `addressCapped`.

Outside Development, plain HTTP counts before it gets its `httpsRequired` 400.

## CORS preflights

The CORS middleware answers a preflight before the limiter runs, so preflights never count. A preflight carries no token, so the only way to count one is by IP. A browser sends one for most API calls, once per URL and method every 2 hours. Every user behind one office NAT would share a bucket, and once it ran out the browser would block the app for the whole office. A preflight touches no database and reaches no controller.

## Client IP

The IP is the socket address, or the forwarded one when the caller is a proxy in `ForwardedHeaders:KnownProxies` or `ForwardedHeaders:KnownNetworks`. IPv6 counts by its /64 prefix. A request with no IP shares one bucket with every other request that has none.

Counters live in process memory. With more than one instance, each counts on its own.

## A rejected request

A client over a limit gets 429 with the `tooManyRequests` error code and a `Retry-After` of one segment, in seconds: 10 for a 1-minute window, 600 for the hour. The 429 carries the CORS headers, so a browser app can read it. Each rejection logs `RateLimited` and adds to `laneway.auth.rate_limit.rejections`, as the [logging inventory](logging-inventory.md) says.

No setting turns the limits off. Tests run with the production numbers.
