# Terraform

Each Terraform root is a folder under `terraform/`. Today there are three: `alerting`, `oci-bootstrap` and `vm`. This page covers what every root shares: where state lives, where secrets come from, how CI runs it, and the tokens that expire. [The alerting runbook](alerting.md) covers what only alerting needs. [The VM runbook](vm.md) covers what only `terraform/vm` needs. [docs/ci.md](../ci.md#terraform-alerting) says how to fix a red run.

| Root | HCP workspace | Doppler project | GitHub environments |
|---|---|---|---|
| `terraform/alerting` | `laneway-alerting` | `laneway-alerting`, config `prd` | `alerting-plan`, `alerting` |
| `terraform/oci-bootstrap` | `laneway-oci-bootstrap` | `laneway-vm`, config `prd` | none, it runs only from the laptop |
| `terraform/vm` | `laneway-vm` | `laneway-vm`, config `prd` | `vm-plan`, `vm` |

HCP Terraform holds state and nothing else. Its workspaces run in local execution mode, so `plan` and `apply` run on the operator's machine or on a GitHub runner, never on HCP. Secrets live in Doppler, one project per root, except that `oci-bootstrap` shares `laneway-vm` with `terraform/vm`. CI loads them at job time with `doppler run`. The state holds every secret the root's resources take, such as the Telegram bot token on a contact point. HCP encrypts it.

## The HCP organization

1. Sign up for HCP Terraform on the Free plan.
2. Create an organization named `laneway`. Every root's `cloud` block names it.

## A workspace per root

1. In the organization, create a workspace with the CLI-driven workflow. Name it `laneway-<root>`, such as `laneway-alerting`. It must match the `cloud` block in `terraform/<root>/versions.tf`.
2. In the workspace's Settings, General, set the execution mode to Local.
3. Add no workspace variables. Doppler holds them all.

## The HCP token

Terraform reads the HCP token from `TF_TOKEN_app_terraform_io`. Doppler stores it as `TF_TOKEN_APP_TERRAFORM_IO`, and Terraform ignores case in the hostname part.

1. In Organization settings, Teams, open `owners` and create a team token that expires in 90 days. The Free plan has only the owners team, so this token can do anything in the organization.
2. Add it as `TF_TOKEN_APP_TERRAFORM_IO` in the root's Doppler config, such as `laneway-alerting` `prd`. Add it in the dashboard, not the shell.

Each root's Doppler config holds its own copy. The token never goes in a GitHub secret.

## The Doppler service token

1. In the root's Doppler project, open the `prd` config, then Access. Generate a service token with read access that expires in 90 days.
2. In GitHub, open Settings, Environments, and create two environments:
   - `<root>-plan`, such as `alerting-plan`. No reviewer. `plan` runs here.
   - `<root>`, such as `alerting`. Add the operator as a required reviewer and leave "Prevent self-review" off, since the operator approves their own gate. `apply` runs here.
3. On both, set Deployment branches and tags to Selected branches, and add `main` alone.
4. On both, add an environment secret `DOPPLER_TOKEN` with the same service token value.

A run on any branch but `main` can't reach either environment, so it never sees the token.

## Run Terraform locally

Sign in to Doppler with `doppler login` once. Then, for alerting:

```sh
doppler run --project laneway-alerting --config prd -- terraform -chdir=terraform/alerting init
doppler run --project laneway-alerting --config prd -- bash scripts/terraform-plan.sh alerting
```

`scripts/terraform-plan.sh` is the same plan CI runs. It prints the addresses and actions and nothing else. It leaves `tfplan` in the root, which holds every secret in plain text. Git ignores it. Delete it when you're done. To read the full diff locally, run `terraform -chdir=terraform/alerting show tfplan` after the script. Don't paste it anywhere public.

The script also passes `-target=<address>` and `-replace=<address>` through to `terraform plan`. The VM's first build and a forced replace use them.

Applying from a laptop works, but it skips the gate. Merge to `main` instead, and let CI apply.

## Apply oci-bootstrap from the laptop

`terraform/oci-bootstrap` holds what lives at tenancy level or in IAM. That's the `laneway` compartment and a quota that leaves only the Always Free A1 allowance in it. It's also the identity domain app and trust that CI trades its GitHub token through, and the CI policy. Last, it holds the VM's dynamic group and that group's policies. CI can't change any of it. The CI principal manages everything inside the compartment and nothing outside it. So this root has no workflow, and the operator applies it by hand with a one-hour session token.

It shares the `laneway-vm` Doppler project with `terraform/vm`, for the HCP token and the tenancy OCID. The Vault secret names, the backup bucket name, the token audience and the resource type are variables with defaults. `terraform/vm` repeats the same values. Neither root reads the other's state. The VM's policy grants `read secret-bundles` by secret name, so create the three Vault secrets with exactly the names in `vault_secret_names`. A secret under any other name stays unreadable to the VM.

Before the first apply:

1. Create the `laneway-oci-bootstrap` workspace as in [A workspace per root](#a-workspace-per-root).
2. Create the Doppler project `laneway-vm` if it doesn't exist yet. Add the HCP token to its `prd` config as in [The HCP token](#the-hcp-token).
3. Add `TF_VAR_TENANCY_OCID` to `laneway-vm` `prd`. The tenancy OCID is on the console's Tenancy details page.
4. Install the OCI CLI on the laptop.

To apply:

1. Start a session. It opens the browser to sign in and writes a profile named `laneway` to `~/.oci/config`:

   ```sh
   oci session authenticate --region eu-frankfurt-1 --profile-name laneway
   ```

2. Plan. The script lowercases the `TF_VAR_` names Doppler hands it, so use it rather than a bare `terraform plan`:

   ```sh
   doppler run --project laneway-vm --config prd -- bash scripts/terraform-plan.sh oci-bootstrap
   ```

3. Read the list of changes. Run `terraform -chdir=terraform/oci-bootstrap show tfplan` for the full diff. Then apply the saved plan and delete it:

   ```sh
   doppler run --project laneway-vm --config prd -- terraform -chdir=terraform/oci-bootstrap apply tfplan
   rm terraform/oci-bootstrap/tfplan
   ```

4. On the first apply, copy the app's client ID, client secret and domain URL into `laneway-vm` `prd`. The secret goes from the output straight into Doppler, never to the screen:

   ```sh
   doppler run --project laneway-vm --config prd -- terraform -chdir=terraform/oci-bootstrap output -raw token_exchange_client_secret | doppler secrets set OCI_TOKEN_EXCHANGE_CLIENT_SECRET --project laneway-vm --config prd
   doppler run --project laneway-vm --config prd -- terraform -chdir=terraform/oci-bootstrap output -raw token_exchange_client_id | doppler secrets set OCI_TOKEN_EXCHANGE_CLIENT_ID --project laneway-vm --config prd
   doppler run --project laneway-vm --config prd -- terraform -chdir=terraform/oci-bootstrap output -raw token_exchange_domain_url | doppler secrets set OCI_TOKEN_EXCHANGE_DOMAIN_URL --project laneway-vm --config prd
   ```

   If the secret output comes back empty, the provider didn't return it on create. Open Identity & Security, Domains, Default, Integrated applications, `laneway-terraform-ci`, OAuth configuration, and copy the secret from there.

The session lasts an hour. If it runs out mid-apply, run `oci session refresh --profile laneway` and apply again with a fresh plan.

## Plan terraform/vm from the laptop

`terraform/vm` holds everything inside the `laneway` compartment: the network, the A1 instance, its data volume and reserved IP, the backup bucket, and the vault. CI applies it. In CI the provider trades the job's GitHub OIDC token for a 60-minute OCI session token through the app and trust in `oci-bootstrap`. On the laptop it uses the same one-hour session token as `oci-bootstrap`.

Add these to `laneway-vm` `prd` once `oci-bootstrap` is applied:

- `TF_VAR_COMPARTMENT_ID`, from `terraform -chdir=terraform/oci-bootstrap output -raw compartment_id`.
- `OCI_TOKEN_EXCHANGE_CLIENT_ID`, `OCI_TOKEN_EXCHANGE_CLIENT_SECRET` and `OCI_TOKEN_EXCHANGE_DOMAIN_URL`, from the last step of the `oci-bootstrap` apply. The provider reads them from the environment.
- `TF_VAR_LANEWAY_DB_DOPPLER_TOKEN_SECRET_ID`, `TF_VAR_LANEWAY_API_DOPPLER_TOKEN_SECRET_ID` and `TF_VAR_TAILSCALE_CLIENT_SECRET_SECRET_ID`, the OCIDs of the three Vault secrets, once the operator creates them. Until all three are set, a plan that includes the instance fails its precondition. A later change to them changes `user_data` and replaces the VM.
- `TF_VAR_BACKUP_AGE_RECIPIENT`, the `age1...` public key whose private key is `AGE_SECRET_KEY` in `laneway-backup` `prd`. The plan fails its precondition without it, and a later change replaces the VM. [The backup runbook](backup.md) says how the VM uses it.

Start a session as in [Apply oci-bootstrap from the laptop](#apply-oci-bootstrap-from-the-laptop), then plan with `auth` switched to the session token:

```sh
TF_VAR_auth=SecurityToken doppler run --project laneway-vm --config prd -- bash scripts/terraform-plan.sh vm
```

The trust accepts the OIDC token of any `terraform.yml` job on `main`, alerting's included, since every root runs the same reusable workflow. Only `laneway-vm` holds the client secret, so only the vm jobs can complete the exchange.

## Check the egress allowlist

Every job that holds the Doppler token runs harden-runner with `egress-policy: block`. `ALLOWED_ENDPOINTS` at the top of `.github/workflows/terraform.yml` lists every host Terraform, the Doppler CLI and the providers call. A host missing from the list fails the job with a connection error.

Confirm the list with an audit run before a new root or provider first merges:

1. On the PR branch, push a commit that sets both jobs to `egress-policy: audit` and adds the branch to the caller's `push` trigger.
2. Add the branch to the `<root>-plan` environment's deployment branches. Leave `<root>` on `main` alone, so `apply` can't run. If `plan` finds changes, `apply` fails on the branch rule and a Telegram alert arrives. Ignore it.
3. Wait for `plan` to finish. Open the harden-runner insights link in its log and read the hosts it called.
4. Put each host in `ALLOWED_ENDPOINTS`, drop the audit commit, and remove the branch from `<root>-plan`.

The audit run covers `plan` only, because `apply` can't run on the branch. `apply` also uploads state to `archivist.terraform.io`, which `plan` never calls. That host is on the list already. A new provider may call hosts at apply time that the audit never sees. If an apply fails on a blocked host, it may leave resources in Grafana that HCP state doesn't hold. Find them, delete or import them, then run the workflow again.

## Rotate the tokens every 90 days

The HCP team token, the Doppler service tokens, the Grafana service account token, and the two Doppler tokens in OCI Vault share an expiry day. HCP emails the owners 30 and 7 days before its token expires, and that reminder covers them all. Rotate them together:

1. Create a new HCP owners team token. Set it as `TF_TOKEN_APP_TERRAFORM_IO` in every root's Doppler config.
2. Create a new Grafana service account token for `terraform-alerting`. Set it as `TF_VAR_GRAFANA_AUTH` in `laneway-alerting` `prd`.
3. Generate a new Doppler service token for each root. Set it as `DOPPLER_TOKEN` on both of that root's GitHub environments.
4. Rotate the `laneway-db` and `laneway-api` tokens in Vault, as in [the VM runbook](vm.md#the-two-doppler-tokens).
5. Run each root's workflow by hand from the Actions tab on `main`. `plan` must pass.
6. Revoke the old HCP and Grafana tokens and the old root Doppler tokens. Step 4 already revoked the old Vault ones.
7. Write the new expiry day here.

Rotation stays manual. Automating it would need a Doppler personal token and a GitHub PAT, and both are stronger and live longer than the tokens they rotate.

## Bump the pinned Terraform version

Dependabot bumps providers, not Terraform itself. To move to a new release, edit `required_version` in every root's `versions.tf` and `terraform_version` in the `Terraform check` job of `.github/workflows/ci.yml` in one PR. The reusable workflow reads the version from each root's `versions.tf`, so it needs no edit. Run `terraform init` and `terraform validate` on each root locally with the new release first.

## Scheduled workflows stop after 60 days

GitHub disables a scheduled workflow in a public repo after 60 days with no activity in the repo. It sends the owner an email first. If it happens, open the workflow in the Actions tab and click Enable workflow. A commit to the repo also resets the count.
