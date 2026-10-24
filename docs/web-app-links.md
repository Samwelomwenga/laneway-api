# Web app links

Every link Laneway puts in an email points at a page in the web app. This file lists those pages, so the web app knows which routes to build and what each one calls.

`WebApp:BaseUrl` (`WEBAPP__BASEURL`) is the origin every link starts with. `AppRoutes` in `Features/Message/` holds the paths.

| Route | Fragment | The page calls | Sent in |
|---|---|---|---|
| `/verify-email` | `token` | `POST /api/v1/auth/verify-email {token}` | `VerifyEmail` |

## Rules for pages with a token

- The token goes in the fragment, as `/verify-email#token=…`. A fragment never reaches a server, so the token stays out of access logs and proxies.
- The page reads the token, then clears the fragment with `history.replaceState` before doing anything else.
- The page calls the API only after the user clicks a button. Mail scanners open links to check them, and a call on page load would spend the token.
- The page sets `Referrer-Policy: no-referrer`.
- The verify call answers like sign-in, with an access token and the refresh cookie, so the page signs the user in. A 400 `invalidToken` means the link was used, replaced by a newer one, or more than 24 hours old.
