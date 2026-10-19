# Hand-written authorization in the services

Laneway checks permissions in its own code, against membership tables in its one Postgres database. It uses no policy engine and no ASP.NET Core authorization handlers. A `Readable` service holds one LINQ expression per entity for "this user can read it". Every lookup by id and every list starts from it, so a single rule decides both the 404 and the list filter. Services load the object and the caller's standing on its board in that one query, then a pure function per scope picks the 403 code. `[Authorize]` stays the only endpoint policy, and it means signed in.

## Considered options

- OpenFGA. It models workspace to board inheritance well, but it needs its own server and database, and every membership change becomes a dual write. ListObjects caps at 1000 results.
- OPA. Its Compile API can turn policy into a Postgres `WHERE`, but nobody showed it handles "board or its workspace", and membership data has to be synced in.
- Cedar. It has no official .NET binding.
- ASP.NET Core resource-based handlers. Endpoint policies run before the action loads the object, so they would need a second lookup, and a handler only passes or fails. Our per-reason 403 codes and the fixed order of 404, 403, body ids, business rules would split across two layers.
- EF global query filters. They need the actor inside the DbContext, EF 9 allows one per entity so they would merge with soft delete, and every background job would need `IgnoreQueryFilters`.
- Roles in the JWT. A removed member would keep editing for up to 15 minutes, a few hundred boards overflow header limits, and every request already reads the session row anyway.

## Consequences

- Roles come from the database on every request. The access token carries only `sub` and `sid`.
- Nothing caches permission checks. A cache waits for a measured slow endpoint, and it would bring back the staleness that ruled out JWT roles.
- `Readable` takes a user id, not the request's actor, so copy jobs re-check the starter's rights through the same rules.
- A service that reads a `DbSet` directly skips authorization. Tests have to catch that, since no framework layer will.
- The service that decides a denial puts it on a bounded channel, and a hosted drainer logs it. A hiding 404 costs the caller no more than a missing one.
