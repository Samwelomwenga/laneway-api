# Tailscale

Tailscale is the only way into the VM. The operator connects over Tailscale SSH as `ubuntu`, and the deploy job connects as `deploy`. Neither needs a stored SSH key, and the VM has no public port 22. This page covers the tags, the tailnet policy, and the two credentials that join nodes as those tags. [The VM runbook](vm.md) covers the first-boot join, the node swap after a replace, and rotating the VM's credential.

## The tags

| Tag | Node | Joins with |
|---|---|---|
| `tag:laneway-vm` | the VM, named `laneway` | an OAuth client secret in OCI Vault, read on first boot |
| `tag:ci` | each deploy job's runner, ephemeral | workload identity federation, trusting the `production` environment |

A tagged node belongs to the tailnet, not to a user, so it keeps working if the operator's login changes. A `tag:ci` node can reach port 22 on the VM and nothing else. The VM can reach nothing in the tailnet.

## The policy

In the admin console, open Access controls and switch to the JSON editor. The default policy lets every node reach every other node, and a `tag:ci` runner would get that too. Replace it with this:

```json
{
  "tagOwners": {
    "tag:laneway-vm": ["autogroup:admin"],
    "tag:ci": ["autogroup:admin"]
  },
  "grants": [
    { "src": ["autogroup:member"], "dst": ["autogroup:self"], "ip": ["*"] },
    { "src": ["autogroup:member"], "dst": ["tag:laneway-vm"], "ip": ["tcp:22"] },
    { "src": ["tag:ci"], "dst": ["tag:laneway-vm"], "ip": ["tcp:22"] }
  ],
  "ssh": [
    { "action": "check", "src": ["autogroup:member"], "dst": ["autogroup:self"], "users": ["autogroup:nonroot", "root"] },
    { "action": "check", "src": ["autogroup:member"], "dst": ["tag:laneway-vm"], "users": ["ubuntu"] },
    { "action": "accept", "src": ["tag:ci"], "dst": ["tag:laneway-vm"], "users": ["deploy"] }
  ]
}
```

- The first grant and the first SSH rule keep the operator's own devices talking to each other, as the default policy did.
- The operator reaches the VM on port 22 alone, as `ubuntu`. `check` asks for a fresh browser sign-in every 12 hours.
- A deploy job reaches the VM on port 22 alone, as `deploy`. `accept` asks for nothing, since the runner has no browser. `deploy` may run `sudo /usr/local/bin/laneway-deploy` and nothing else.
- Nothing can reach a `tag:ci` node, and the VM can't start a connection to anything in the tailnet.

Save it. The editor refuses a policy that doesn't parse.

## The VM's credential

The VM joins on first boot with an OAuth client secret. The secret doesn't expire. It mints a one-hour access token, then a one-off auth key for `tag:laneway-vm`.

1. In the admin console, open Settings, then Trust credentials. Create an OAuth client named `laneway-vm`.
2. Give it the `auth_keys` scope with write access, and add `tag:laneway-vm` as its only tag.
3. Tailscale shows the secret once. Keep it in the password manager until step 2 of [the first build](vm.md#the-first-build), which writes it to OCI Vault as `laneway-vm-tailscale-client-secret`. Then delete the password manager entry, so Vault holds the only copy.

[The VM runbook](vm.md#the-tailscale-client-secret) covers rotating it.

## The deploy job's credential

The deploy job joins through workload identity federation. GitHub's OIDC token proves which job is asking, so no secret is stored anywhere.

1. In Settings, Trust credentials, create a workload identity credential named `laneway-deploy`, with GitHub as the issuer.
2. Set the subject to `repo:Samwelomwenga/laneway-api:environment:production`. Only a job in the `production` environment can join. The deploy workflow's PR creates that environment and limits it to `main` and `v*` tags. Until then, nothing matches the subject.
3. Give it the `auth_keys` scope with write access, and add `tag:ci` as its only tag.
4. Copy the client ID and the audience it shows. Neither is secret. The deploy workflow passes both to the Tailscale GitHub Action.

Each runner joins as an ephemeral node, so it leaves the tailnet when the job ends.
