# Terraform

Each Terraform root is a folder under `terraform/`. Today there is one, `alerting`. This page covers what every root shares: where state lives, where secrets come from, how CI runs it, and the tokens that expire. [The alerting runbook](alerting.md) covers what only alerting needs. [docs/ci.md](../ci.md#terraform-alerting) says how to fix a red run.

| Root | HCP workspace | Doppler project | GitHub environments |
|---|---|---|---|
| `terraform/alerting` | `laneway-alerting` | `laneway-alerting`, config `prd` | `alerting-plan`, `alerting` |

HCP Terraform holds state and nothing else. Its workspaces run in local execution mode, so `plan` and `apply` run on the operator's machine or on a GitHub runner, never on HCP. Secrets live in Doppler, one project per root. CI loads them at job time with `doppler run`. The state holds every secret the root's resources take, such as the Telegram bot token on a contact point. HCP encrypts it.

## The HCP organization

1. Sign up for HCP Terraform on the Free plan.
2. Create an organization named `laneway`. Every root's `cloud` block names it.

## A workspace per root

1. In the organization, create a workspace with the CLI-driven workflow. Name it `laneway-<root>`, such as `laneway-alerting`. It must match the `cloud` block in `terraform/<root>/versions.tf`.
2. In the workspace's Settings, General, set the execution mode to Local.
3. Add no workspace variables. Doppler holds them all.

## The HCP token

Terraform reads the HCP token from `TF_TOKEN_app_terraform_io`.

1. In Organization settings, Teams, open `owners` and create a team token that expires in 90 days. The Free plan has only the owners team, so this token can do anything in the organization.
2. Add it as `TF_TOKEN_app_terraform_io` in the root's Doppler config, such as `laneway-alerting` `prd`. Add it in the dashboard, not the shell.

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

`scripts/terraform-plan.sh` is the same plan CI runs. It prints the addresses and actions and nothing else. It leaves `tfplan` in the root, which holds every secret in plain text. Git ignores it. Delete it when you're done. To read the full diff locally, run `terraform plan` in place of the script. Don't paste it anywhere public.

Applying from a laptop works, but it skips the gate. Merge to `main` instead, and let CI apply.

## Check the egress allowlist

Every job that holds the Doppler token runs harden-runner with `egress-policy: block`. `ALLOWED_ENDPOINTS` at the top of `.github/workflows/terraform.yml` lists every host Terraform, the Doppler CLI and the providers call. A host missing from the list fails the job with a connection error.

Confirm the list with an audit run before a new root or provider first merges:

1. On the PR branch, push a commit that sets both jobs to `egress-policy: audit` and adds the branch to the caller's `push` trigger.
2. Add the branch to the `<root>-plan` environment's deployment branches. Leave `<root>` on `main` alone, so `apply` can't run. If `plan` finds changes, `apply` fails on the branch rule and a Telegram alert arrives. Ignore it.
3. Wait for `plan` to finish. Open the harden-runner insights link in its log and read the hosts it called.
4. Put each host in `ALLOWED_ENDPOINTS`, drop the audit commit, and remove the branch from `<root>-plan`.

## Rotate the tokens every 90 days

The HCP team token, the Doppler service token, and the Grafana service account token share an expiry day. HCP emails the owners 30 and 7 days before its token expires, and that reminder covers all three. Rotate them together:

1. Create a new HCP owners team token. Set it as `TF_TOKEN_app_terraform_io` in every root's Doppler config.
2. Create a new Grafana service account token for `terraform-alerting`. Set it as `TF_VAR_grafana_auth` in `laneway-alerting` `prd`.
3. Generate a new Doppler service token for each root. Set it as `DOPPLER_TOKEN` on both of that root's GitHub environments.
4. Run each root's workflow by hand from the Actions tab on `main`. `plan` must pass.
5. Revoke the old tokens in HCP, Grafana and Doppler.
6. Write the new expiry day here.

Rotation stays manual. Automating it would need a Doppler personal token and a GitHub PAT, and both are stronger and live longer than the tokens they rotate.

## Bump the pinned Terraform version

Dependabot bumps providers, not Terraform itself. To move to a new release, edit `required_version` in every root's `versions.tf` and `terraform_version` in the `Terraform check` job of `.github/workflows/ci.yml` in one PR. The reusable workflow reads the version from each root's `versions.tf`, so it needs no edit. Run `terraform init` and `terraform validate` on each root locally with the new release first.

## Scheduled workflows stop after 60 days

GitHub disables a scheduled workflow in a public repo after 60 days with no activity in the repo. It sends the owner an email first. If it happens, open the workflow in the Actions tab and click Enable workflow. A commit to the repo also resets the count.
