# Web app links

Every link Laneway puts in an email points at a page in the web app. This file lists those pages, so the web app knows which routes to build and what each one calls.

`WebApp:BaseUrl` (`WEBAPP__BASEURL`) is the origin every link starts with. `AppRoutes` in `Features/Message/` holds the paths.

| Route | Fragment | The page calls | Sent in |
|---|---|---|---|
| `/verify-email` | `token` | `POST /api/v1/auth/verify-email {token}` | `VerifyEmail` |
| `/forgot-password` | none | `POST /api/v1/auth/forgot-password {email}` | `PasswordChanged` |
| `/reset-password` | `token` | `POST /api/v1/auth/reset-password {token, newPassword}` | `ResetPassword` |
| `/confirm-email-change` | `token` | `POST /api/v1/auth/confirm-email-change {token}` | `ConfirmEmailChange` |

## Rules for pages with a token

- The token goes in the fragment, as `/verify-email#token=…`. A fragment never reaches a server, so the token stays out of access logs and proxies.
- The page reads the token, then clears the fragment with `history.replaceState` before doing anything else.
- The page calls the API only after the user clicks a button. Mail scanners open links to check them, and a call on page load would spend the token.
- The page sets `Referrer-Policy: no-referrer`.
- The verify call answers like sign-in, with an access token and the refresh cookie, so the page signs the user in. A 400 `invalidToken` means the link was used, replaced by a newer one, or more than 24 hours old.
- The reset call answers 204 and signs nobody in, so the page sends the user to sign in with the new password. A 400 `invalidToken` means the link was used, replaced by a newer one, or more than 1 hour old. Any other 400 is the password policy refusing `newPassword`. The link still works, so the page lets the user pick another.
- The confirm call answers 204 and signs nobody in. Sessions that were signed in stay signed in, so the page tells the user to use the new address next time. A 400 `invalidToken` means the link was used, replaced by a newer request, cancelled by a password reset, or more than 24 hours old. A 409 `duplicate` means another account proved that address after the request, and nothing changed.

## The forgot password page

The page takes an email and always gets a 202, whether or not an account uses that address. It shows the same "check your email" message every time.
