# Logging inventory

What Laneway logs about requests, auth, and unexpected errors, and what it never logs. This meets ASVS 16.1.1. A change that adds an event, a field, or a counter updates this file in the same diff.

## Sink

The API ships logs over OTLP, with traces and metrics, through one exporter. Only startup failures and Serilog's own errors go to stderr. OTLP carries each field as its own attribute, so a value can't forge a second entry. Retention is 30 days, the longest a session lives, and the deploy enforces it.

## Fields on every entry

The record itself holds the time, the level, the category, the event id and name, the message template, the trace id and span id, and the exception when there is one. OTLP sends them as record fields, not attributes. OTLP calls the category the scope name. The stable key for an event is the scope name plus `EventName`, since ids repeat across categories. OpenTelemetry's experimental `OTEL_DOTNET_EXPERIMENTAL_LOGS_EMIT_EVENT_LOG_ATTRIBUTES` flag, which copies the id and name into attributes, stays off.

Serilog copies the category and the event into attributes too. ASP.NET Core adds the endpoint and the connection inside a request. So any entry may carry these besides its own fields:

| Field | Carries |
|---|---|
| `SourceContext` | the category |
| `EventId` | the event id and name |
| `ActionId` | the endpoint's id |
| `ActionName` | the endpoint's method name |
| `ConnectionId` | Kestrel's id for the connection |

Framework categories start with `Microsoft.` or `System.`. This file doesn't list their entries at Warning and above. It doesn't list framework meters either.

## Requests

Each request logs one line when it ends, under the `Laneway.Request` category. `UseSerilogRequestLogging` writes it with its own message template, so Serilog's default, which quotes the raw path, never runs.

| Id | Event | Level | Fields |
|---|---|---|---|
| 1 | `RequestCompleted` | Error for a 5xx or an unhandled exception, Warning over 1 second, otherwise Information | `RequestMethod`, `route`, `StatusCode`, `Elapsed`, `clientIp` when the request has an address, and `sub` and `sid` when a valid access token signed it in |

`RequestMethod` is `GET`, `HEAD`, `POST`, `PUT`, `PATCH`, `DELETE`, `OPTIONS`, `TRACE`, or `CONNECT`, and `other` for anything else. `route` is the endpoint's route template, such as `api/v1/WorkSpaces/{id}`, or `unmatched` when no endpoint matched. A 500 keeps the route of the endpoint that threw. `Elapsed` is in milliseconds. A token that fails, or names an ended session, leaves `sub` and `sid` off.

A 4xx logs at Information. A 404 or 405 gets no event of its own, so this line is its only record.

## The client IP

`clientIp` is the address `ForwardedHeadersMiddleware` resolves, the same one the rate limiter counts. `X-Forwarded-For` counts only when the caller is in `ForwardedHeaders:KnownProxies` or `ForwardedHeaders:KnownNetworks`. With neither set, the socket address stands. The field holds the full address. The rate limiter cuts an IPv6 address to its /64 for its own buckets, and the log doesn't. An IPv4 address mapped into IPv6 logs as plain IPv4. A request with no remote address leaves the field off.

The purpose is network and information security, which GDPR recital 49 names as a legitimate interest. It lets a reviewer trace an attack back to its source. An erasure request doesn't reach logs, and retention removes the address.

It appears on these entries only:

- `RequestCompleted`.
- Every `Laneway.Auth` event logged inside a request, `RateLimited` and `CsrfRejected` included.
- `AccessDenied`. The `Denial` record carries it, since `DenialDrainer` logs on another thread.
- `WebhookRejected`.

Delivery events, sweeps, `UnexpectedError`, and the other webhook events never carry it. No counter tags by it.

## Correlation

Every entry logged inside a request carries the request's W3C trace id, and so do its spans. `UseTraceIdentifier` runs first in the pipeline and sets `HttpContext.TraceIdentifier` to that trace id. The 500 body, `UnexpectedError`, and a ProblemDetails body all quote it as `traceId`. ProblemDetails would otherwise quote the whole `traceparent`.

Each request starts a fresh root trace. The API ignores an inbound `traceparent`, `tracestate`, and `baggage`, so a client can't pick its trace id or join someone else's trace. It sends none of them on any outbound HttpClient either, so Brevo and Pwned Passwords learn nothing about our traces. Their client spans still ship. `TraceBoundary` does both by setting the runtime's propagator and OpenTelemetry's to ones that read and write nothing.

`AccessDenied` logs after the response, on `DenialDrainer`'s thread. The `Denial` record carries the request's trace context, and the drainer restores it for the log call only. The entry has the request's trace id and a span id that names no shipped span. The drainer's existence check runs before that, outside the request trace.

## Events

Auth events log under the `Laneway.Auth` category. Each has a fixed event id. Every entry also carries the route and the time, and `clientIp` inside a request with an address.

| Id | Event | Level | Fields |
|---|---|---|---|
| 1 | `AccessTokenRejected` | Information | `reason`, and `kid` when it names a configured key, otherwise `kidKnown: false` |
| 2 | `SignUp` | Information | `sub` |
| 3 | `SignInSucceeded` | Information | `sub`, `sid` |
| 4 | `SignInFailed` | Warning | `sub` when the email belongs to a user, otherwise `accountFound: false`, and `blocked` |
| 5 | `BreachedCheckUnavailable` | Warning | the exception from the Pwned Passwords call |
| 6 | `SessionRefreshed` | Information | `sub`, `sid`, and `refreshTokenId` of the new token |
| 7 | `RefreshSuperseded` | Information | `sub`, `sid` |
| 8 | `RefreshTokenReused` | Warning | `sub`, `sid`, and `refreshTokenId` of the reused token |
| 9 | `RefreshRejected` | Information | `reason`, and `sid` when the token was found |
| 10 | `SessionEnded` | Information | `sub`, `sid`, `endReason` |
| 11 | `CsrfRejected` | Warning | `check` |
| 12 | `SignInBlockStarted` | Warning | `sub`, `failureCount`, `blockedUntil` |
| 13 | `PasswordCheckFailed` | Warning | `sub`, `sid` when a session sent it, `blocked` |
| 14 | `PasswordChanged` | Information | `sub`, and `sid` when a session made the change |
| 15 | `EmailChanged` | Information | `sub` |
| 16 | `UserDeleted` | Information | `sub`, `sid` |
| 17 | `RateLimited` | Warning | `policy`, and `sub` when the policy counts by user |
| 18 | `MessageSkipped` | Information, or Warning for `addressCapped` | `messageType`, `reason`, and `sub` when the address belongs to a user |
| 19 | `EmailChangeUndone` | Warning | `sub` |

`reason` is one of `malformed`, `expired`, `unknownKey`, `invalidSignature`, `invalidAlgorithm`, `invalidType`, `invalidIssuer`, `invalidAudience`, `sessionEnded`, or `invalid`.

`RefreshRejected` has `reason` `missingCookie`, `unknownToken`, or `sessionEnded`. `sessionEnded` covers a session that ended, passed its 30-day limit, sat idle for 7 days, or belongs to a deleted user.

`RefreshSuperseded` is a refresh that lost a race inside the 10-second grace window. It ends nothing. `RefreshTokenReused` is a retired token back after that window, and a `SessionEnded` with `endReason` `TokenReused` follows it.

`SessionEnded` logs when an action ends a session. A session that runs out logs nothing, because nothing happens at that moment. `endReason` is `SignedOut`, `SignedOutEverywhere`, `EndedFromSessionList`, `PasswordChanged`, `UserDeleted`, `TokenReused`, `PasswordReset`, or `EmailChangeUndone`. Sign-out everywhere, change password, password reset, undo email change, and delete user log one entry per session they end. Ending a session that already ended logs nothing.

`SignUp` logs when a sign-up creates an unverified user or takes over one. A sign-up whose address belongs to a verified user logs `MessageSkipped` instead. `AuthSweeper` runs hourly and logs nothing per row. It deletes expired token rows, deletes password history rows replaced more than 14 days ago, and hard-deletes users still unverified 7 days after their latest sign-up, in batches of 1,000. A failed sweep logs `SweepFailed`, listed under [Background jobs and cleanup](#background-jobs-and-cleanup). No session exists yet, so there is no `sid`. Using the verification link logs `SignInSucceeded` with the new session, and the counter tags it `succeeded`.

`MessageSkipped` logs when sign-up, resend verification, forgot password, or change email sends no email and still answers 202. `messageType` is `VerifyEmail` for the first two, `ResetPassword` for forgot password, and `ConfirmEmailChange` for change email. `reason` is one of these:

- `unknownAccount`: no user has the address. Only resend and forgot password log it, and it has no `sub`.
- `verified`: a verified user has the address, for `VerifyEmail`.
- `unverified`: an unverified user has the address, for `ResetPassword`. Only verified users get a reset link.
- `suppressed`: the address hard-bounced, marked mail as spam, or was blocked. Sign-up still creates or takes over the user.
- `addressCapped`: the user already got 5 token emails in the last 24 hours. For change email, the new address already got 5 confirm emails in the last 24 hours, from any account. The request changed nothing. It logs at Warning, since that many to one address looks like mail-bombing.

`CsrfRejected` is a sign-up, verification, sign-in, or refresh that failed a CSRF check. `check` names the first one that failed: `header` when `X-Laneway-Csrf: 1` is missing, `origin` when `Origin` isn't in `Cors:AllowedOrigins`, or `fetch-site` when `Sec-Fetch-Site` is `cross-site`. The 403 body is the same for all three.

An unverified user who sends the right password gets a 403 and logs `SignInFailed` with `blocked: false`. The counter tags that attempt `unverified`.

`SignInFailed` has `blocked: true` when the user was in a sign-in block, whatever password they sent. An unknown email always has `blocked: false`. `SignInBlockStarted` logs when a block starts, on every 5th wrong password since the count last reset. `failureCount` is the count that started the block, and `blockedUntil` is when it ends. Attempts during a block don't add to the count. A successful sign-in resets the count, and so do 12 hours with no wrong password.

`PasswordCheckFailed` is a wrong password, or any password during a sign-in block, sent to change password, change email, or delete user. `sid` is the session that sent it. A wrong password there counts toward the sign-in block, so it can also log `SignInBlockStarted`. `blocked: true` means the request got a 429 because a block was running. A wrong recent password sent to undo email change logs it with no `sid`, since no session sent it. It counts toward the sign-in block too. A running block never stops an undo, so there `blocked: true` only says a block was running.

`PasswordChanged` and `UserDeleted` log the action itself, with the `sid` of the session that made it. A password reset logs `PasswordChanged` with no `sid`, since a link made the change and no session did. `EmailChanged` logs when someone uses the confirm link, so it has no `sid` either. Asking for a change logs nothing, because nothing on the account changes yet. `EmailChanged` never logs either email. `EmailChangeUndone` logs when someone uses an undo link, with no `sid` and no email. It also covers the new password the undo sets, so no `PasswordChanged` logs. It logs at Warning, since an undo means someone the owner didn't trust changed the address.

`RateLimited` logs a request that got a 429 from a rate limit. `policy` is any policy in [Rate limits](rate-limits.md), such as `sign-in`, `email-request`, or `anonymous`. Only `password-check` and `signed-in` count by user, so only they log `sub`. The limits are in [Rate limits](rate-limits.md).

`BreachedCheckUnavailable` means the Pwned Passwords check failed or took over 2 seconds, so the password was accepted without it. Each entry is one password that skipped ASVS 6.2.12.

## Authorization denials

Denials log under the `Laneway.Authorization` category, which meets ASVS 16.3.2. ADR 0007 records the scheme.

| Id | Event | Level | Fields |
|---|---|---|---|
| 1 | `AccessDenied` | Warning | `sub`, `targetType`, `targetId`, `status`, `code`, and `clientIp` when the request had an address |
| 2 | `DenialCheckFailed` | Warning | the exception from the existence check |

`targetType` names the denied object: `Workspace`, `Board`, `List`, `Card`, `Label`, `Checklist`, `CheckItem`, `Attachment`, `Comment`, or `CopyJob`. `status` is 404 when the caller couldn't read a route object, and `code` is then `notFound`. `status` is 400 with `code` `notFound` when the id came from a body or filter field the caller couldn't read, such as a move's `boardId`, an anchor, or an activity `cardId` filter. `status` is 403 when they could read the object but their role forbids the action, and `code` is the 403 code, such as `readOnlyRole`, `requiresWorkspaceAdmin`, or `notAuthor`. A copy job is readable only by the user who started it.

The service puts each denial on a bounded channel and answers at once, so a hiding 404 is as fast as a missing one. `DenialDrainer` reads the channel in the background. For a 404 or a 400 it checks whether the object exists, and drops the entry when it doesn't. A random id is a miss, not a denial. So every logged `targetId` names a real object. When the check itself throws, the drainer logs `DenialCheckFailed` and the denial goes unlogged.

The channel holds 1024 entries. When it's full, `DenialLog` drops the denial and counts it on the `Laneway.Authorization` meter.

| Counter | Tags |
|---|---|
| `laneway.authorization.denials.dropped` | none |

## Deliveries

Deliveries log under the `Laneway.Message` category. Features never learn how a delivery ended, so these entries are the only record of a failed or dropped one. `DeliveryWorker` logs each delivery inside a scope that carries `deliveryId`, `messageType`, `channel`, `attempt`, and `sub` when the message names a user.

| Id | Event | Level | Fields |
|---|---|---|---|
| 1 | `DeliverySent` | Information | `providerMessageId` |
| 2 | `DeliveryRetrying` | Information | `reason`, `status` when Brevo answered, `nextAttemptAt` |
| 3 | `DeliveryFailed` | Warning, or Error when `status` is 401, 402, or 403 | `reason`, `status` when Brevo answered, and `code` when Brevo sent a known one |
| 4 | `DeliveryExpired` | Warning | `sendBy` |
| 5 | `DeliveryDropped` | Information | `reason`, and `suppressedBy` when `reason` is `suppressed` |
| 6 | `WebhookRejected` | Warning | `reason`, and `clientIp` when the request has an address |
| 7 | `AddressSuppressed` | Information | `event`, `deliveryId`, `messageType`, `alreadySuppressed` |
| 8 | `WebhookEventUnmatched` | Warning | `event`, `reason` |
| 9 | `WebhookEventIgnored` | Information | `event` |
| 10 | `DeliveryPollFailed` | Warning | the exception |
| 11 | `DeliverySendThrew` | Error | the exception |

`DeliveryRetrying` has `reason` `rateLimited`, `serverError`, `timeout`, or `networkError`. A 429 is `rateLimited` and a 5xx is `serverError`. Each Brevo call times out after 10 seconds. Attempts run at 0, 30 seconds, 2 minutes, and 10 minutes after the one before, all with the same idempotency key.

`DeliveryFailed` has `reason` `retriesExhausted` or `rejected`. `retriesExhausted` follows a fourth attempt that would have retried, or a claim past the fourth attempt after a send threw or the worker stopped mid-send. `rejected` is any other 4xx from Brevo, which fails at once. A 401, 402, or 403 means the API key, the credits, or the key's permissions broke, so every send fails until someone fixes it. Those log at Error.

`DeliveryExpired` is a delivery past its `SendBy`, dropped without calling Brevo. The retry schedule ends long before any `SendBy`, so this means the worker stalled.

`DeliveryDropped` has `reason` `userDeleted`, `noAddress`, `unsubscribed`, or `suppressed`. `userDeleted` covers a user who was deleted, or whose id matches no user row. `noAddress` is a delivery with nowhere to go on its channel. `unsubscribed` is an opt-out message to an address that unsubscribed. `suppressed` is any message to an address that hard bounced, complained of spam, or was blocked, and `suppressedBy` says which: `hardBounce`, `spam`, or `blocked`.

Before it calls Brevo, the worker checks `SendBy`, then the user, then the address, then an unsubscribe, then the other suppressions, and logs only the first that applies. A sent, failed, or dropped delivery clears its secret column. A send that throws anything other than a Brevo answer, a timeout, or a network error logs `DeliverySendThrew`. The claim's 2-minute lease runs out and a later poll tries again. A failed poll logs `DeliveryPollFailed`.

`DeliverySweep` deletes delivery rows 30 days after they were created, hourly, in batches of 1,000.

`WebhookRejected` is a call to `POST /api/v1/webhooks/brevo` with a missing or bad webhook token. It got a 401. `reason` is `missing`, `malformed`, `expired`, `notYetValid`, `invalidSignature`, `invalidAlgorithm`, or `invalidAudience`. A token with no `nbf` is `malformed`. A run of them means a broken token rotation, or someone probing.

`event` on the webhook events is the payload's event in camelCase: `hardBounce`, `spam`, `blocked`, `unsubscribed`, `softBounce`, `delivered`, `deferred`, `request`, `invalidEmail`, `opened`, `uniqueOpened`, `click`, `proxyOpen`, or `uniqueProxyOpen`. Anything else is `other`. `AddressSuppressed` logs a handled event whose `message-id` matched a delivery to the same address. `alreadySuppressed: true` means a Brevo retry or a repeat that wrote nothing new. `WebhookEventUnmatched` has `reason` `unknownMessageId`, `addressMismatch`, or `missingMessageId`, and still gets a 204. A real event always matches unless its row is gone, so an unmatched one is a forger holding a valid token, an event that outlived its 30-day delivery row, or a bug. `WebhookEventIgnored` is an event the webhook doesn't handle, or a body that isn't JSON.

A `Laneway.Message` meter counts each delivery once, when it ends. Locally, `dotnet-counters monitor --counters Laneway.Message` reads it.

| Counter | Tags |
|---|---|
| `laneway.message.deliveries` | `result`: `sent`, `failed`, `expired`, or `dropped`. `channel`: `email` |
| `laneway.message.suppressions` | `event`: `hardBounce`, `spam`, `blocked`, or `unsubscribed`. It counts new suppressions only, not repeats. |

The unsubscribe link in an invite email writes the same `unsubscribed` suppression a webhook event does, with `unsubscribe-link` in place of a Brevo `message-id`. It adds to `laneway.message.suppressions` and logs no event, since no delivery matches it.

## Background jobs and cleanup

Sweepers, drainers, copy jobs, and attachment cleanup log under their class name in the `Laneway.Api` namespace, such as `Laneway.Api.AuthSweeper`. Each category numbers its own events from 1. None of them carries a `sub`, an address, or a token.

| Category | Id | Event | Level | Fields |
|---|---|---|---|---|
| `Laneway.Api.AuthSweeper` | 1 | `SweepFailed` | Warning | the exception |
| `Laneway.Api.SessionSweeper` | 1 | `SweepFailed` | Warning | the exception |
| `Laneway.Api.InviteSweeper` | 1 | `SweepFailed` | Warning | the exception |
| `Laneway.Api.PendingObjectDrainer` | 1 | `ObjectsAlreadyGone` | Information | `Missing`, `Batch` |
| `Laneway.Api.PendingObjectDrainer` | 2 | `DrainFailed` | Warning | the exception |
| `Laneway.Api.ObjectCopies` | 1 | `CleanupFailed` | Warning | `Count`, and the exception |
| `Laneway.Api.AttachmentService` | 1 | `CleanupFailed` | Warning | `ObjectKey`, and the exception |
| `Laneway.Api.CopyJobWorker` | 1 | `PollFailed` | Warning | the exception |
| `Laneway.Api.CopyJobWorker` | 2 | `CopyJobThrew` | Error | `JobId`, `Attempt`, and the exception |
| `Laneway.Api.CopyJobClaims` | 1 | `CopyJobGaveUp` | Error | `JobId`, `Attempts` |
| `Laneway.Api.ListCopyRunner` | 1 | `CopyBodyUnreadable` | Error | `JobId`, and the exception |
| `Laneway.Api.BoardCopyRunner` | 1 | `CopyBodyUnreadable` | Error | `JobId`, and the exception |

A failed sweep, drain, or poll keeps the batches it already finished, and the next run picks up what is left. `ObjectsAlreadyGone` counts pending deletes whose object was already gone from storage. The drainer treats them as done. Both `CleanupFailed` events leave the objects to the pending delete outbox. The `AttachmentService` one fires during the request, after a failed create. `CopyJobThrew` fails the job once it threw on its last attempt. `CopyJobGaveUp` is a job claimed again after its last attempt, which fails with `internalError`. `CopyBodyUnreadable` is a stored copy body missing a field the runner needs, which also fails the job with `internalError`.

## Unexpected errors

`ApiExceptionHandler` logs each exception it turns into a 500 once, at Error, under the `Laneway.Api.ApiExceptionHandler` category as event 1, `UnexpectedError`. Event ids are unique within a category, so this id doesn't clash with `AccessTokenRejected`.

| Id | Event | Level | Fields |
|---|---|---|---|
| 1 | `UnexpectedError` | Error | `traceId`, and the exception |

`traceId` is the request's W3C trace id. The 500 body returns the same `traceId` with a generic message, so a client can quote it and nothing else leaks. The operator pastes it into the trace search and finds the request's spans and every log it wrote. This meets ASVS 16.3.4 and 16.5.1.

`ApiExceptionHandler` doesn't log the foreign-key 400 or the unique-violation 409. They're expected outcomes.

On .NET 9, `ExceptionHandlerMiddleware` logs every exception before the handler runs, so `Program.cs` filters that category out. The filter costs two rarer entries from the same category. One says the response had already started, so the handler never ran. The other carries the exception when `ApiExceptionHandler` itself throws. In both cases Kestrel still logs the original exception. .NET 10 adds `SuppressDiagnosticsCallback`, which mutes only the duplicate, and the upgrade should swap the filter for it.

## Meter

A `System.Diagnostics.Metrics` meter named `Laneway.Auth`. Counter tags hold only small fixed sets, never `sub` or `sid`. Locally, `dotnet-counters monitor --counters Laneway.Auth` reads it.

| Counter | Tags |
|---|---|
| `laneway.auth.sign_in.attempts` | `result`: `succeeded`, `wrong_password`, `unknown_account`, `blocked`, or `unverified` |
| `laneway.auth.sign_in.blocks` | none |
| `laneway.auth.password_check.failures` | `endpoint`: `change_password`, `change_email`, `delete_user`, or `undo_email_change`. `blocked`: `true` or `false` |
| `laneway.auth.breached_check.unavailable` | none |
| `laneway.auth.refresh` | `result`: `rotated`, `superseded`, `reused`, or `rejected` |
| `laneway.auth.csrf.rejections` | `check`: `header`, `origin`, or `fetch_site` |
| `laneway.auth.rate_limit.rejections` | `policy`: one of the [rate limit](rate-limits.md) policies |

`laneway.auth.sign_in.attempts` includes successes, so the failure-to-success ratio is visible. A climbing ratio is the usual sign of credential stuffing. `blocked` counts attempts made during a block, and `sign_in.blocks` counts the blocks themselves.

## Never logged

- A password, in any form.
- The email typed into sign-in, raw or hashed. People type passwords into the email field.
- A refresh token, or its hash.
- Any part of an access token. A `kid` is a key name, not part of the secret, and follows the rule below.
- A user agent.
- The Pwned Passwords request URI. It ends in the first 5 hex characters of the password's SHA-1, so the `BreachedPasswords` client has its HttpClient request logs turned off. Its span ships `url.full` as `https://api.pwnedpasswords.com/range/{prefix}`.
- The raw request path. `http.route` names the endpoint instead.
- The query string.
- Request or response headers.
- A request or response body.
- Postgres error detail. It quotes row values.
- The database connection string.
- An email address in any form, raw, hashed, or masked, including a delivery's recipient.
- A message token, a link built from one, or any rendered subject or body.
- The Brevo API key, or the Brevo webhook token.
- Brevo's error `message`. It's free text and may quote the address.
- A webhook payload's `email`, `message-id`, `reason`, `subject`, `tags`, `X-Mailin-custom`, `sending_ip`, `user_agent`, or `link`. They're from outside, and a bounce reason often quotes the address. A matched event logs our own `deliveryId` instead of the `message-id`.
- The Brevo request body. The `BrevoEmail` client has its HttpClient request logs turned off.

## Enforcement

Every environment runs the same pipeline, so Development ships what production ships.

- `CanaryTests` plants a password, a typed email, a user agent, both tokens, and a path segment across the main flows. It fails if any of them reaches an exported log, span, or metric.
- The allowlist check runs as every API test ends. It fails the test when a log or a `Laneway.*` counter ships a category, event, field, or tag this file doesn't list. It reads this file's tables, so a reshaped table fails the run with the section's name.
- `SpanScrub` keeps the user agent, the connection string, the raw path, the query string, and exception text off spans. It drops `user_agent.original`, Npgsql's `db.connection_string`, the server span's `url.path` and `url.query`, and every status description. It rewrites the Pwned Passwords `url.full` to its template. When a span has an `exception` event and no `error.type`, it copies the event's `exception.type` into `error.type`.
- `RequestScopeScrub` keeps the raw path off log events by dropping ASP.NET Core's `RequestPath` and `RequestId` scope fields. `ConnectionId` stays.
- The `Include Error Detail` check stops the app at startup when `ConnectionStrings:DefaultConnection` sets it. That keeps Postgres's DETAIL field, which quotes row values, out of errors.
- `BannedSymbols.txt` keeps unparameterized SQL and EF's parameter values out of spans and logs. It fails the API build on `FromSqlRaw`, `ExecuteSqlRaw`, `ExecuteSqlRawAsync`, `SqlQueryRaw`, and `EnableSensitiveDataLogging`, since spans keep `db.statement`. The test project may use raw SQL.

Our instrumentation never calls `RecordException`. Npgsql 9 still adds an `exception` event to a failed database span, with Postgres's primary message and a stack trace. No setting or processor can remove it. The `Include Error Detail` check keeps Postgres's DETAIL field out of that message. A primary message can still quote an input value, such as `invalid input syntax for type uuid`. Npgsql sends typed parameters, so that error is rare.

## Request values

No field carries a value from the request unless the value is checked against a known set first. The one exception is `clientIp`, an address Kestrel parsed from the connection or `ForwardedHeadersMiddleware` parsed from a trusted proxy's header. `kid` on `AccessTokenRejected` is logged only when it matches a key in `Auth:SigningKeys`. `code` on `DeliveryFailed` is logged only when it matches one of Brevo's documented error codes. `event` on the webhook events is logged only after it matches Brevo's event list, and as `other` otherwise. A webhook's `message-id` is never logged. A matched event logs our own `deliveryId` instead. Every other field comes from the server: ids from the database, and reasons from fixed lists.
