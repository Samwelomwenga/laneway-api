# Logging inventory

What Laneway logs about requests, auth, and unexpected errors, and what it never logs. This meets ASVS 16.1.1. A change that adds an event, a field, or a counter updates this file in the same diff.

## Sink

The API ships logs over OTLP, with traces and metrics, through one exporter. Only startup failures and Serilog's own errors go to stderr. OTLP carries each field as its own attribute, so a value can't forge a second entry. Retention is at most 30 days, the longest a session lives. Grafana's free tier keeps 14 until Laneway moves to Pro.

## Backend

Production ships to one Grafana Cloud stack, `lanewayapi`, on the free tier, in the EU region `prod-eu-west-2`, per [ADR 0012](../adr/0012-grafana-cloud-for-telemetry-without-sentry.md). Local runs ship to the Aspire Dashboard. [The Grafana runbook](../runbooks/grafana.md) has the setup steps.

- The app's token holds `logs:write`, `traces:write`, and `metrics:write` only. It can't read or delete. The OTLP endpoint and the token live in Doppler, project `laneway-api`, config `prd`, never in appsettings.
- No standing token holds `logs:delete`. The operator makes one only to purge a never-logged item that leaked, then revokes it.
- Readers are named people, each with MFA. There is no shared login. The free tier allows 3 users.
- Pro, at $19 a month, keeps 30 days. Laneway moves to Pro on the first paying customer, or on the first incident that needed older logs.
- The .NET `System.Runtime` meter ships too, so production sends metrics every 60 seconds at zero traffic. The [logging stopped](../runbooks/alerts/logging-stopped.md) alert fires when they stop.
- A 500 always shows as an `UnexpectedError` line. Application Observability's Exceptions tab lists only spans with an `exception` event, which only Npgsql adds. Sentry isn't used.
- The free tier doesn't record who read what, and tamper evidence is access control, not cryptographic. [ADR 0004](../adr/0004-auth-targets-asvs-l2-with-exceptions.md#1642-logs-protected-from-access-and-edits-l2) lists both as exceptions.

## Alerting

Grafana alerts the operator from these logs and counters. OWASP A09 asks for alert thresholds and a playbook per alert. Every rule but log usage reads production alone. Every threshold is a starting guess, retuned once real traffic exists. [The alerting runbook](../runbooks/alerting.md) has the queries and the thresholds.

| Rule | Source | Tier | Playbook |
|---|---|---|---|
| Sign-in failure ratio | `laneway.auth.sign_in.attempts` | notify | [sign-in-failure-ratio.md](../runbooks/alerts/sign-in-failure-ratio.md) |
| Refresh token reused | `laneway.auth.refresh{result=reused}` | page | [refresh-token-reused.md](../runbooks/alerts/refresh-token-reused.md) |
| Delivery failed at Error | `Laneway.Message` `DeliveryFailed` at Error | page | [delivery-failed.md](../runbooks/alerts/delivery-failed.md) |
| WebhookRejected run | `Laneway.Message` `WebhookRejected` | notify | [webhook-rejected-run.md](../runbooks/alerts/webhook-rejected-run.md) |
| One clientIp burst | `Laneway.*` Warnings by `clientIp` | notify | [client-ip-burst.md](../runbooks/alerts/client-ip-burst.md) |
| UnexpectedError | `Laneway.Api.ApiExceptionHandler` `UnexpectedError` | page | [unexpected-error.md](../runbooks/alerts/unexpected-error.md) |
| Unknown value or field | `Laneway.Request` `RequestCompleted` with `unknownValue` or `unknownField` in `errorCodes` | notify | [unknown-value-or-field.md](../runbooks/alerts/unknown-value-or-field.md) |
| Id scan | `laneway.authorization.denials.unknown_target` | notify | [id-scan.md](../runbooks/alerts/id-scan.md) |
| Admin granted | `Laneway.Access` `MemberAdded` or `MemberRoleChanged` with `role` Admin | notify | [admin-granted.md](../runbooks/alerts/admin-granted.md) |
| Made public | `Laneway.Access` `VisibilityChanged` to Public | notify | [made-public.md](../runbooks/alerts/made-public.md) |
| Logging stopped | No Data on `dotnet.process.cpu.count` | page | [logging-stopped.md](../runbooks/alerts/logging-stopped.md) |
| Log usage | `bytes_over_time` over the whole stack | notify | [log-usage.md](../runbooks/alerts/log-usage.md) |

Two contact points carry them. `Laneway page` goes to Grafana IRM, which pushes to the operator's phone and emails after 15 minutes without an ack, and to Telegram. `Laneway notify` goes to email and Telegram. A rule whose query fails sends Grafana's `DatasourceError`, which goes to `Laneway notify`.

The rules, the contact points, and the notification policy tree live in `terraform/alerting`. Terraform is the only way to change them. Provenance locks them in the Grafana UI, and the service account token that can write them lives in its own Doppler project, apart from the app's.

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
| 1 | `RequestCompleted` | Error for a 5xx or an unhandled exception, Warning over 1 second, otherwise Information | `RequestMethod`, `route`, `StatusCode`, `Elapsed`, `clientIp` when the request has an address, `sub` and `sid` when a valid access token signed it in, and `errorCodes` on a 4xx with an `ApiError` body |

`RequestMethod` is `GET`, `HEAD`, `POST`, `PUT`, `PATCH`, `DELETE`, `OPTIONS`, `TRACE`, or `CONNECT`, and `other` for anything else. `route` is the endpoint's route template, such as `api/v1/WorkSpaces/{id}`, or `unmatched` when no endpoint matched. A 500 keeps the route of the endpoint that threw. `Elapsed` is in milliseconds. A token that fails, or names an ended session, leaves `sub` and `sid` off. `errorCodes` lists the distinct codes from the body's errors, sorted, such as `["required", "tooLong"]`. It holds only codes from the fixed set in `ErrorCodes`, never a field name, so a failed validation is on record as ASVS 16.3.3 asks without logging what the caller sent.

A 4xx logs at Information. A 404 or 405 gets no event of its own, so this line is its only record.

## The client IP

`clientIp` is the address `ForwardedHeadersMiddleware` resolves, the same one the rate limiter counts. `X-Forwarded-For` counts only when the caller is in `ForwardedHeaders:KnownProxies` or `ForwardedHeaders:KnownNetworks`. With neither set, the socket address stands. The field holds the full address. The rate limiter cuts an IPv6 address to its /64 for its own buckets, and the log doesn't. An IPv4 address mapped into IPv6 logs as plain IPv4. A request with no remote address leaves the field off.

The purpose is network and information security, which GDPR recital 49 names as a legitimate interest. It lets a reviewer trace an attack back to its source. An erasure request doesn't reach logs, and retention removes the address.

It appears on these entries only:

- `RequestCompleted`.
- Every `Laneway.Auth` event logged inside a request, `RateLimited`, `CsrfRejected`, and `UsernameChanged` included.
- `AccessDenied`. The `Denial` record carries it, since `DenialDrainer` logs on another thread.
- `WebhookRejected`.
- Every `Laneway.Access` event logged inside a request.

Delivery events, sweeps, `UnexpectedError`, and the other webhook events never carry it. No counter tags by it.

## Correlation

Every entry logged inside a request carries the request's W3C trace id, and so do its spans. `UseTraceIdentifier` runs first in the pipeline and sets `HttpContext.TraceIdentifier` to that trace id. The 500 body, `UnexpectedError`, and a ProblemDetails body all quote it as `traceId`. ProblemDetails would otherwise quote the whole `traceparent`.

Each request starts a fresh root trace. The API ignores an inbound `traceparent`, `tracestate`, and `baggage`, so a client can't pick its trace id or join someone else's trace. It sends none of them on any outbound HttpClient either, so Brevo and Pwned Passwords learn nothing about our traces. Their client spans still ship. `TraceBoundary` does both by setting the runtime's propagator and OpenTelemetry's to ones that read and write nothing.

`AccessDenied` logs after the response, on `DenialDrainer`'s thread. The `Denial` record carries the request's trace context, and the drainer restores it for the log call only. The entry has the request's trace id and a span id that names no shipped span. The drainer's existence check runs before that, outside the request trace.

Deliveries and copy jobs store their origin's `traceparent`, which is the span that added the row. `DeliveryWorker` starts a `DeliveryAttempt` span under the `Laneway.Message` source for each send attempt, and `CopyJobWorker` starts a `CopyJobAttempt` span under `Laneway.CopyJob`. Each attempt is its own root trace with a link to that origin, so a retry hours later doesn't stretch the request's trace. A row added outside a request has no `traceparent`, and its span has no link. Brevo webhooks are requests with their own traces and no link to a delivery.

Timed workers make a span only when a poll finds work. `DeliverySweep` and `CopyJobSweep` start a root span under their worker's source. `AuthSweeper`, `SessionSweeper`, `InviteSweeper`, and `PendingObjectDrainer` start one under `Laneway.Worker`. The sampler drops any database or HTTP client span with no parent, so an empty poll ships nothing. Its log entries, such as a failed poll, carry no trace id, and the category names the worker.

## Events

Auth events log under the `Laneway.Auth` category. Each has a fixed event id. Every entry also carries the time, and inside a request the route, and `clientIp` when the request has an address.

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
| 20 | `UsernameChanged` | Information | `sub` |
| 21 | `SigningKeysLoaded` | Information | `kids`, `signingKid` |

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

`UsernameChanged` logs a profile edit that changes the username. It never logs the old name or the new one. A put that keeps the name logs nothing. A sign-up that takes over an unverified user logs `SignUp`, not `UsernameChanged`, even when it brings a new username.

`SigningKeysLoaded` logs once at startup. `kids` lists every id in `Auth:SigningKeys`, in order, and `signingKid` is key 0, the one that signs new tokens. Keys change only through a deploy, so this entry dates each rotation. It logs outside any request and has no `clientIp`.

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

A hiding denial on an id that doesn't exist logs nothing, but `DenialDrainer` counts it by `targetType`. An id scan shows up as a climbing count, and the random ids never log.

| Counter | Tags |
|---|---|
| `laneway.authorization.denials.dropped` | none |
| `laneway.authorization.denials.unknown_target` | `targetType`: one of the `AccessDenied` target types |

## Access changes

Membership, invite, visibility, and member-adding settings changes, and workspace and board deletes, log under the `Laneway.Access` category, so an admin action reaches a store the app can't edit. Activity entries record the same changes in the app database. Each event logs after its transaction commits. Every entry also carries `clientIp` inside a request with an address.

| Id | Event | Level | Fields |
|---|---|---|---|
| 1 | `MemberAdded` | Information | `sub`, `targetType`, `targetId`, `memberSub`, `role`, and `inviteId` when an invite made the member |
| 2 | `MemberRoleChanged` | Information | `sub`, `targetType`, `targetId`, `memberSub`, `role`, `previousRole` |
| 3 | `MemberRemoved` | Information | `sub`, `targetType`, `targetId`, `memberSub`, `role` |
| 4 | `InviteSent` | Information | `sub`, `targetType`, `targetId`, `inviteId`, `role` |
| 5 | `InviteRevoked` | Information | `sub`, `targetType`, `targetId`, `inviteId`, `role` |
| 6 | `VisibilityChanged` | Information | `sub`, `targetType`, `targetId`, `visibility`, `previousVisibility` |
| 7 | `BoardSettingsChanged` | Information | `sub`, `targetType`, `targetId`, `settings` |
| 8 | `WorkspaceDeleted` | Information | `sub`, `targetType`, `targetId` |
| 9 | `BoardDeleted` | Information | `sub`, `targetType`, `targetId` |

`sub` is the user who made the change. `targetType` is `Workspace` or `Board`, and `targetId` is its id. `memberSub` is the member the change is about. `role` is the role the member or invite has after the change, and for `MemberRemoved` the role they had. No entry carries the invited email.

Leaving logs `MemberRemoved` with `sub` equal to `memberSub`. A self-join logs `MemberAdded` the same way. Leaving or removing someone from a workspace logs one `MemberRemoved` for the workspace, then one for each board in it they were on, by board name. Setting the role a member already has logs nothing.

`InviteSent` logs each invite to an address with no verified user, a repeat invite that renews a live one included. A revoke that finds no live invite logs nothing.

Verifying an address, or confirming an email change to it, turns its live invites into memberships. Each one logs `MemberAdded` with `sub` as the user who sent the invite, `memberSub` as the new member, the invite's `role`, and its `inviteId`. That matches the activity entry and ADR 0010. The entry carries the invitee's `clientIp` and trace id, since their request made the change. An invite for a place the user is already in is deleted and logs nothing, and conversion never logs `InviteRevoked`.

Creating a workspace or board makes its creator an admin and logs no `MemberAdded`. Deleting a user ends their memberships and logs `UserDeleted` only.

`VisibilityChanged` logs a workspace edit or a board settings change that changes the visibility. `visibility` and `previousVisibility` are `Private`, `Workspace`, or `Public`. A workspace is only ever `Private` or `Public`. A put that keeps the visibility logs nothing.

`BoardSettingsChanged` logs a board settings change that changes who may add members, whether workspace members may join on their own, or both. `settings` lists the names that changed, `addingMembers` and `selfJoin`, never their values. A visibility change in the same put logs its own `VisibilityChanged`. The commenting setting, the other workspace and board edits, and attachment uploads and deletes stay activity entries only.

`WorkspaceDeleted` and `BoardDeleted` log a delete that went through. A board must be archived first, and a workspace must hold no boards, so a refused delete logs nothing.

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
| 12 | `InvitesUnsubscribed` | Information | `alreadySuppressed` |

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

The unsubscribe link in an invite email writes the same `unsubscribed` suppression a webhook event does, with `unsubscribe-link` in place of a Brevo `message-id`. It adds to `laneway.message.suppressions` and logs `InvitesUnsubscribed`. `alreadySuppressed: true` means the address was already unsubscribed, so the use wrote nothing new. The entry has no email, no `deliveryId`, since no delivery matches it, and no `clientIp`. A bad token logs no event, and the request line records its `invalidToken`.

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

On .NET 10, `ExceptionHandlerMiddleware` skips its own log and metrics whenever an `IExceptionHandler` returns true from `TryHandleAsync`. `ApiExceptionHandler` always does, so the middleware logs nothing for a handled exception and no config mutes its category. It still logs at Warning or above in the two cases the handler never finishes. One is a response that has already started. The other is `ApiExceptionHandler` throwing.

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
- `SpanScrub` keeps the user agent, the connection string, the raw path, the query string, and exception text off spans. It drops `user_agent.original`, Npgsql's `db.npgsql.data_source`, the server span's `url.path` and `url.query`, and every status description. It rewrites the Pwned Passwords `url.full` to its template.
- `RequestScopeScrub` keeps the raw path off log events by dropping ASP.NET Core's `RequestPath` and `RequestId` scope fields. `ConnectionId` stays.
- The `Include Error Detail` check stops the app at startup when `ConnectionStrings:DefaultConnection` sets it. That keeps Postgres's DETAIL field, which quotes row values, out of errors.
- The mute check stops the app at startup when `Serilog:MinimumLevel`, `Serilog:MinimumLevel:Default`, an entry under `Serilog:MinimumLevel:Override`, or an entry under `Logging:OpenTelemetry:LogLevel` would drop Information from any `Laneway.*` category. Warning, Error, Fatal, Critical, and None all count, by name or by number. A `*` in an entry counts as reaching every category it could match. A muted default passes when a `Laneway` entry sets Information or lower, and Debug is always allowed. Framework categories stay free to set.
- `BannedSymbols.txt` keeps unparameterized SQL and EF's parameter values out of spans and logs. It fails the API build on `FromSqlRaw`, `ExecuteSqlRaw`, `ExecuteSqlRawAsync`, `SqlQueryRaw`, and `EnableSensitiveDataLogging`, since spans keep `db.query.text`. The test project may use raw SQL.

Our instrumentation never calls `RecordException`. Npgsql still adds an `exception` event to a failed database span, with Postgres's primary message and a stack trace. The span's `error.type` is the SQLSTATE, such as `23505`. No setting or processor can remove it. The `Include Error Detail` check keeps Postgres's DETAIL field out of that message. A primary message can still quote an input value, such as `invalid input syntax for type uuid`. Npgsql sends typed parameters, so that error is rare.

## Request values

No field carries a value from the request unless the value is checked against a known set first. The one exception is `clientIp`, an address Kestrel parsed from the connection or `ForwardedHeadersMiddleware` parsed from a trusted proxy's header. `kid` on `AccessTokenRejected` is logged only when it matches a key in `Auth:SigningKeys`. `code` on `DeliveryFailed` is logged only when it matches one of Brevo's documented error codes. `event` on the webhook events is logged only after it matches Brevo's event list, and as `other` otherwise. A webhook's `message-id` is never logged. A matched event logs our own `deliveryId` instead. Every other field comes from the server: ids from the database, and reasons from fixed lists.
