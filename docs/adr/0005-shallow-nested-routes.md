# Shallow nested routes

Collections and creates sit under their parent, as in `GET /workspaces/{workspaceId}/boards` and `POST /lists/{listId}/cards`. One item keeps its own short URL, as in `GET /cards/{id}` and `PUT /attachments/{id}`, so a card's URL doesn't change when it moves. Every collection read needs its parent, and no search crosses parents. This reverses an earlier spec, which kept routes flat so clients saw one routing style and nested only attachments and comment writes. With nesting as the rule, those two became the exceptions, so they moved to short URLs like everything else.

## Considered options

- Nesting the full chain, as in `/workspaces/{wid}/boards/{bid}/lists/{lid}/cards/{id}`. URLs get long, and every move breaks them.
- Nesting one parent up for single items, as in `/lists/{listId}/cards/{id}`. That breaks on a move too.
- Keeping `GET /cards` as one search across lists for due dates. A view like "due this week" will get its own endpoint instead.

## Consequences

- The activity log and copy jobs stay flat. Log entries and jobs outlive the items they name, and a nested route would answer 404 once that item is deleted.
- Move and copy bodies keep the destination parent id. It says where the item goes, not where it lives.
- A parent id in the path comes out of the query and the create body. Sending it anyway is a 400.
