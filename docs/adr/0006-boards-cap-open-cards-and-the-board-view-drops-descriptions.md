# Boards cap open cards, and the board view drops descriptions

A board holds at most 5,000 open cards, 500 open lists, and 1,000 labels, and a card carries at most 100 labels. Cards in `GET /boards/{id}` carry `hasDescription` instead of `description`, and the full text comes from `GET /cards/{id}`. The board view returns a whole board in one response with no paging, so before these rules nothing bounded its size. A user with write access could build a board that answers with any number of megabytes, then fetch it 300 times a minute.

We measured the board view's query and serialization on seeded boards in Postgres 18:

| Board | Cards | Description | Query | Serialize | JSON | Allocated |
|---|---|---|---|---|---|---|
| Typical | 200 | 200 chars | 8 ms | 3 ms | 0.16 MB | 1 MB |
| Large | 4,750 | 300 chars | 95 ms | 62 ms | 4.4 MB | 21 MB |
| Full descriptions | 5,000 | 10,000 chars | 293 ms | 51 ms | 53 MB | 171 MB |

At the caps, without descriptions, the worst case is about 26 MB. Most of that is 100 label ids on each of 5,000 cards. A real board stays under 1 MB.

## Considered options

- No bound. Nothing capped cards per board, since `CardsPerCopy` only limits one copy, so the worst case had no ceiling.
- Paging the board view, as Jira's board API does with 50 issues a page. Drawing a board would take many calls, which the one-call board view exists to avoid.
- Lowering the description limit. At 2,000 characters a capped board still sends about 10 MB, and clients that saved longer text would break.
- A description preview on board cards. It brings the cost of a second card shape, and no board screen draws description text.
- No cap on labels per card. The worst case would go back to about 200 MB.
- Response compression. It affects every endpoint and the deployment, and over HTTPS it raises BREACH for responses that carry tokens. We left it for later.

## Consequences

- The board view's cards are a `BoardCardDto`, not the `CardDto` that `GET /cards/{id}` and card writes return. This reverses the one-card-shape choice from the nested read shapes decision.
- Open means a card or list that isn't archived and isn't inside an archived list. Unarchiving counts against the caps, so a big archived list may not come back until the board has room.
- Every write that adds open cards, open lists, or labels to a board checks the caps. That covers create, move, copy, unarchive, and the labels a card brings to another board. The API refuses a write over a cap with 409 `limitReached` and does none of it. Copy jobs check before the 202 and again inside the job, as ADR 0003 requires.
- The checks lock the board row before counting, so two writes in different lists can't both fit under the cap. Locks go board, then list, then card, or two writes can deadlock.
- Raising a cap later is safe. Lowering one strands boards already over it.
