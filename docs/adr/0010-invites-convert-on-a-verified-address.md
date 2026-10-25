# Invites convert on a verified address, with no token

Adding someone by an email that no verified user has makes an invite instead of a member. The invite email carries no token. It links to sign-up with the address filled in. The invite becomes a membership when a user proves they own that exact address, either by using the sign-up verification link or by confirming a change to it. Every pending invite for the address converts in the same transaction. Signing up with another address attaches nothing, so the person asks the inviter to invite that address instead.

Adding by email still tells the inviter whether the address has an account. A verified user comes back `active`, and anyone else `invited`. Laneway accepts that. Only someone who may add members can ask, and the `add-member` limit plus a cap of 50 invite emails per inviter per 24 hours bound it. ASVS 6.3.8 is L3, and Laneway targets L2.

## Considered options

- A tokened accept link that any signed-in user can use. It lets someone forward an invite to a different person, which defeats inviting an address. It also needs a token table, an accept endpoint, and enumeration-safe timing on that endpoint, and a mail scanner that prefetches links could spend it.
- A placeholder user that joins the place at once, the way the kanban reference does it. Every query that assumes a user signed up would have to skip it, including the unverified-user sweeper, username uniqueness, and activity.
- Making every email add an invite, even for existing users, to hide whether the address has an account. Existing users would then need an accept step, which is the token this decision avoids.

## Consequences

- Whoever controls a mailbox gets every invite sent to it, even if they never meant to join. That holds for any email-based invite.
- A user who changes to an invited address joins those places. Undoing that email change doesn't undo the memberships.
- Member lookup ignores unverified users, so a squatter's unverified row can never be added to a place.
- ADR 0004's line on member lookup by email becomes an accepted exception. It used to answer 400 `notFound` for an address with no verified user, and now it answers 201 `invited`.
