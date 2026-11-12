# CI

PRs run no tests. A green PR means the code builds and passes the checks below. The full suite runs after the merge, on every push to main, as [main.yml](#mainyml) says. Run `dotnet test` before you push, so main doesn't go red.

Every PR to main runs two workflows. `.github/workflows/ci.yml` holds the `CI` workflow with every check but one. `.github/workflows/pr-title.yml` holds `PR title`. A new push to the same PR cancels the run still going for the older commit. Each section heading is the check name GitHub shows on the PR, except [main.yml](#mainyml), whose jobs run after the merge, [release.yml](#releaseyml), which runs on a tag, [rescan.yml](#rescanyml), which runs on a schedule, and [Terraform alerting](#terraform-alerting) and [Terraform vm](#terraform-vm), which run on `main` after a merge. The `main` ruleset requires these names, and [docs/runbooks/repo-setup.md](runbooks/repo-setup.md) says what happens if one changes.

## Build & format

The check restores `Laneway.Api.sln`, runs `dotnet format` on it and builds it in Release. Then it asks EF whether the model differs from the latest migration's snapshot, and fails if it does.

```sh
dotnet restore Laneway.Api.sln
dotnet format Laneway.Api.sln --verify-no-changes --no-restore
dotnet build Laneway.Api.sln --no-restore -c Release
dotnet tool restore
CONNECTIONSTRINGS__DEFAULTCONNECTION='Host=localhost;Database=laneway' \
  dotnet ef migrations has-pending-model-changes --project Laneway.Api.csproj --no-build --configuration Release
```

`dotnet ef` builds the app's host to find `ApplicationDbContext`, and `Program.cs` stops at startup without a connection string. The step passes a dummy one. EF compares the model to the snapshot in memory and never connects. The tool manifest pins `dotnet-ef`, so CI and your machine run the same version. Dependabot bumps it in the `aspnetcore-efcore` group, so it moves with the EF packages. Write `--configuration` in full, because `-c` means `--context` to `dotnet ef`.

Every `dotnet` command names the `.sln`, because the repo root also holds `Laneway.Api.csproj` and `dotnet` won't pick between them. `dotnet ef` is the exception. It works on one project, so it names `Laneway.Api.csproj`.

`global.json` pins the SDK, so CI and your machine load the same format and analyzer rules. If `dotnet --version` doesn't start with `10.0.4`, install that SDK band.

To fix a red run:

- The format step failed. Run `dotnet format Laneway.Api.sln` and commit what it changes.
- The build step failed on a warning. `TreatWarningsAsErrors` is on in `Directory.Build.props`, so any compiler warning fails the build. Fix the warning. The log names the file and line.
- The build step failed on a CA rule. It shouldn't. `CodeAnalysisTreatWarningsAsErrors` is false, so CA warnings don't fail the build. If one does, someone changed the props.
- The pending model changes step failed with `Changes have been made to the model since the last migration`. An entity or its configuration changed with no migration. Run `dotnet ef migrations add <Name>`, strip the BOM from the new `.cs` files so `dotnet format` passes, and commit the migration with the change.

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

The check runs `terraform fmt -check` over `terraform/` and `terraform validate` on every root under it. It holds no secrets, has no path filter, and never runs `plan` or `apply`. `init -backend=false` skips the `cloud` block, so it needs no HCP token. [Terraform alerting](#terraform-alerting) plans and applies after the merge.

To run it locally:

```sh
terraform -chdir=terraform fmt -check -recursive
for root in terraform/*/; do
  terraform -chdir="$root" init -backend=false && terraform -chdir="$root" validate || break
done
```

To fix a red run:

- Format failed. Run `terraform -chdir=terraform fmt -recursive` and commit the result.
- Init failed on a provider version. Run `terraform -chdir=terraform/<root> init -upgrade -backend=false` on the failing root and commit its `.terraform.lock.hcl`.
- Validate failed. The log names the file and line. In `alerting`, a rule entry in `rules.tf` with a missing field fails here.

## Image build / Build and scan

The check builds Laneway's container image and scans it with Trivy. It runs on every PR, fork PRs included, with no path filter. GitHub names a check from a reusable workflow `<caller job> / <called job>`, so the check reports as `Image build / Build and scan`.

The `Image build` job in `ci.yml` has no steps of its own. It calls `.github/workflows/image.yml`, the one workflow that builds the image. [release.yml](#releaseyml) calls it too, with push and attest on. On a PR both stay off, so nothing leaves the runner.

`image.yml` declares no permissions. Its job takes whatever the caller job grants. GitHub checks a called workflow's permissions when the run starts, skipped steps included, so a `packages: write` in `image.yml` would make every PR grant it, and a fork PR can't. `Image build` grants `contents: read` and `security-events: write`. The release grants `packages`, `id-token` and `attestations` write on top. A new caller grants only what its run needs.

With push on, the workflow tags the image Trivy scanned, pushes it to `ghcr.io/<owner>/laneway-api` as the version and as `sha-<short>`, and reads the registry digest back. It never tags `latest`. With attest on, `actions/attest` writes SLSA provenance for that digest, and a second call attaches a CycloneDX SBOM that Trivy makes from the same image. A pushed build skips the Actions cache, so no layer saved by a PR build ends up in a published image.

The workflow builds the `Dockerfile` for `linux/arm64`, the platform of the production VM. It builds on a native arm64 runner and loads the image into the runner's Docker. The build includes the `migrations` stage, so a broken migration bundle turns the check red too. Buildx caches layers in the Actions cache, so a PR that only changes code skips the restore.

The workflow's `version` input becomes the Dockerfile's `VERSION` build arg, and the publish stamps it on the assembly. The app reports it as `service.version` on every log, span and metric it exports. A PR build uses the default `0.0.0-pr`. A `service.version` in `OTEL_RESOURCE_ATTRIBUTES` wins over the stamped one.

Then Trivy scans the loaded image twice, with its vulnerability and secret scanners.

- The first run reports every high or critical finding, fixable or not, to code scanning under the `trivy-image` category. It never fails.
- The second run is the gate. It fails on any high or critical finding that has a fix. A finding with no fixed version yet doesn't block, since nothing in this repo can fix it. It still shows in code scanning from the first run.

The log shows only whether the gate passed and the image digest. Read the findings in the PR's annotations or in the Security tab, never in the log, since the repo is public.

To run it locally:

```sh
docker build -t laneway-api:scan .
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v "$PWD:/src" -w /src aquasec/trivy image --scanners vuln,secret --severity HIGH,CRITICAL --ignore-unfixed laneway-api:scan
```

To fix a red run:

- The build failed. The log names the Dockerfile step. A failed `dotnet publish` fails the same way `Build & format` does. A failed `dotnet ef migrations bundle` means the migrations don't compile, or `Program.cs` stops before EF can build the host.
- A vulnerable package in the app. Bump it, the same way as for `Vulnerable packages`.
- A vulnerable package in the base image. Microsoft rebuilds the .NET images with OS fixes, and Dependabot bumps both digests in one `dotnet-images` PR each Monday. Dependabot sends no security updates for Docker images, so for an urgent fix, look up the new digest with `docker buildx imagetools inspect mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra` and edit the `FROM` line by hand.
- A secret in the image. Treat it like a gitleaks finding in `Secret scan`. Rotate it, and check `.dockerignore` keeps the file out of the build.
- You accept the finding. Add its ID to `.trivyignore` with an expiry date and a reason on the line above, like this:

  ```text
  # Reachable only through X, which Laneway doesn't call. Recheck when the base image moves.
  CVE-2026-12345 exp:2026-12-31
  ```

  After the expiry date the gate fails on it again. Every entry needs both, so someone looks at an accepted finding again instead of letting it hide. `Trivy filesystem scan` reads the same file.

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

`notify` has no steps of its own. It calls `.github/workflows/telegram-alert.yml`, the one alert path for every workflow. `ci.yml`, `main.yml`, `release.yml`, `rescan.yml` and `terraform.yml` call it. The message holds a headline, the workflow name, the failed jobs, the ref, the short SHA, and a link to the run. Nothing else goes in it, so no plan output, findings, or PR title ever reach the chat.

A caller job uses `needs` plus `if: failure()`, passes `toJSON(needs)` and a `message` headline as inputs, and passes `TELEGRAM_BOT_TOKEN` and `TELEGRAM_CHAT_ID` by name. Never `secrets: inherit` on a call to `telegram-alert.yml`. Titles and refs go in through `inputs` and reach the script through `env`, never through `${{ }}` inside `run:`. `ref` and `sha` are optional and default to the run's own ref name and SHA. `ci.yml` passes the PR number with the head branch, and the head SHA, because the run's own SHA is the merge commit.

The alert job's log prints its `env`, and `NEEDS` holds the outputs of every job in `needs`. In `release.yml` those are the version and the image digest, both public. A caller whose jobs set outputs must keep secrets and plan text out of them.

`telegram-alert.yml` reads both secrets. Doppler syncs them to the repo's Actions secrets and Dependabot secrets, because a Dependabot run reads only Dependabot secrets. A PR from a fork gets no secrets, so `notify` skips the call without a word. On any other run, a missing secret prints `Telegram secrets missing, failure alert not sent` as a warning. If you see that warning, check the Doppler sync. [docs/runbooks/repo-setup.md](runbooks/repo-setup.md) has the setup steps. A failed send prints `Telegram alert failed to send`.

The alert job has no checkout, and harden-runner blocks every host but `api.telegram.org`, since it holds the bot token.

In a workflow with an alert job, a new job goes in that alert job's `needs` list. Otherwise its failures send no alert.

## main.yml

Every push to main runs `.github/workflows/main.yml`, the `Main` workflow. It has three jobs: `Tests`, `CodeQL (SAST)` and `Trivy filesystem scan`. None of them is a required check, since they run after the merge. A failed job sends a Telegram alert through `notify`, the same way `ci.yml` does, with `Laneway main failed` as the headline.

Runs on main share the concurrency group `main` with `cancel-in-progress: false`, so a running job is never cut short. A new push waits for the run before it. GitHub keeps only one waiting run per group, though. If a third push lands while one run is going and one is waiting, GitHub cancels the waiting run, and that commit gets no verdict and no alert.

[release.yml](#releaseyml) reads the verdict and refuses to build a commit whose `main.yml` run isn't green. To release a commit whose run was cancelled, rerun that run from the Actions tab first.

### Tests

The job runs the full suite, the same `dotnet test` you run locally. Testcontainers starts `postgres:18` in the runner's Docker. Storage comes from the local Supabase stack. The workflow pins the Supabase CLI version in its `supabase/setup-cli` step. `supabase start -x` leaves out every service storage doesn't need, so only the database, Kong, storage-api and auth (`gotrue`) start. Storage runs fine without `gotrue`, but `supabase status` prints no keys while it is stopped. The test step reads `API_URL` and `SERVICE_ROLE_KEY` from `supabase status -o env` and hands them to the tests as `Supabase__Url` and `Supabase__ServiceRoleKey`. They are the local stack's public demo keys, not secrets.

The test step sets `DOTNET_USE_POLLING_FILE_WATCHER=1`. Every test host watches the appsettings files. Without polling, a full run uses up the inotify instances and the later tests fail in 1 ms each.

Harden-runner stays on audit for this job. Testcontainers and the CLI pull images from several registries, and that list is hard to pin down up front.

Dependabot doesn't bump the CLI version, since it is an input to the action and not a dependency Dependabot reads. Bump it by hand. Then check that every name after `-x` is still in the list `supabase start --help` prints, because the CLI renames services now and then.

To run it locally, start the stack with `supabase start`, put `Supabase:Url` and `Supabase:ServiceRoleKey` in the test project's user secrets, and run:

```sh
DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test Laneway.Api.sln
```

To fix a red run:

- A test failed. The log names the test and the failed assertion. Run its class locally with `dotnet test Laneway.Api.sln --filter <ClassName>`, and fix it in a new PR.
- Many tests failed in 1 ms each, with `The configured user limit (1024) on the number of inotify instances has been reached`. The polling variable is missing from the test step. Put it back.
- `Start storage` failed. The log names the service. If the CLI says a name after `-x` is unknown, take the current name from `supabase start --help`.
- Every test failed with `The test run needs Supabase:Url and Supabase:ServiceRoleKey`. `supabase status -o env` printed no `SERVICE_ROLE_KEY`. Check that `gotrue` isn't in the `-x` list. If it isn't, the CLI renamed the variables. Run `supabase status -o env` locally on the pinned version and fix the names in the test step.

### Scans on main

The `CodeQL (SAST)` and `Trivy filesystem scan` jobs are copies of the ones in `ci.yml`. Change both files together. Their results give code scanning a main baseline. GitHub compares each PR's analysis to the latest one on main, so a PR shows only the alerts it adds. That needs the same category on both sides. Trivy uploads under `trivy` in both files. CodeQL uploads under `.github/workflows/main.yml:codeql` in both, the name GitHub gave main's first analysis.

Never change the CodeQL category in only one file. Changing it in both doesn't help either, because main keeps its analyses under the old name. Code scanning then reports `1 configuration not found` on every PR and leaves `CodeQL` neutral. The code scanning rule in the `main` ruleset doesn't pass, and the PR can't merge. To rename it anyway, turn off that rule for the rename PR's merge. Then delete the old configuration under Security, Code scanning, Tool status.

`CodeQL (SAST)` stays green with findings here too, so a new CodeQL alert on main shows only in the Security tab. A red Trivy job on main means a finding got merged, or a new advisory came out for a package main already uses. Fix it in a PR, with the steps under [Trivy filesystem scan](#trivy-filesystem-scan).

## release.yml

Pushing a `vX.Y.Z` tag runs `.github/workflows/release.yml`, the `Release` workflow. Nothing else releases. A merge doesn't, and the workflow has no `workflow_dispatch`. The `release-tags` ruleset lets only the repo admin create a `v*` tag, as [docs/runbooks/repo-setup.md](runbooks/repo-setup.md#ruleset-release-tags) says.

Versions follow SemVer from `v0.1.0`. A major bump marks a breaking API change. There are no prerelease tags.

To cut a release, tag a commit on main whose `main.yml` run is green, and push the tag:

```sh
git tag v0.1.0 <sha>
git push origin v0.1.0
```

The workflow has four jobs.

1. `Guards` fails unless the tag is exactly `vX.Y.Z`, `git merge-base --is-ancestor` finds the commit on main, and a `main.yml` run on that commit concluded `success`. It never waits. A missing, running or red run fails the release at once.
2. `Image` calls [image.yml](#image-build--build-and-scan) with push and attest on and the version without its `v`. The image gets the same build and Trivy gate as on a PR, then goes to GHCR as `1.2.3` and `sha-<short>`, with provenance and an SBOM attested. No other PR check runs again.
3. `GitHub release` creates a release on the tag with GitHub's generated notes. The body names the image as `ghcr.io/<owner>/laneway-api@sha256:...`. The deploy reads that digest from the latest release and nothing else.
4. `notify` sends a Telegram alert, `Laneway release failed`, when any job fails.

There is no environment gate. Only the admin can push the tag, and the build holds no secrets. Releases share the concurrency group `release` with `cancel-in-progress: false`, so a running release is never cut short.

A tag is never moved or reused. A failed release burns its number, and the fix ships under the next one. What a failure leaves behind depends on where it stopped:

- `Guards` failed. The workflow built nothing. The tag points at a commit with no image.
- The Trivy gate failed. The workflow pushed nothing. The tag has no image.
- Attest failed. The image is on GHCR with no attestation. The deploy verifies the attestation before it pulls, so it refuses this image.
- `GitHub release` failed. The image is on GHCR with its attestations, but no release names it, so the deploy never sees it.

To check a release's image by hand:

```sh
gh attestation verify oci://ghcr.io/<owner>/laneway-api@sha256:<digest> \
  -R <owner>/laneway-api \
  --signer-workflow <owner>/laneway-api/.github/workflows/image.yml
```

The signer is `image.yml`, not `release.yml`, because `image.yml` is the reusable workflow that built and attested the image.

The GHCR package is public, so anyone can pull and verify it. GitHub can't make a public package private again.

To fix a red run:

- `Check the tag name` failed. The tag isn't `vX.Y.Z`. Tag the commit again with a valid name.
- `Check the commit is on main` failed. The tag points at a branch commit. Merge the change first, then tag the merge commit on main with the next version.
- `Check main.yml passed on the commit` failed. Open the commit's `main.yml` run. If it was still going, wait for it to finish. If it was cancelled, rerun it. If it was red, fix main in a PR. Then tag with the next version.
- The image job failed. Fix it like a red [Image build / Build and scan](#image-build--build-and-scan), in a PR, then tag with the next version.
- `GitHub release` failed. The image is on GHCR already. Tag with the next version rather than writing the release by hand, so every release goes through the same steps.

## rescan.yml

Every Monday at 06:00 UTC, `.github/workflows/rescan.yml` scans the image in the latest GitHub release again, so a CVE published after the release still reaches you. It reads the digest from the release body, never from a tag or a GHCR listing, so it scans exactly what the deploy runs. It pulls that digest from GHCR and never rebuilds.

The scan matches the gate in `image.yml`. Trivy runs twice with its vulnerability and secret scanners. The first run reports every high or critical finding to code scanning under `trivy-image`. The second fails on any high or critical finding with a fix. `.trivyignore` on main applies, as on a PR. The log shows only the tag, the digest and whether the gate passed. A failed run alerts as `Laneway release rescan failed`.

Harden-runner blocks every host but GitHub, GHCR, and where Trivy gets its binary and its vulnerability DB. Trivy's DB cache is off, so the run downloads the DB each week and the Actions cache hosts stay off the list. If Trivy moves its DB to a new host, the run goes red with a blocked connection. Add the host to `allowed-endpoints`.

To run it now, open Actions, `Rescan`, and use Run workflow.

GitHub turns off scheduled workflows in a public repo after 60 days with no commits. If main goes quiet that long, the rescan stops without a word, and GitHub only emails about it. Turn it back on from the Actions tab, or push a commit.

To fix a red run:

- The gate failed. A fixable finding showed up in the released image. Read it in the Security tab under `trivy-image`. Fix it the way [Image build / Build and scan](#image-build--build-and-scan) says. A base image fix usually means merging Dependabot's `dotnet-images` PR. Then tag a new release. The next rescan reads the new release and goes green.
- `Read the latest release` failed with `names no image digest`. The latest release has no `ghcr.io/...@sha256:...` line, so `release.yml` didn't write it. Delete it, or tag a new release.
- The pull failed. Check that the package is still public.
- A connection was blocked. Harden-runner names the host. Add it to `allowed-endpoints` if it belongs to GitHub, GHCR or Trivy.

## Terraform alerting

`.github/workflows/terraform-alerting.yml` runs on a push to `main` that touches `terraform/alerting/**`, every Monday at 06:00 UTC as a [drift check](#drift-check), and by hand from the Actions tab. It calls `.github/workflows/terraform.yml`, the one reusable workflow every Terraform root uses, with `root: alerting`. Plan and apply runs for one root never overlap or cancel each other mid-run, so an apply waiting at the gate holds the next push back. GitHub keeps one waiting run per root. A newer push replaces a waiting one, which is safe because the newer run plans all of `main`. [docs/runbooks/terraform.md](runbooks/terraform.md) has the setup.

The run has three jobs:

- `Plan` runs in the `alerting-plan` environment. It reads the Terraform version from `terraform/alerting/versions.tf`, loads the root's secrets with `doppler run`, and runs `scripts/terraform-plan.sh`. The log and the job summary show each changed address and its action, such as `create grafana_folder.laneway`. No attribute values appear, because the repo is public.
- `Apply` runs in the `alerting` environment and waits for the operator to approve. It plans again, compares the change list with `Plan`'s, and fails if they differ. Then it applies that plan. No plan file leaves the runner. When `Plan` finds no changes, `Apply` shows as skipped.
- `notify` sends one Telegram message, `Terraform plan or apply failed for alerting`, when either job fails. GitHub marks `Apply` failed when the operator rejects the gate or the gate times out, so those alert too. `notify` is skipped otherwise.

The caller passes `secrets: inherit`. When a caller passes secrets by name, GitHub hands the called jobs an empty `DOPPLER_TOKEN` from their environment, as [actions/runner#4453](https://github.com/actions/runner/issues/4453) describes. The repo-level secrets it passes along are the two Telegram values, which `notify` needs anyway, and Doppler's project, config and environment names. Keep it that way. A new repo-level secret reaches `terraform.yml` too.

Both jobs that hold the Doppler token run harden-runner with `egress-policy: block`. The runbook's [allowlist check](runbooks/terraform.md#check-the-egress-allowlist) says how to find a missing host.

To fix a red run:

- `Plan` failed at init with a 401. `TF_TOKEN_APP_TERRAFORM_IO` in Doppler expired or was revoked. Rotate it, as the runbook says.
- `Plan` failed before init, at `doppler run`. The `DOPPLER_TOKEN` secret on the environment expired. Generate a new one and set it on both environments.
- `Plan` failed on a provider error, such as a 401 from Grafana. The provider token in Doppler expired or lost a role. The error names the resource.
- A step failed with a connection error to a host. harden-runner blocked it. Add the host to `ALLOWED_ENDPOINTS` at the top of `terraform.yml`.
- `Plan` failed with a lock error. Another run or a local run holds the HCP state lock. Wait for it, or unlock the workspace in HCP once you're sure nothing is running. A lock left by a failed or cancelled run needs Force unlock, under the workspace's Settings, Locking.
- A job hit its timeout, 15 minutes for `Plan` or 30 for `Apply`. A healthy `Plan` takes under a minute. The stop leaves the HCP lock held, so force unlock it. If `Apply` timed out, it may have created resources that HCP state doesn't hold. Check Grafana for them, delete them, then run the workflow again.
- `Apply` failed on `The change list moved since the Plan job`. Someone changed the resources between the two plans, often in the Grafana UI. Read both summaries, then run the workflow again by hand so `Plan` sees the new state.
- `Apply` failed partway. Some resources changed and others didn't. HCP holds the state of what did change. Fix forward with a PR, or rerun the workflow by hand if the cause was outside the repo.
- `Apply` failed with `Failed to save state`. Terraform changed resources but couldn't upload the new state, so HCP holds a pending state version with nothing in it. Every later run then plans and hangs at the end, retrying `unable to unlock workspace while state version upload is still pending` until it times out. HCP has no way to discard a pending version, and only a newer state replaces it. To recover:
  1. Fix the cause first, such as a host missing from `ALLOWED_ENDPOINTS`.
  2. List what the apply created in Grafana. Delete each one, or import it once the state is clean.
  3. Force unlock the workspace.
  4. Write a state file with no resources, `"serial": 2` and a fresh `lineage`, and push it with `doppler run --project laneway-<root> --config prd -- terraform -chdir=terraform/<root> state push <file>`. This creates a finalized version that replaces the pending one.
  5. Run the workflow by hand. `Plan` should finish in under a minute.

### Drift check

The Monday run sets `drift`, and so does a manual run with the `drift` box ticked. `Plan` runs `scripts/terraform-plan.sh alerting --drift`, which plans `main` with `-detailed-exitcode` in `alerting-plan`. The log and summary follow the same address-only rule. Any change, or any error, fails `Plan`. `Apply` never runs on a drift check, and `notify` sends `Terraform drift check failed for alerting`.

A Dependabot provider bump changes `terraform/alerting/.terraform.lock.hcl`, so merging it starts a normal run that stops at the gate.

Drift runs have their own concurrency group, `terraform-alerting-drift`, so a drift run never replaces a waiting push run or waits behind an `Apply` held at the gate. The HCP state lock keeps a drift plan and an apply from running at once. A drift run that lands between `Plan` and an approved `Apply` reports the pending change as drift. Approve or reject the gate, then run the drift check again by hand.

When it fires:

- The summary lists changes. An output shows as `output.<name>`. Someone edited Grafana in the UI, or nobody applied a merged change. Read the addresses. To keep the code, run the workflow by hand without `drift` and approve the gate, which puts Grafana back. To keep the UI edit, copy it into `terraform/alerting` in a PR.
- `Plan` failed with an error and no list. Read the red-run fixes above. A 401 usually means one of the three 90-day tokens expired, so the weekly run doubles as an expiry warning.

## Terraform vm

`.github/workflows/terraform-vm.yml` calls `terraform.yml` with `root: vm` on a push to `main` that touches `terraform/vm/**`, every Monday at 06:00 UTC as a drift check, and by hand. Everything in [Terraform alerting](#terraform-alerting) applies, with `vm-plan` and `vm` as the environments and Doppler `laneway-vm` `prd` as the secrets.

The caller also passes `id-token-audience: https://cloud.oracle.com`. Before each Terraform step, `scripts/github-id-token.sh` writes a fresh GitHub OIDC token with that audience to `$RUNNER_TEMP/id-token`, and the OCI provider trades it for a session token. Both callers grant `id-token: write`, because a called job can't ask for more than its caller gives. Alerting passes no audience, so its jobs never request a token.

If it fails:

- `Plan` failed with a 401 or `NotAuthenticated` from the token exchange. The client secret in `laneway-vm` is wrong, someone regenerated it in the identity domain, or the trust's claim checks don't match. The trust checks `repository`, `ref`, `job_workflow_ref` and `aud`, so a run from any branch but `main` fails here by design.
- `Plan` failed with `NotAuthorizedOrNotFound` on every resource. The exchange worked but the CI policy's `identityfederateddomainapp` condition didn't match. The fallback is an API key user in `oci-bootstrap`, with its key in `laneway-vm`.

## Local hooks

Restoring the solution installs the husky hooks, so a fresh clone or a new worktree runs them with no extra step. Until that first restore, every commit in a new worktree fails with `.husky/_/husky.sh: No such file or directory`. Run `dotnet restore Laneway.Api.sln` and commit again. Restore skips the install once `.husky/_/husky.sh` exists. To reinstall, delete `.husky/_` and restore.

| Hook | Runs | Mirrors |
| --- | --- | --- |
| `pre-commit` | `dotnet format whitespace` on staged `.cs` files, then `scripts/check-secrets.sh` | part of `Build & format`, and the script half of `Secret scan` |
| `commit-msg` | `.husky/csx/commit-lint.csx` on the message | `PR title` |
| `pre-push` | `dotnet format Laneway.Api.sln --verify-no-changes` | the format step of `Build & format` |

The rest stays in CI: the build, gitleaks, both package scans, CodeQL and zizmor. They are slow or need tools a laptop may not have.

`git commit --no-verify` and `git push --no-verify` skip the hooks for one run. `HUSKY=0` skips every hook and also stops restore from installing them. CI sets `HUSKY=0`, so runners don't install hooks. Skipping a hook skips nothing in CI. Every check still runs on the PR.
