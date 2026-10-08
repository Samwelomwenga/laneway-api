# CI

CI runs no tests. A green PR means the code builds and passes the checks below. Run `dotnet test` before you push.

Every PR to main runs the `CI` workflow in `.github/workflows/ci.yml`. A new push to the same PR cancels the run still going for the older commit. Each section heading is the check name GitHub shows on the PR.

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

## Workflow lint

The check runs [zizmor](https://docs.zizmor.sh) over `.github/`. It covers the workflows and, once it exists, `dependabot.yml`. Any finding fails the check, and each one shows as an annotation on the file and line.

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
