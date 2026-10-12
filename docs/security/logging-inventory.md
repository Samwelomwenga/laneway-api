# Logging inventory

What Laneway logs about auth and unexpected errors, and what it never logs. This meets ASVS 16.1.1. A change that adds an auth event or counter updates this file in the same diff.

## Sink

Every environment writes JSON to the console, with UTC ISO 8601 timestamps. The JSON writer escapes newlines and quotes in every value, so a field can't forge a second entry. Retention is 30 days, the longest a session lives, and the deploy enforces it.

## Events

Auth events log under the `Laneway.Auth` category. Each has a fixed event id. Every entry also carries the route and the time.

| Id | Event | Level | Fields |
|---|---|---|---|
| 1 | `AccessTokenRejected` | Information | `reason`, and `kid` when it names a configured key, otherwise `kidKnown: false` |
| 2 | `SignUp` | Information | `sub`, `sid` |
| 3 | `SignInSucceeded` | Information | `sub`, `sid` |
| 4 | `SignInFailed` | Warning | `sub` when the email belongs to a user, otherwise `accountFound: false` |
| 5 | `BreachedCheckUnavailable` | Warning | the exception from the Pwned Passwords call |
| 6 | `SessionRefreshed` | Information | `sub`, `sid`, and `refreshTokenId` of the new token |
| 7 | `RefreshSuperseded` | Information | `sub`, `sid` |
| 8 | `RefreshTokenReused` | Warning | `sub`, `sid`, and `refreshTokenId` of the reused token |
| 9 | `RefreshRejected` | Information | `reason`, and `sid` when the token was found |
| 10 | `SessionEnded` | Information | `sub`, `sid`, `endReason` |
| 11 | `CsrfRejected` | Warning | `check` |

`reason` is one of `malformed`, `expired`, `unknownKey`, `invalidSignature`, `invalidAlgorithm`, `invalidType`, `invalidIssuer`, `invalidAudience`, `sessionEnded`, or `invalid`.

`RefreshRejected` has `reason` `missingCookie`, `unknownToken`, or `sessionEnded`. `sessionEnded` covers a session that ended, passed its 30-day limit, sat idle for 7 days, or belongs to a deleted user.

`RefreshSuperseded` is a refresh that lost a race inside the 10-second grace window. It ends nothing. `RefreshTokenReused` is a retired token back after that window, and a `SessionEnded` with `endReason` `TokenReused` follows it.

`SessionEnded` logs when an action ends a session. A session that runs out logs nothing, because nothing happens at that moment. `endReason` is `SignedOut`, `SignedOutEverywhere`, `EndedFromSessionList`, or `TokenReused`. Sign-out everywhere logs one entry per session it ends. Ending a session that already ended logs nothing.

`CsrfRejected` is a sign-up, sign-in, or refresh that failed a CSRF check. `check` names the first one that failed: `header` when `X-Laneway-Csrf: 1` is missing, `origin` when `Origin` isn't in `Cors:AllowedOrigins`, or `fetch-site` when `Sec-Fetch-Site` is `cross-site`. The 403 body is the same for all three.

`BreachedCheckUnavailable` means the Pwned Passwords check failed or took over 2 seconds, so the password was accepted without it. Each entry is one password that skipped ASVS 6.2.12.

## Unexpected errors

`ApiExceptionHandler` logs each exception it turns into a 500 once, at Error, under the `Laneway.Api.ApiExceptionHandler` category as event 1, `UnexpectedError`. Event ids are unique within a category, so this id doesn't clash with `AccessTokenRejected`. The entry carries the exception and `traceId`, the request's `TraceIdentifier`. The 500 body returns the same `traceId` with a generic message, so a client can quote it and nothing else leaks. This meets ASVS 16.3.4 and 16.5.1.

`ApiExceptionHandler` doesn't log the foreign-key 400 or the unique-violation 409. They're expected outcomes.

On .NET 9, `ExceptionHandlerMiddleware` logs every exception before the handler runs, so `Program.cs` filters that category out. The filter costs two rarer entries from the same category. One says the response had already started, so the handler never ran. The other carries the exception when `ApiExceptionHandler` itself throws. In both cases Kestrel still logs the original exception. .NET 10 adds `SuppressDiagnosticsCallback`, which mutes only the duplicate, and the upgrade should swap the filter for it.

## Meter

A `System.Diagnostics.Metrics` meter named `Laneway.Auth`. Counter tags hold only small fixed sets, never `sub` or `sid`. Locally, `dotnet-counters monitor --counters Laneway.Auth` reads it.

| Counter | Tags |
|---|---|
| `laneway.auth.sign_in.attempts` | `result`: `succeeded`, `wrong_password`, or `unknown_account` |
| `laneway.auth.breached_check.unavailable` | none |
| `laneway.auth.refresh` | `result`: `rotated`, `superseded`, `reused`, or `rejected` |
| `laneway.auth.csrf.rejections` | `check`: `header`, `origin`, or `fetch_site` |

`laneway.auth.sign_in.attempts` includes successes, so the failure-to-success ratio is visible. A climbing ratio is the usual sign of credential stuffing.

## Never logged

- A password, in any form.
- The email typed into sign-in, raw or hashed. People type passwords into the email field.
- A refresh token, or its hash.
- Any part of an access token. A `kid` is a key name, not part of the secret, and follows the rule below.
- A client IP address or user agent.
- The Pwned Passwords request URI. It ends in the first 5 hex characters of the password's SHA-1, so the `BreachedPasswords` client has its HttpClient request logs turned off.

## Request values

No field carries a value from the request unless the value is checked against a known set first. `kid` on `AccessTokenRejected` is logged only when it matches a key in `Auth:SigningKeys`. Every other field comes from the server: ids from the database, and reasons from fixed lists.
