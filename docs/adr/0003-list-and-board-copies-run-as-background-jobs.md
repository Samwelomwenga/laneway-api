# List and board copies run as background jobs

A list or board copy returns 202 with a copy job, which the client polls at `GET /api/v1/copy-jobs/{id}`. A card copy runs in the request and returns 201. Nothing caps the cards on a list or board, and every copied file is one storage call, so a big copy can run for minutes. That's longer than a request should wait or hold a transaction. A card holds at most 100 files, and "duplicate this card" is the copy a UI makes most, so card copies stay synchronous.

## Considered options

- Every copy in the request, capped at 500 cards and 500 files. A board over the cap would take a labels-only board copy and then one list copy per list.
- Every copy in the request with no cap. One request could run for minutes and time out after doing most of the work.
- Card copies as jobs too. That gives one response style, but a duplicate click becomes a POST, a poll, and a GET.

## Consequences

- Clients handle two response styles, and the route decides which one. `/cards/{id}/copies` always returns 201, and `/lists/{id}/copies` and `/boards/{id}/copies` always return 202.
- The API runs a job worker inside its own process. Workers claim jobs with Postgres session advisory locks, which Supabase's transaction-mode pooler on port 6543 drops. A deployment needs a direct connection or the session-mode pooler.
- Every check a synchronous copy runs happens before the 202, and again inside the job, because the source or destination can change while the job waits.
- Making list or board copies synchronous later breaks every client that polls.
