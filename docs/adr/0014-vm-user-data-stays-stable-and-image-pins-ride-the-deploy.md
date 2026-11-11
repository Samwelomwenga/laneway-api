# VM user_data stays stable and image pins ride the deploy

On OCI, any change to an instance's `user_data` destroys the VM and creates a new one. A1 capacity in Frankfurt ran out in all three availability domains when we first made the tenancy, so a replace can leave production with no VM for hours. We keep cloud-init's output stable and change it only on purpose. The Postgres, `cloudflared`, and OCI CLI digests live in `deploy/vm/compose.images.yml`, not in `user_data`. Dependabot bumps that file, `deploy.yml` reads the digests from it, and `laneway-deploy` takes them as arguments that it checks against `^sha256:[0-9a-f]{64}$` and writes to its env file on the data volume. A digest bump is a deploy, not a replace.

## Considered options

- Put everything in `user_data` and replace on every change. Dependabot would replace production about once a month, each time risking a capacity outage.
- Use `ignore_changes` on `user_data`. A change in the repo would never reach the VM until someone forced a replace, and the plan would hide it.

## Consequences

- The file is named like a Compose file only because Dependabot's `docker-compose` ecosystem reads nothing else. Nothing runs it with `docker compose`.
- Changing `laneway-deploy`, the Compose layout, the sudoers rule, or any other cloud-init file still replaces the VM. Those changes go in as their own PR, with a replace planned in the runbook.
- First boot can't read the file, so `user_data` carries its own `bootstrap_oci_cli_digest` variable. It's used once, to fetch the Tailscale secret, and gets bumped by hand only if that image breaks.
