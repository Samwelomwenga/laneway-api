# Logging inventory

What Laneway logs about auth, and what it never logs. This meets ASVS 16.1.1. A change that adds an auth event or counter updates this file in the same diff.

## Sink

Every environment writes JSON to the console, with UTC ISO 8601 timestamps. The JSON writer escapes newlines and quotes in every value, so a field can't forge a second entry. Retention is 30 days, the longest a session lives, and the deploy enforces it.

## Events

Auth events log under the `Laneway.Auth` category. Each has a fixed event id. Every entry also carries the route and the time.

| Id | Event | Level | Fields |
|---|---|---|---|
| 1 | `AccessTokenRejected` | Information | `reason`, and `kid` when it names a configured key, otherwise `kidKnown: false` |

`reason` is one of `malformed`, `expired`, `unknownKey`, `invalidSignature`, `invalidAlgorithm`, `invalidType`, `invalidIssuer`, `invalidAudience`, `sessionEnded`, or `invalid`.

## Meter

A `System.Diagnostics.Metrics` meter named `Laneway.Auth`. Counter tags hold only small fixed sets, never `sub` or `sid`. Locally, `dotnet-counters monitor --counters Laneway.Auth` reads it.

No counters yet.

## Never logged

- A password, in any form.
- The email typed into sign-in, raw or hashed. People type passwords into the email field.
- A refresh token, or its hash.
- Any part of an access token. A `kid` is a key name, not part of the secret, and follows the rule below.
- A client IP address or user agent.

## Request values

No field carries a value from the request unless the value is checked against a known set first. `kid` on `AccessTokenRejected` is logged only when it matches a key in `Auth:SigningKeys`. Every other field comes from the server: ids from the database, and reasons from fixed lists.
