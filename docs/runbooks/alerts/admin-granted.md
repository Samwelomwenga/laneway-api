# Admin granted

Tier: notify. The threshold is a starting guess. Retune it once real traffic exists.

## What fired

A `MemberAdded` or `MemberRoleChanged` line in production with `role` `Admin`. Someone became an admin of a workspace or a board, either by an admin's change or by accepting an invite sent with that role. Creating a workspace or a board makes its creator admin and doesn't fire this.

## The first query to run

```logql
{service_name="laneway-api", deployment_environment_name="production"} | scope_name="Laneway.Access" | event_name=~"MemberAdded|MemberRoleChanged" | role="Admin"
```

Read `sub`, who made the change, `memberSub`, who got it, `targetType` and `targetId`, and `clientIp`. An `inviteId` means an invite made the member. Then see what else that `sub` did:

```logql
{service_name="laneway-api", deployment_environment_name="production"} | scope_name=~"Laneway[.](Access|Auth)" | sub="<sub>"
```

## Telling noise from attack

Noise looks like a known admin promoting a teammate from an address they've used before. It's the normal way a workspace grows.

An attack looks like a grant from a `sub` that just signed in from a new address, or that just changed its password or email. It also looks like a grant followed by `VisibilityChanged`, `MemberRemoved` on the other admins, or a delete. Someone took over an admin account and is locking the owners out.

## What to do

1. If it looks normal, do nothing. Admin grants are rare, so this alert stays useful.
2. If not, ask the workspace's other admins whether they know about it.
3. If the account is taken over, ask the owner to reset the password, which ends every session. Then undo the grant from an admin account that is still trusted.

## When to escalate

Escalate to an incident when the grant is followed by deletes, removals of other admins, or a [made public](made-public.md) alert on the same target.
