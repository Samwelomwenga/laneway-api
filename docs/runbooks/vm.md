# The VM

Laneway runs on one Oracle Cloud A1 instance in Frankfurt, built by `terraform/vm`. This page covers the Doppler keys the VM reads, the first build, a planned replace, the tailnet node swap, serial console break-glass, and rotating the secrets in OCI Vault. [The Terraform runbook](terraform.md) covers state, Doppler, and CI for every root. [The backup runbook](backup.md) covers the dumps and the database passwords. [The Cloudflare runbook](cloudflare.md) covers the Tunnel and R2, and [the Tailscale runbook](tailscale.md) covers the tailnet.

The only way in is Tailscale SSH. The VM has no inbound port, `sshd` is masked, and no account has a password or an authorized key.

## What a replace keeps

Any change to `user_data` destroys the instance and launches a new one. These change it:

- any file cloud-init writes: `cloud-init.yaml`, `compose.yaml`, `config.alloy`, and the four `.sh` files in `terraform/vm`
- `image_id` or `bootstrap_oci_cli_digest`
- the three Vault secret OCIDs, or `backup_age_recipient`

A replace keeps everything on the 100 GB data volume. That's the Postgres data under `/mnt/data/postgres`, and the rendered env files, `images.env`, and the current and previous digests under `/var/lib/laneway`. The new VM finds them on first boot and starts the last good release. The reserved public IP moves to the new instance in place, so Brevo's Authorised IPs list needs no change. The tailnet IP stays the same once you do [the node swap](#the-tailnet-node-swap).

A replace loses the boot volume: Docker's image cache, the local journal, and anything installed by hand. Compose pulls the images again on first boot.

The data volume lives in one availability domain and has `prevent_destroy`, so the VM can't move to another one. A plan that changes `availability_domain` after the first build fails on the volume. A replace that runs out of capacity waits for capacity in the same domain.

## The Doppler keys the VM reads

On every deploy, `laneway-secrets` downloads `laneway-db` `prd` and `laneway-api` `prd` and splits them into one env file per consumer. Every key below must exist before the first deploy. Set each value in the Doppler dashboard, never in a ticket, a commit, or a shell that logs. No value may hold a single quote or a newline, because `laneway-secrets` refuses to render one.

### `laneway-db` `prd`

Make the project `laneway-db` with one config, `prd`. It holds what only Postgres, the migrator, and the backup job see.

1. Generate three passwords, one at a time, with `openssl rand -hex 32`. Hex keeps `;` and `=` out of the connection strings.
2. Add:

   ```sh
   POSTGRES_PASSWORD=<first password>
   MIGRATOR_PASSWORD=<second password>
   APP_PASSWORD=<third password>
   MIGRATOR_CONNECTION=Host=postgres;Port=5432;Database=laneway;Username=laneway_migrator;Password=${MIGRATOR_PASSWORD}
   ```

   `laneway` is the database Compose creates through `POSTGRES_DB`, and the initdb script creates both roles with these passwords on an empty volume. Doppler resolves `${MIGRATOR_PASSWORD}`, so a rotation changes one key.
3. Add the four `R2_` keys from [the Cloudflare runbook](cloudflare.md#the-token).

The read-only service token for this config goes into OCI Vault in step 2 of [the first build](#the-first-build).

### `laneway-api` `prd`

This is the app's own config. Add:

```sh
CONNECTIONSTRINGS__DEFAULTCONNECTION=Host=postgres;Port=5432;Database=laneway;Username=laneway_app;Password=${laneway-db.prd.APP_PASSWORD}
```

The password is a reference into `laneway-db`, so it lives in one place. Leave `Include Error Detail` out. It puts row values into exception messages, and those reach the logs.

The same config also holds:

- `TUNNEL_TOKEN`, from [the Cloudflare runbook](cloudflare.md#the-tunnel).
- `ALLOY_LOKI_URL`, `ALLOY_LOKI_USERNAME` and `ALLOY_LOKI_TOKEN`, from [the Grafana runbook](grafana.md#alloys-token).
- the three `BREVO__` keys, from [the Brevo runbook](brevo.md#production).

`laneway-secrets` keeps `TUNNEL_TOKEN`, every `ALLOY_` key and every `DOPPLER_` key out of `api.env`, so keep those names exact. Its read-only service token goes into OCI Vault beside the `laneway-db` one.

### `laneway-backup` `prd`

It holds the `age` private key that decrypts the backups. No service token ever exists for it, and nothing on the VM can read it. Make the project with one config, `prd`, then generate the key pair on the laptop. Install `age` first, with `sudo dnf install age` on Fedora. The private key goes from `age-keygen` into Doppler without touching disk, and the public key goes into `laneway-vm` for `terraform/vm`:

```sh
key=$(age-keygen 2>/dev/null | grep '^AGE-SECRET-KEY-')
if [ -n "$key" ]; then
  printf %s "$key" | doppler secrets set AGE_SECRET_KEY --project laneway-backup --config prd >/dev/null
  printf %s "$key" | age-keygen -y | doppler secrets set TF_VAR_BACKUP_AGE_RECIPIENT --project laneway-vm --config prd >/dev/null
else
  echo 'age-keygen gave no key. Is age installed?'
fi
unset key
```

`doppler secrets set` prints the value it saved, so `>/dev/null` keeps the private key off the screen. Without the `if`, a missing `age-keygen` saves two empty strings.

Check that the two halves match. It prints `ok`:

```sh
echo ok | age -r "$(doppler secrets get TF_VAR_BACKUP_AGE_RECIPIENT --project laneway-vm --config prd --plain)" \
  | age -d -i <(doppler secrets get AGE_SECRET_KEY --project laneway-backup --config prd --plain)
```

Then open `AGE_SECRET_KEY` in the Doppler dashboard and copy it into the password manager. If Doppler loses the project, that copy is the only way to read a backup.

## The first build

Run it once, from the `terraform/vm` PR's branch, on the laptop. The `oci-bootstrap` apply and every `laneway-vm` key in [Plan terraform/vm from the laptop](terraform.md#plan-terraformvm-from-the-laptop) come first, except the three Vault secret OCIDs, which step 2 creates. So do the Doppler keys above and [the Tailscale setup](tailscale.md).

Start a session and point the OCI CLI at it for the whole shell:

```sh
oci session authenticate --region eu-frankfurt-1 --profile-name laneway
export OCI_CLI_PROFILE=laneway OCI_CLI_AUTH=security_token
```

1. Apply the vault and its key alone. The instance's preconditions need the secret OCIDs, so a full plan fails until step 2:

   ```sh
   TF_VAR_auth=SecurityToken doppler run --project laneway-vm --config prd -- \
     bash scripts/terraform-plan.sh vm -target=oci_kms_vault.laneway -target=oci_kms_key.secrets
   doppler run --project laneway-vm --config prd -- terraform -chdir=terraform/vm apply tfplan
   rm terraform/vm/tfplan
   ```

2. Create the three Vault secrets. The snippets are for zsh. Each value goes from a hidden prompt into the CLI and never to disk or shell history. The VM's policy matches these exact names. The OCID each create prints goes straight into Doppler:

   ```sh
   compartment=$(doppler secrets get TF_VAR_COMPARTMENT_ID --project laneway-vm --config prd --plain)
   vault=$(doppler run --project laneway-vm --config prd -- terraform -chdir=terraform/vm output -raw vault_id)
   key=$(doppler run --project laneway-vm --config prd -- terraform -chdir=terraform/vm output -raw secrets_key_id)
   vault_secret() {
     local value id
     read -s "?$1: " value && echo
     id=$(oci vault secret create-base64 --compartment-id "$compartment" --vault-id "$vault" --key-id "$key" \
       --secret-name "$1" --secret-content-content "$(printf %s "$value" | base64 -w0)" \
       --query data.id --raw-output) || return
     printf %s "$id" | doppler secrets set "$2" --project laneway-vm --config prd
   }
   vault_secret laneway-db-doppler-token TF_VAR_LANEWAY_DB_DOPPLER_TOKEN_SECRET_ID
   vault_secret laneway-api-doppler-token TF_VAR_LANEWAY_API_DOPPLER_TOKEN_SECRET_ID
   vault_secret laneway-vm-tailscale-client-secret TF_VAR_TAILSCALE_CLIENT_SECRET_SECRET_ID
   ```

   Generate each read-only service token as in step 1 of [The two Doppler tokens](#the-two-doppler-tokens). Paste the tokens for `laneway-db` `prd` and `laneway-api` `prd` at the first two prompts, and the Tailscale OAuth client secret at the third. Give both Doppler tokens the shared expiry day in [the Terraform runbook](terraform.md#rotate-the-tokens-every-90-days). A failed create stops before Doppler, so it never blanks a key.

3. Terminate the placeholder A1 instance in the console, with "Permanently delete the attached boot volume" ticked. Then delete the VCN the launch wizard made for it. The placeholder holds the whole free A1 allowance, so the full apply can't launch while it runs.

4. Run the full apply:

   ```sh
   TF_VAR_auth=SecurityToken doppler run --project laneway-vm --config prd -- bash scripts/terraform-plan.sh vm
   doppler run --project laneway-vm --config prd -- terraform -chdir=terraform/vm apply tfplan
   rm terraform/vm/tfplan
   ```

   On `Out of host capacity`, plan and apply again a few times. Then set `TF_VAR_AVAILABILITY_DOMAIN` to `1` in `laneway-vm` `prd` and try again, then `3`. Terraform creates the data volume after the instance, so a failed launch strands nothing. Whichever domain works is the VM's home from then on.

The session lasts an hour. If it runs out, run `oci session refresh --profile laneway` and plan again.

### First boot checks

cloud-init takes a few minutes. When `laneway` shows in `tailscale status` on the laptop, connect over Tailscale SSH as `ubuntu` and run:

```sh
cloud-init status --long
ls -l /dev/oracleoci/oraclevdb && findmnt /mnt/data && findmnt /var/lib/laneway
sudo docker network create egress-check
sudo docker run --rm --network egress-check curlimages/curl --fail --silent --show-error --output /dev/null https://www.cloudflare.com && echo egress=ok
sudo docker network rm egress-check
curl --silent https://checkip.amazonaws.com
```

- `laneway` is connected in `tailscale status`, tagged `tag:laneway-vm`, and Tailscale SSH got you in.
- `cloud-init status` says `done` with no errors.
- The data volume is present at `/dev/oracleoci/oraclevdb` and mounted at both paths.
- `egress=ok` prints. Compose's network is a user-defined bridge like `egress-check`, so this shows Docker's rules let containers out beside OCI's FORWARD REJECT rule.
- The address `checkip` prints matches `terraform -chdir=terraform/vm output -raw public_ip` on the laptop. That's the reserved IP, attached.

Tick each of the five in the PR. Don't paste the IP. A brand-new VM starts no containers until the first deploy, so `docker ps` is empty for now. Alloy is inactive too. Its unit waits for `alloy.env`, which the first deploy writes.

### Handing over to CI

1. Set the repo variable the deploy workflow reads. Run it on the laptop:

   ```sh
   gh variable set VM_TAILNET_IP --body "$(tailscale ip -4 laneway)"
   ```

2. Plan once more from the branch and check it says `No changes in vm.`
3. Merge. `Terraform vm` runs on `main` and its `Plan` must also say `No changes in vm.` It's the first run to authenticate through workload identity federation, so a pass proves the CI policy's condition. If it fails with `NotAuthorizedOrNotFound` on every resource, the fallback API key user goes into `oci-bootstrap` and its key into `laneway-vm`, as [docs/ci.md](../ci.md#terraform-vm) says. If it fails on a blocked host, add the host to `ALLOWED_ENDPOINTS` and run the workflow again.
4. Record in the PR the hosts the `Plan` job's harden-runner insights link lists. CI owns the root from here.

The trust accepts only `main`'s OIDC token, so a PR can't run a CI plan, and the egress audit in the Terraform runbook can't authenticate from a branch. The OCI hosts in `ALLOWED_ENDPOINTS` came from the provider's endpoints. The first run on `main` is the check.

## A planned replace

Every change in [What a replace keeps](#what-a-replace-keeps) goes in as its own PR, with nothing else in it. Production is down from the moment the old VM stops until the new one starts the API. That's a few minutes with capacity, or hours without it.

1. Open the PR, then plan from its branch on the laptop with a session token, as in [Plan terraform/vm from the laptop](terraform.md#plan-terraformvm-from-the-laptop). The plan should list these and nothing else:

   ```text
   delete+create oci_core_instance.vm
   delete+create oci_core_volume_attachment.data
   update oci_core_public_ip.vm
   ```

   `delete+create` means Terraform destroys the old instance before it launches the new one. The free allowance has no room for two. Anything else in the list, a `delete` on `oci_core_volume.data` most of all, means stop.
2. Pick a time away from the 03:00 UTC backup and the 04:00 UTC patch reboot, and away from a release. Take a fresh dump on the VM and check it says `result=ok`:

   ```sh
   sudo systemctl start laneway-backup.service
   journalctl -t laneway-backup --since -10min
   ```

3. Merge. `Terraform vm` plans on `main`. Check its job summary shows the same three lines. When the `vm` environment asks for approval, stop the VM first over Tailscale SSH:

   ```sh
   sudo systemctl poweroff
   ```

   Terraform detaches the data volume before it terminates the instance. A clean shutdown stops Postgres and unmounts the volume first, so the detach can't cut a write in half.
4. Approve the apply.
   - If it fails with `Out of host capacity`, rerun the failed job from the Actions tab until it launches. Each rerun plans again, and the volume, IP and vault stay put.
   - If the apply fails before it destroys anything, start the old VM from the console. Nothing else changed.
5. Do [the tailnet node swap](#the-tailnet-node-swap) as soon as the new node shows in the admin console.
6. Check the new VM over Tailscale SSH:

   ```sh
   cloud-init status --long
   systemctl status laneway.service alloy.service
   sudo docker ps
   curl --fail http://127.0.0.1:8080/health/ready
   ```

   `docker ps` shows `laneway-api-1`, `laneway-postgres-1` and `laneway-cloudflared-1` running, and `/health/ready` answers `Healthy`. Then check `https://laneway-api.samwelomwenga.com/health/ready` from the laptop.

## The tailnet node swap

The deploy workflow connects to `VM_TAILNET_IP`, a repo variable. A replaced VM joins the tailnet as a new node with a new IP, and the old node stays behind, since the VM's node isn't ephemeral. Give the new node the old IP so the variable never changes.

1. In the Tailscale admin console, open Machines. The old `laneway` node shows offline, and the new one shows as `laneway-1` or similar, tagged `tag:laneway-vm`.
2. Remove the old node. Its IP is free once it's gone.
3. On the new node, open its menu, edit its Tailscale IPv4 address, and set it to the value of `VM_TAILNET_IP`.
4. Rename the new node to `laneway`, so MagicDNS and `tailscale ip -4 laneway` match the docs.
5. The new VM has a new SSH host key. If plain `ssh` on the laptop warns about a changed key, run `ssh-keygen -R <tailnet IP>`. `tailscale ssh` checks keys against the tailnet and needs nothing.

If the node swap can't happen, set `VM_TAILNET_IP` to the new node's IP with `gh variable set` instead.

## Serial console break-glass

Use this when Tailscale on the VM won't connect. If the console can't fix it, stop the VM with `oci compute instance action --instance-id "$instance" --action SOFTSTOP`, using the id from step 1, then replace it from a local plan with `-replace=oci_core_instance.vm` and apply. The data volume survives.

The console connection is an OCI resource that Terraform doesn't track. Delete it when you're done.

1. Make a throwaway key and open the connection. OCI's console proxy wants an RSA key:

   ```sh
   oci session authenticate --region eu-frankfurt-1 --profile-name laneway
   export OCI_CLI_PROFILE=laneway OCI_CLI_AUTH=security_token
   ssh-keygen -t rsa -b 4096 -N '' -f ~/.ssh/laneway-console
   compartment=$(doppler secrets get TF_VAR_COMPARTMENT_ID --project laneway-vm --config prd --plain)
   instance=$(oci compute instance list --compartment-id "$compartment" --display-name laneway \
     --lifecycle-state RUNNING --query 'data[0].id' --raw-output)
   connection=$(oci compute instance-console-connection create --instance-id "$instance" \
     --ssh-public-key-file ~/.ssh/laneway-console.pub --query data.id --raw-output --wait-for-state ACTIVE)
   oci compute instance-console-connection get --instance-console-connection-id "$connection" \
     --query 'data."connection-string"' --raw-output
   ```

2. Run the `ssh` command it prints, adding `-i ~/.ssh/laneway-console` to both the outer command and its `ProxyCommand`. If OpenSSH refuses the proxy's RSA host key, add `-o HostKeyAlgorithms=+ssh-rsa -o PubkeyAcceptedAlgorithms=+ssh-rsa` to both too. Press Enter to get output.
3. In a second terminal, set `OCI_CLI_PROFILE`, `OCI_CLI_AUTH` and `instance` the same way as in step 1, then reboot the VM:

   ```sh
   oci compute instance action --instance-id "$instance" --action SOFTRESET
   ```

4. Back in the console, press Esc as the VM restarts until the GRUB menu shows. Press `e` on the first entry. Go to the end of the line that starts with `linux` and add `init=/bin/bash`. Press Ctrl+X to boot.
5. At the root shell, set a temporary root password, then reboot hard. With `init=/bin/bash`, systemd isn't running, so `reboot` won't work:

   ```sh
   mount -o remount,rw /
   passwd root
   sync
   echo b > /proc/sysrq-trigger
   ```

6. Let the VM boot normally. Log in on the console as `root` with that password. Find out why Tailscale won't connect:

   ```sh
   systemctl status tailscaled
   journalctl -u tailscaled -b
   tailscale status
   cloud-init status --long
   ```

   If the node was removed from the tailnet, join again with `/usr/local/sbin/laneway-tailscale-join`. It reads the OAuth client secret from Vault, as first boot does.
7. Once Tailscale SSH works from the laptop, lock root again on the console, then type `~.` to leave it:

   ```sh
   passwd --lock root
   ```

8. On the laptop, delete the connection and the key:

   ```sh
   oci compute instance-console-connection delete --instance-console-connection-id "$connection" --force
   rm ~/.ssh/laneway-console ~/.ssh/laneway-console.pub
   ```

Record what worked in the log below. The GRUB step is the part most likely to differ from these notes on Oracle's Ubuntu image.

### Break-glass log

| Date | Cause | What fixed it | Notes |
|---|---|---|---|

## Rotating the secrets in Vault

OCI Vault holds three secrets, and the VM reads each by OCID, always the newest version. A new version keeps the OCID, so a rotation never changes `user_data` and never replaces the VM.

| Secret | Value | Read by | Expires |
|---|---|---|---|
| `laneway-db-doppler-token` | read-only service token for `laneway-db` `prd` | `laneway-secrets`, on every deploy | 90 days |
| `laneway-api-doppler-token` | read-only service token for `laneway-api` `prd` | `laneway-secrets`, on every deploy | 90 days |
| `laneway-vm-tailscale-client-secret` | Tailscale OAuth client secret, `auth_keys` and `tag:laneway-vm` only | `laneway-tailscale-join`, on first boot | never |

### The two Doppler tokens

They share the 90-day rotation day in [the Terraform runbook](terraform.md#rotate-the-tokens-every-90-days). The env files on the data volume keep working when a token expires. Only the next deploy fails, with `result=secrets-failed`.

1. In Doppler, open `laneway-db`, config `prd`, Access. Generate a read-only service token that expires on the shared rotation day. Don't revoke the old one yet.
2. Write it into Vault as a new version, from a hidden prompt:

   ```sh
   oci session authenticate --region eu-frankfurt-1 --profile-name laneway
   export OCI_CLI_PROFILE=laneway OCI_CLI_AUTH=security_token
   read -s '?token: ' value && echo
   oci vault secret update-base64 --secret-id "$(doppler secrets get TF_VAR_LANEWAY_DB_DOPPLER_TOKEN_SECRET_ID --project laneway-vm --config prd --plain)" \
     --secret-content-content "$(printf %s "$value" | base64 -w0)" --query 'data."lifecycle-state"' --raw-output
   unset value
   ```

3. Repeat steps 1 and 2 for `laneway-api` `prd`, with `TF_VAR_LANEWAY_API_DOPPLER_TOKEN_SECRET_ID`.
4. Redeploy the live digests on the VM, so `laneway-secrets` reads both new tokens:

   ```sh
   sudo laneway-deploy $(. /var/lib/laneway/images.env && echo "$API_DIGEST $POSTGRES_DIGEST $CLOUDFLARED_DIGEST $OCI_CLI_DIGEST")
   ```

   It must end with `result=deployed`. On `result=secrets-failed`, `journalctl -t laneway-secrets -b` says which read failed, and the old env files are still in place.
5. Revoke the old tokens in Doppler.

### The Tailscale client secret

It doesn't expire. Only a new VM reads it, so a running VM doesn't notice a rotation. Rotate it if it leaks:

1. In the Tailscale admin console, Settings, Trust credentials, create an OAuth client with the `auth_keys` scope and `tag:laneway-vm` alone.
2. Write its secret into Vault with step 2 above, using `TF_VAR_TAILSCALE_CLIENT_SECRET_SECRET_ID`.
3. Revoke the old client.
