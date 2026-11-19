# Development and stable releases

`main` is the source of stable releases. `development` is the integration branch for changes that are ready to test together. The initial baseline for the desktop redesign is the v0.1.4 commit `e492782`, which includes PR #14.

## Incremental changes

1. Update your view of the remote branches. Create a `feature/*`, `fix/*`, or `docs/*` branch from the current `development`.
2. Open a PR with `development` as the base. Include the tested scenarios and any follow-up work. For UI changes, follow the [desktop redesign plan](../design/DESKTOP-REDESIGN.md).
3. Review, build, and test each increment before merging to `development`. Resolve interactions between merged changes on new branches from `development`.

The current GitHub workflow builds and publishes Windows packages on manual dispatch. A separate build/test check for PRs targeting `development` or `main` is planned. Until that check is available, run and report the following checks on the proposed changes.

```powershell
dotnet restore FirebirdStudio.sln
dotnet build FirebirdStudio.sln -c Release --no-restore
dotnet test FirebirdStudio.sln -c Release --no-build --no-restore
python scripts/verify-structure.py
```

Require reviews and build/test checks on the integration and release branches once the dedicated PR check is in place. Add a stable-publishing guard to enforce the `main` source branch in the workflow. Until then, select `main` explicitly in the manual publishing dialog.

## Promote a release

1. Smoke-test the integrated build from `development`. Prepare the next unused `VERSION` through a PR to `development`.
2. Open a release PR from `development` to `main`. Review the combined changes, version, package expectations, and release notes.
3. After the release PR passes review and testing, merge it. Run **Publish Windows release** on `main` and verify the tag, assets, and updater manifest. See the [Windows packaging guide](WINDOWS.md).
4. Merge any release-only changes from `main` back into `development` before starting the next cycle.
