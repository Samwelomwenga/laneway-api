# CI

CI runs no tests. A green PR means the code builds and passes the checks below. Run `dotnet test` before you push.

Every PR to main runs two workflows. `.github/workflows/ci.yml` holds the `CI` workflow with every check but one. `.github/workflows/pr-title.yml` holds `PR title`. A new push to the same PR cancels the run still going for the older commit. [Terraform alerting](#terraform-alerting) runs on `main` after a merge, not on PRs. Each section heading is the check name GitHub shows on the PR. The `main` ruleset requires these names, and [docs/runbooks/repo-setup.md](runbooks/repo-setup.md) says what happens if one changes.

## Build & format

The check restores `Laneway.Api.sln`, runs `dotnet format` on it and builds it in Release.

```sh
dotnet restore Laneway.Api.sln
dotnet format Laneway.Api.sln --verify-no-changes --no-restore
dotnet build Laneway.Api.sln --no-restore -c Release
```

Every command names the `.sln`, because the repo root also holds `Laneway.Api.csproj` and `dotnet` won't pick between them.

`global.json` pins the SDK, so CI and your machine load the same format and analyzer rules. If `dotnet --version` doesn't start with `10.0.4`, install that SDK band.

To fix a red run:

- The format step failed. Run `dotnet format Laneway.Api.sln` and commit what it changes.
- The build step failed on a warning. `TreatWarningsAsErrors` is on in `Directory.Build.props`, so any compiler warning fails the build. Fix the warning. The log names the file and line.
- The build step failed on a CA rule. It shouldn't. `CodeAnalysisTreatWarningsAsErrors` is false, so CA warnings don't fail the build. If one does, someone changed the props.

## Secret scan

The check runs two steps.

`scripts/check-secrets.sh` fails when git tracks a `secrets.json` or `.env` file, `.env.example` aside. It also fails when a committed `appsettings*.json` has a `ConnectionStrings` section or a `Password=` value. A placeholder like `Password=changeme` fails too, because connection strings and passwords live in Doppler.

Then gitleaks scans every commit in the PR with the default rules plus `.gitleaks.toml`. It doesn't rescan main. A local scan of all 131 commits found nothing when this gate landed, and every later change reaches main through a PR, so the two cover the whole history.

To run both locally:

```sh
bash scripts/check-secrets.sh
docker run --rm -v "$PWD:/repo" -w /repo zricethezav/gitleaks:latest git --config .gitleaks.toml --redact .
```

In a worktree, mount the parent folder at the same path instead, so gitleaks can follow the worktree's `.git` file back to the main checkout.

To fix a red run:

- The script names a tracked secret file. Run `git rm --cached <file>`, add it to `.gitignore` and rotate whatever it held.
- The script names an appsettings file. Delete the section or value and put it in Doppler.
- gitleaks found a real secret. Rotate it first. Then drop the commit that added it, because deleting the line in a later commit leaves it in the PR's history and the scan stays red.
- gitleaks flagged something that isn't a secret. Add the file path or the exact value to the allowlist in `.gitleaks.toml`.

## Vulnerable packages

The check restores the `.sln` and runs `dotnet list package --vulnerable --include-transitive`. It fails when any package has a known advisory, including packages that only arrive through another package.

```sh
dotnet restore Laneway.Api.sln
dotnet list Laneway.Api.sln package --vulnerable --include-transitive
```

Restore runs the same NuGet audit, and `TreatWarningsAsErrors` turns its NU1901 to NU1904 warnings into errors. So a vulnerable package usually turns this check red at the Restore step, and `Build & format` goes red with it. The log names the package, the version and the advisory link either way.

To fix a red run:

- A direct package is vulnerable. Bump its version in `Directory.Packages.props` to one the advisory lists as patched.
- A transitive package is vulnerable. Bump the direct package that pulls it in. If no release of that package has the fix yet, pin the patched transitive version in `Directory.Packages.props` and reference it from the project that needs it.

## Trivy filesystem scan

The check runs [Trivy](https://trivy.dev) over the checkout with its vulnerability, secret and misconfiguration scanners. It fails on any high or critical finding. Trivy reads package versions from `Directory.Packages.props`, so it only sees direct packages. `Vulnerable packages` covers the transitive ones.

The results go to code scanning under the `trivy` category, even when the scan fails. Read them in the PR's annotations or in the Security tab.

```sh
docker run --rm -v "$PWD:/src" aquasec/trivy fs --scanners vuln,secret,misconfig --severity HIGH,CRITICAL --skip-dirs '**/bin' --skip-dirs '**/obj' /src
```

To fix a red run:

- A vulnerability. Bump the package, the same way as for `Vulnerable packages`.
- A secret. Treat it like a gitleaks finding in `Secret scan`. Rotate it and drop the commit.
- A misconfiguration. The finding links to the rule. Fix the config it names.

## CodeQL (SAST)

The check runs CodeQL on the C# code with the `security-extended` queries. It reads the source without building it.

The job goes green once the analysis finishes, whatever it found. Findings show in the PR's annotations and in the Security tab. The code scanning rule on main blocks a PR with a CodeQL error or a high or critical alert.

To fix a blocked PR, read the alert. Each one names the query, the file and the line, and links to the query's help with an example fix. If the alert is wrong, dismiss it in the Security tab with a reason.

## Workflow lint

The check runs [zizmor](https://docs.zizmor.sh) over the repo. It audits the workflows and `.github/dependabot.yml`, where it checks that every update entry has a cooldown. Any finding fails the check, and each one shows as an annotation on the file and line.

To run it locally with Docker:

```sh
docker run --rm -v "$PWD:/repo" -w /repo ghcr.io/zizmorcore/zizmor:latest .
```

Without a `GH_TOKEN`, zizmor skips the online audits that CI runs, such as the check for actions with known advisories.

To fix a red run:

- An action is pinned to a tag or branch. Pin it to the full commit SHA of that release and put the version in a trailing comment, like `uses: actions/checkout@<sha> # v7.0.1`.
- A job asks for more permissions than it uses. Drop the extra ones. A job that needs more than `contents: read` declares its own block and repeats `contents: read` in it.
- A `${{ }}` expression sits inside a `run:` script. Pass the value in through `env:` and read the variable in the script.
- You accept the finding. Add `# zizmor: ignore[<audit>]` on the flagged line, followed by the reason. Every exception lives next to the line it excuses.

## Terraform check

The check runs `terraform fmt -check` over `terraform/` and `terraform validate` on `terraform/alerting`. It holds no secrets, has no path filter, and never runs `plan` or `apply`. `init -backend=false` skips the `cloud` block, so it needs no HCP token. [Terraform alerting](#terraform-alerting) plans and applies after the merge. It isn't in the `main` ruleset yet.

To run it locally:

```sh
terraform -chdir=terraform fmt -check -recursive
terraform -chdir=terraform/alerting init -backend=false
terraform -chdir=terraform/alerting validate
```

To fix a red run:

- Format failed. Run `terraform -chdir=terraform fmt -recursive` and commit the result.
- Init failed on a provider version. Run `terraform -chdir=terraform/alerting init -upgrade -backend=false` and commit `.terraform.lock.hcl`.
- Validate failed. The log names the file and line. A rule entry in `rules.tf` with a missing field fails here.

## PR title

The check runs `.husky/csx/commit-lint.csx` on the PR title. Your `commit-msg` hook runs the same script on each commit message, so both follow one rule. A squash merge uses the PR title as the commit on main, which is why CI checks the title and not each commit.

The title must look like `<type>[(scope)][!]: <subject>`, such as `ci: lint the PR title`. The script holds the list of types and the length limits.

The check lives in its own workflow, `pr-title.yml`, which also runs when you edit the PR. Fixing a title reruns only this check. Editing the PR body reruns it too. It reruns nothing in `CI`. A new edit or push cancels the title run still going.

Dependabot's PRs pass without edits. `.github/dependabot.yml` sets `include: scope` on every entry. SDK updates come out as `chore(deps): bump ...` and action updates as `ci(deps): ...`. NuGet updates come out as `chore: Bump ...` with no scope, because the NuGet updater ignores `include: scope` ([dependabot-core#15158](https://github.com/dependabot/dependabot-core/issues/15158)). The scope is optional, and an uppercase subject only warns, so these titles pass too.

To check a title locally, install husky first. `husky exec` won't run without it.

```sh
dotnet tool restore
dotnet husky install
echo 'ci: lint the PR title' > title.txt
dotnet husky exec .husky/csx/commit-lint.csx --args title.txt
```

To fix a red run, edit the PR title. The log lists what failed and shows valid examples. No push needed.

## notify

When any `CI` check fails, `notify` sends one Telegram message. It isn't a required check. It shows as a skipped `notify` on a green run, and it never goes red. When it runs, GitHub names the check `notify / Send`, after the caller job and the called job. `PR title` sends no alert. A cancelled run sends nothing, even when a job failed before a newer push cancelled it.

`notify` has no steps of its own. It calls `.github/workflows/telegram-alert.yml`, the one alert path for every workflow. Its callers are `ci.yml` and `terraform.yml`. The message holds a headline, the workflow name, the failed jobs, the ref, the short SHA, and a link to the run. Nothing else goes in it, so no plan output, findings, or PR title ever reach the chat.

A caller job uses `needs` plus `if: failure()`, passes `toJSON(needs)` and a `message` headline as inputs, and passes `TELEGRAM_BOT_TOKEN` and `TELEGRAM_CHAT_ID` by name. Never `secrets: inherit`. Titles and refs go in through `inputs` and reach the script through `env`, never through `${{ }}` inside `run:`. `ref` and `sha` are optional and default to the run's own ref name and SHA. `ci.yml` passes the PR number with the head branch, and the head SHA, because the run's own SHA is the merge commit.

The alert job's log prints its `env`, and `NEEDS` holds the outputs of every job in `needs`. No `ci.yml` job has outputs today. A caller whose jobs set outputs must keep secrets and plan text out of them.

`telegram-alert.yml` reads both secrets. Doppler syncs them to the repo's Actions secrets and Dependabot secrets, because a Dependabot run reads only Dependabot secrets. A PR from a fork gets no secrets, so `notify` skips the call without a word. On any other run, a missing secret prints `Telegram secrets missing, failure alert not sent` as a warning. If you see that warning, check the Doppler sync. [docs/runbooks/repo-setup.md](runbooks/repo-setup.md) has the setup steps. A failed send prints `Telegram alert failed to send`.

The alert job has no checkout, and harden-runner blocks every host but `api.telegram.org`, since it holds the bot token.

In a workflow with an alert job, a new job goes in that alert job's `needs` list. Otherwise its failures send no alert.

## Terraform alerting

`.github/workflows/terraform-alerting.yml` runs on a push to `main` that touches `terraform/alerting/**`, every Monday at 06:00 UTC as a [drift check](#drift-check), and by hand from the Actions tab. It calls `.github/workflows/terraform.yml`, the one reusable workflow every Terraform root uses, with `root: alerting`. Plan and apply runs for one root never overlap or cancel each other mid-run, so an apply waiting at the gate holds the next push back. GitHub keeps one waiting run per root. A newer push replaces a waiting one, which is safe because the newer run plans all of `main`. [docs/runbooks/terraform.md](runbooks/terraform.md) has the setup.

The run has three jobs:

- `Plan` runs in the `alerting-plan` environment. It reads the Terraform version from `terraform/alerting/versions.tf`, loads the root's secrets with `doppler run`, and runs `scripts/terraform-plan.sh`. The log and the job summary show each changed address and its action, such as `create grafana_folder.laneway`. No attribute values appear, because the repo is public.
- `Apply` runs in the `alerting` environment and waits for the operator to approve. It plans again, compares the change list with `Plan`'s, and fails if they differ. Then it applies that plan. No plan file leaves the runner. When `Plan` finds no changes, `Apply` shows as skipped.
- `notify` sends one Telegram message, `Terraform plan or apply failed for alerting`, when either job fails. GitHub marks `Apply` failed when the operator rejects the gate or the gate times out, so those alert too. `notify` is skipped otherwise.

Both jobs that hold the Doppler token run harden-runner with `egress-policy: block`. The runbook's [allowlist check](runbooks/terraform.md#check-the-egress-allowlist) says how to find a missing host.

To fix a red run:

- `Plan` failed at init with a 401. `TF_TOKEN_app_terraform_io` in Doppler expired or was revoked. Rotate it, as the runbook says.
- `Plan` failed before init, at `doppler run`. The `DOPPLER_TOKEN` secret on the environment expired. Generate a new one and set it on both environments.
- `Plan` failed on a provider error, such as a 401 from Grafana. The provider token in Doppler expired or lost a role. The error names the resource.
- A step failed with a connection error to a host. harden-runner blocked it. Add the host to `ALLOWED_ENDPOINTS` at the top of `terraform.yml`.
- `Plan` failed with a lock error. Another run or a local run holds the HCP state lock. Wait for it, or unlock the workspace in HCP once you're sure nothing is running.
- `Apply` failed on `The change list moved since the Plan job`. Someone changed the resources between the two plans, often in the Grafana UI. Read both summaries, then run the workflow again by hand so `Plan` sees the new state.
- `Apply` failed partway. Some resources changed and others didn't. HCP holds the state of what did change. Fix forward with a PR, or rerun the workflow by hand if the cause was outside the repo.

### Drift check

The Monday run sets `drift`, and so does a manual run with the `drift` box ticked. `Plan` runs `scripts/terraform-plan.sh alerting --drift`, which plans `main` with `-detailed-exitcode` in `alerting-plan`. The log and summary follow the same address-only rule. Any change, or any error, fails `Plan`. `Apply` never runs on a drift check, and `notify` sends `Terraform drift check failed for alerting`.

A Dependabot provider bump changes `terraform/alerting/.terraform.lock.hcl`, so merging it starts a normal run that stops at the gate.

Drift runs have their own concurrency group, `terraform-alerting-drift`, so a drift run never replaces a waiting push run or waits behind an `Apply` held at the gate. The HCP state lock keeps a drift plan and an apply from running at once. A drift run that lands between `Plan` and an approved `Apply` reports the pending change as drift. Approve or reject the gate, then run the drift check again by hand.

When it fires:

- The summary lists changes. An output shows as `output.<name>`. Someone edited Grafana in the UI, or nobody applied a merged change. Read the addresses. To keep the code, run the workflow by hand without `drift` and approve the gate, which puts Grafana back. To keep the UI edit, copy it into `terraform/alerting` in a PR.
- `Plan` failed with an error and no list. Read the red-run fixes above. A 401 usually means one of the three 90-day tokens expired, so the weekly run doubles as an expiry warning.

## Local hooks

Restoring the solution installs the husky hooks, so a fresh clone or a new worktree runs them with no extra step. Until that first restore, every commit in a new worktree fails with `.husky/_/husky.sh: No such file or directory`. Run `dotnet restore Laneway.Api.sln` and commit again. Restore skips the install once `.husky/_/husky.sh` exists. To reinstall, delete `.husky/_` and restore.

| Hook | Runs | Mirrors |
| --- | --- | --- |
| `pre-commit` | `dotnet format whitespace` on staged `.cs` files, then `scripts/check-secrets.sh` | part of `Build & format`, and the script half of `Secret scan` |
| `commit-msg` | `.husky/csx/commit-lint.csx` on the message | `PR title` |
| `pre-push` | `dotnet format Laneway.Api.sln --verify-no-changes` | the format step of `Build & format` |

The rest stays in CI: the build, gitleaks, both package scans, CodeQL and zizmor. They are slow or need tools a laptop may not have.

`git commit --no-verify` and `git push --no-verify` skip the hooks for one run. `HUSKY=0` skips every hook and also stops restore from installing them. CI sets `HUSKY=0`, so runners don't install hooks. Skipping a hook skips nothing in CI. Every check still runs on the PR.
