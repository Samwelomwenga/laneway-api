# Repo setup

Everything on this page lives in GitHub and Doppler settings, not in the repo. No file exports the rulesets, so this page is the only record of them. If you change a setting by hand, change it here too.

[docs/ci.md](../ci.md) explains each required check and how to fix it when it goes red.

## Renaming a check blocks every PR

The `main` ruleset requires seven checks by name. If a job's `name:` changes in a workflow and the ruleset still lists the old name, GitHub waits for a check that never reports. Every open PR stays blocked, this one included.

A PR runs the workflow from its own branch, so the rename PR reports the new name only. Rename in this order:

1. Open the rename PR and wait for the new name to report.
2. In the `main` ruleset, swap the old name for the new one.
3. Merge the rename PR.
4. Rebase every other open PR. Until it picks up the new workflow, it reports the old name and stays blocked.

The same applies to a new required check. Add it to the ruleset only after it has reported on at least one PR. A required check that has never reported blocks every PR.

## Rollout order

GitHub's evaluate mode for rulesets is Enterprise only, so there is no dry run. Follow this order once.

1. Merge the build PR with both rulesets off.
2. Set up [Doppler and Telegram](#doppler-and-telegram).
3. Open a test PR. Confirm these checks report, spelled exactly like this:
   - `Build & format`
   - `Secret scan`
   - `Vulnerable packages`
   - `Trivy filesystem scan`
   - `CodeQL (SAST)`
   - `Workflow lint`
   - `PR title`
4. Turn on the rest of the [repo settings](#repo-settings).
5. Rewrite `DEfault_RULES` as the [`main` ruleset](#ruleset-main).
6. Create the [`main-merge-lock` ruleset](#ruleset-main-merge-lock).
7. On the test PR, push a change that turns one check red. The merge button must stay blocked, and a Telegram alert must arrive. Revert it, and squash merge once everything is green. The merge button must offer only squash, and the commit on main must take the PR title.
8. Try `git push origin main` from a local checkout. GitHub must refuse it.

## Repo settings

Settings, General, Pull Requests:

- Allow squash merging only. Turn off merge commits and rebase merging.
- Default squash commit title: pull request title (`PR_TITLE`). `PR title` checks the title because it becomes the commit on main.
- Default squash commit message: pull request description (`PR_BODY`).
- Automatically delete head branches: on.

Settings, Actions, General:

- Require actions to be pinned to a full-length commit SHA: on. zizmor's `Workflow lint` catches an unpinned action on the PR. This setting refuses to run one at all.

Settings, Advanced Security:

- Dependabot alerts: on.
- Dependabot security updates: on. Version updates come from `.github/dependabot.yml` and need no toggle.
- Code scanning default setup: leave off. `ci.yml` runs CodeQL in advanced setup, and the two can't run side by side.

## Ruleset `main`

Settings, Rules, Rulesets. Edit `DEfault_RULES` in place and rename it to `main`.

- Enforcement status: Active.
- Bypass list: empty. Not even the owner skips these rules.
- Target branches: the default branch.

Rules:

- Restrict deletions.
- Require linear history.
- Require signed commits. GitHub signs every squash merge, so this costs nothing.
- Block force pushes.
- Require a pull request before merging:
  - Required approvals: 0.
  - Dismiss stale pull request approvals when new commits are pushed: on.
  - Require conversation resolution before merging: on.
  - Allowed merge methods: squash only.
- Require status checks to pass:
  - Require branches to be up to date before merging: off. With it on, every merge forces every open PR to rebase and rerun. With it off, two PRs that pass alone can break main together. CI runs no tests, so it only catches a break that stops the build, and only on the next PR.
  - Add the seven checks from step 3 of the rollout. Pin each one to the GitHub Actions app, integration id `15368`. Then a commit status with the same name from some other source can't satisfy the check.
- Require code scanning results:
  - Tool: CodeQL.
  - Alerts: errors. Security alerts: high or higher.
  - The `CodeQL (SAST)` job stays green with findings, so this rule does the blocking. Trivy isn't listed, because its job fails on its own.

Leave out "Require code quality results". GitHub Code Quality is a paid per-committer product, and this personal-account repo has no Code quality settings page. With nothing reporting, the rule would block every PR.

## Ruleset `main-merge-lock`

Create a new ruleset.

- Name: `main-merge-lock`.
- Enforcement status: Active.
- Target branches: the default branch.
- Bypass list: the Repository admin role, in pull requests only mode.

Rules:

- Restrict creations.
- Restrict updates.

Restrict updates with no bypass would stop every merge, the owner's too. A bypass on `main` would fix that, but it would also let the owner skip the required checks. So these two rules live here, where only the admin role gets past them, and only through a PR. Rulesets stack, so that PR still has to pass everything in `main`.

## Doppler and Telegram

The `notify` section of [docs/ci.md](../ci.md#notify) says what the alert does and which two secrets it reads. Doppler holds them and syncs them to GitHub. The workflow never calls Doppler.

1. Create the bot. In Telegram, message `@BotFather`, send `/newbot` and keep the token it returns.
2. Send `/start` to the bot from the chat that should get alerts. In a group, mention the bot instead, since a bot in privacy mode ignores plain group messages. Then open `https://api.telegram.org/bot<token>/getUpdates` and copy `message.chat.id`.
3. In Doppler, create a project `laneway-api` with a `ci` config. Add `TELEGRAM_BOT_TOKEN` and `TELEGRAM_CHAT_ID` and nothing else. A sync pushes every secret in its config, so anything else added here lands in GitHub too.
4. In the `ci` config, add a GitHub integration sync to `Samwelomwenga/laneway-api`, target Actions secrets.
5. Add a second sync from the same config, target Dependabot secrets.
6. Check that both secrets appear under Settings, Secrets and variables, in Actions and in Dependabot.

Step 7 of the rollout tests it. If no message arrives, check the `notify` log for the missing-secrets warning.

## Dependabot checks

`.github/dependabot.yml` runs weekly on Mondays.

- After the build PR merges, open Insights, Dependency graph, Dependabot. The config must parse with no errors.
- On the first Monday after that, check the PRs. Packages that match a group arrive in one PR per group: `aspnetcore-efcore`, `fluentvalidation`, `test` and `actions`. Other NuGet packages and SDK updates arrive one per PR. Each title starts with `chore(deps):` or `ci(deps):`, and each PR passes the required checks.
- No Dependabot PR may offer a major version of ASP.NET Core, EF Core, Npgsql or the SDK. Those ignores come out when Laneway moves to .NET 10.
