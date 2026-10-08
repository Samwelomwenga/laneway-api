# CI

CI runs no tests. A green PR means the code builds and passes the checks below. Run `dotnet test` before you push.

Every PR to main runs two workflows. `.github/workflows/ci.yml` holds the `CI` workflow with every check but one. `.github/workflows/pr-title.yml` holds `PR title`. A new push to the same PR cancels the run still going for the older commit. Each section heading is the check name GitHub shows on the PR.

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

## PR title

The check runs `.husky/csx/commit-lint.csx` on the PR title. Your `commit-msg` hook runs the same script on each commit message, so both follow one rule. A squash merge uses the PR title as the commit on main, which is why CI checks the title and not each commit.

The title must look like `<type>[(scope)][!]: <subject>`, with one of the types `build`, `feat`, `ci`, `chore`, `docs`, `fix`, `perf`, `refactor`, `revert`, `style` or `test`. It must be 10 to 100 characters and must not end with a period.

The check lives in its own workflow, `pr-title.yml`, which also runs when you edit the PR. Fixing a title reruns only this check. Editing the PR body reruns it too, and nothing in `CI`.

To check a title locally:

```sh
dotnet tool restore
echo 'ci: lint the PR title' > title.txt
dotnet husky exec .husky/csx/commit-lint.csx --args title.txt
```

To fix a red run, edit the PR title. The log lists what failed and shows valid examples. No push needed.

## Workflow lint

The check runs [zizmor](https://docs.zizmor.sh) over the repo. It audits the workflows and, once it exists, `dependabot.yml`. Any finding fails the check, and each one shows as an annotation on the file and line.

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
