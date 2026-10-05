# Activity entries hold comments and outlive their items

A comment is an activity entry of type `comment`, stored in `ActivityEntries` with every other entry, the way Trello stores a comment as a `commentCard` action. Entries point at their workspace, board, list, and card by plain ids with no foreign keys, so deleting any of those leaves its entries in place. We chose this over a separate `Comment` table and over entries that cascade with their item, so a card's comments and history read through one route and stay after the card is deleted.

## Consequences

- The log is not append-only. An edit changes a comment entry's `Text` and `UpdatedAt`, a delete removes the entry, and every rule about entries has to allow for that.
- Postgres doesn't stop an entry from naming an id that never existed. Only service code and tests keep the ids right.
- Every entry copies the names it shows into `Data`, the actor's name included, because those rows can disappear or be hidden.
- A deleted card's comments stay in the database. Nothing cascades, so removing them later means choosing which entries to delete by hand.
- Deleting a user doesn't remove their name from their entries.
