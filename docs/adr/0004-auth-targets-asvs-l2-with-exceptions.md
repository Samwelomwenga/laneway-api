# Auth targets ASVS L2, with exceptions

Laneway's auth targets OWASP ASVS 5.0 Level 2 in every chapter. Any L2 item it doesn't meet goes on the list below. Each entry gives the requirement and its level, what Laneway doesn't do, why, the controls that make up for it, and what closes it. A change that relaxes an L2 item adds its entry in the same diff.

L1 was turned down. The auth design already meets L2 items such as 9.2.2, 9.2.3, 7.5.1, and 7.5.2, and an L1 label would let later work drop them without saying so.

The minimum password length is 8, which meets 6.2.1 at every level, so it isn't on the list. 6.2.1 and the Authentication cheat sheet, citing NIST SP 800-63B, ask for 15 when there's no MFA. Laneway chose 8 and leans harder on the common-password and breached-password checks.

## Exceptions

### 6.3.3, MFA (L2)

- What Laneway doesn't do: offer or require a second factor. A password alone signs a user in.
- Why: enrolling and recovering a second factor needs email, and Laneway sends no email yet.
- Mitigating controls:
  - A per-user sign-in block that grows with wrong passwords, and the `sign-in` rate limit by IP (6.1.1, 6.3.1, 2.4.1).
  - The password-checked endpoints share the sign-in block's counter, and the `password-check` rate limit covers them.
  - A new password must pass a top-100k common-password list (6.2.4), a context-word check (6.2.11), and a Have I Been Pwned range lookup (6.2.12).
- What closes it: the MFA effort.

### 6.3.8, sign-up enumeration (L3)

6.3.8 is an L3 item, so it isn't strictly owed at L2. It's listed so the email effort finds it.

- What Laneway doesn't do: hide whether a username or email is taken. Sign-up answers 409 for a taken one.
- Why: without email, sign-up has no way to tell a user their address is taken without telling everyone who tries it.
- Mitigating controls: the `sign-up` rate limit, 10 per hour per IP. It slows harvesting from one IP and doesn't stop a botnet. Sign-in itself resists enumeration with one 401 for an unknown user or a wrong password, and a dummy hash so the timing matches.
- What closes it: Laneway sending email. Sign-up then answers "check your email" whether the address is taken or not.
