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
