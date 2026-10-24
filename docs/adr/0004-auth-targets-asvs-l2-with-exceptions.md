# Auth targets ASVS L2, with exceptions

Laneway's auth targets OWASP ASVS 5.0 Level 2 in every chapter. Any L2 item it doesn't meet goes on the list below. Each entry gives the requirement and its level, what Laneway doesn't do, why, the controls that make up for it, and what closes it. A change that relaxes an L2 item adds its entry in the same diff.

L1 was turned down. The auth design already meets L2 items such as 9.2.2, 9.2.3, 7.5.1, and 7.5.2, and an L1 label would let later work drop them without saying so.

The minimum password length is 8, which meets 6.2.1 at every level, so it isn't on the list. 6.2.1 and the Authentication cheat sheet, citing NIST SP 800-63B, ask for 15 when there's no MFA. Laneway chose 8 and leans harder on the common-password and breached-password checks.

## Exceptions

### 6.3.3, MFA (L2)

- What Laneway doesn't do: offer or require a second factor. A password alone signs a user in.
- Why: the MFA effort owns enrolment and recovery. Laneway sends email now, but nothing in it enrols or recovers a second factor.
- Mitigating controls:
  - A per-user sign-in block that grows with wrong passwords, and the `sign-in` rate limit by IP (6.1.1, 6.3.1, 2.4.1).
  - The password-checked endpoints share the sign-in block's counter, and the `password-check` rate limit covers them.
  - A new password must pass a top-100k common-password list (6.2.4), a context-word check (6.2.11), and a Have I Been Pwned range lookup (6.2.12).
- What closes it: the MFA effort.

### 6.3.8, enumeration (L3)

6.3.8 is an L3 item, so it isn't strictly owed at L2. It stays listed because some answers still show whether an account exists.

- Closed: sign-up by email. Past the username check, sign-up always answers 202 with no session. A new address, an unverified user's address, and a verified user's address all get the same reply, and the taken path still runs the password policy and the hash.
- What Laneway doesn't do: hide whether a username is taken, or whether another user has an email in two places. Sign-up and the username checker answer 409 or `false` for a taken username. Change email, and its confirm link, answer 409 when a verified user has the new email. An unverified user's address doesn't count as taken. Adding a member by email answers 400 `notFound` when no verified user has the address.
- Why: usernames are public handles, so a taken one isn't a secret. Change email keeps its 409 because only a signed-in user who knows their password can ask.
- Mitigating controls: the `sign-up` limit of 10 per hour per IP and the `username-check` limit of 30 per minute per IP cap username harvesting. The checker looks up usernames only, never emails. Change email needs an access token and the current password, and the `password-check` limit of 5 per minute by `sub` caps it. Adding a member needs an access token and the right to add members to a place, and the `add-member` limit of 20 per minute by `sub` caps it. Forgot password runs under the `email-request` limit of 10 per hour per IP. Sign-in resists enumeration with one 401 for an unknown user or a wrong password, and a dummy hash so the timing matches. The 403 for an unverified user only comes with the right password.
- Closed: forgot password. It always answers 202 with the same body, for an unknown address, an unverified user, a suppressed address, a capped user, and a verified owner. The leftover gap is timing: a verified owner costs one token insert and one delivery insert. The user accepted that gap over padding every reply to match.
- What closes it: nothing planned. The username and change-email answers stay as accepted risk under the limits above.

### 13.2.1, short-lived service credentials (L2)

- What Laneway doesn't do: give the Brevo webhook a short-lived credential. Its bearer is a JWT that lasts a year.
- Why: Brevo sends only a static header and doesn't sign requests.
- Mitigating controls:
  - The JWT has its own key, `BREVO__WEBHOOKSIGNINGKEY`, never the access-token key. Its audience is `brevo-webhook`, only HS256 passes, and the endpoint checks `exp` and `nbf` (9.1.1 to 9.1.3, 9.2.1 to 9.2.4).
  - The key lives in Doppler (13.3.1, 13.3.2). The yearly `exp` gives the L3 rotation in 13.3.4. Changing the key kills every token at once.
  - An event suppresses an address only when its `message-id` matches a delivery to that address. A leaked token can only suppress addresses Laneway already emailed.
  - The `webhook` rate limit, 600 per minute by IP, covers the route.
- What closes it: Brevo signing webhooks, or fetching each event back from Brevo's events API before suppressing.
