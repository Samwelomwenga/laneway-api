# Code comments

The house rule for comments, and what a comment review leaves alone.

## Our code

Keep comments lean. A comment earns its place when it explains something the code can't say for itself:

- A surprise forced by a dependency or platform. Npgsql or EF Core behavior, a Postgres constraint rule, a Supabase storage quirk.
- A constraint the type system can't express, with a link to the ADR in `docs/adr/`. Cite it and stop.
- A trap where the compiler accepts the wrong thing. Two ids that are both `Guid`, or a query that leaks unless it's scoped. Name the trap in one sentence.
- A doc comment on a public symbol that defines its contract.

Delete anything else. A comment that restates the line below it or narrates our own control flow is noise.

## Version comments on pinned actions stay

Every `uses:` in `.github/workflows/` pins a full commit SHA and ends with the version it points at:

```yaml
- uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
```

That trailing comment stays. Dependabot reads it to know which release the SHA is and rewrites it when it bumps the pin. Without it, a reader can't tell which version runs.

Workflow files carry no other comments. How to fix a red check lives in `docs/ci.md`. An accepted zizmor finding is the one exception, an inline `# zizmor: ignore[...]` with its reason.

## EF Core migrations are exempt

`dotnet ef` generates everything under `Migrations/`. Don't review its comments, and don't hand-edit a migration to satisfy a comment rule.
