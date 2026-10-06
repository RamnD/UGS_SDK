# Agent guide (UGS_SDK)

Entrypoint for AI coding agents working on this Unity UPM monorepo. Read this file and the affected package documentation before editing.

## Repository purpose

This repository contains reusable RamnD Unity packages:

| Path | Package |
| --- | --- |
| `packages/gameservices-sdk` | `com.ramnd.gameservices-sdk` |
| `packages/core-logging` | `com.ramnd.core-logging` |
| `packages/service-fault` | `com.ramnd.service-fault` |
| `packages/gameservices-servicefault` | `com.ramnd.gameservices-servicefault` |

The packages are consumed through Unity Package Manager. Preserve package boundaries, public API compatibility, Unity serialization behavior, and stable asset GUIDs.

## Source and package rules

1. Keep runtime code independent from `UnityEditor` APIs. Editor-only code belongs under an Editor folder and an Editor-only assembly definition.
2. Preserve optional integrations through their existing assembly definitions, `versionDefines`, `defineConstraints`, and platform guards. A consumer without an optional third-party package must still compile.
3. Do not introduce a hard dependency on a game project, scene, Resources asset, or project-specific singleton.
4. Treat public interfaces, serialized fields, package IDs, assembly names, UPM paths, and persistence keys as compatibility contracts.
5. Avoid Unity-unsupported language/runtime APIs unless the repository's Unity version and assembly settings demonstrably support them.
6. Keep asynchronous flows explicit about cancellation, timeout, duplicate callbacks, retries, and late completion. Do not turn an indeterminate purchase/auth result into a false success or a destructive failure.
7. Preserve `.meta` files and GUIDs. Add the matching `.meta` file for committed Unity assets when the workflow requires it; never regenerate unrelated GUIDs.
8. Do not edit generated, vendored, or third-party content unless the task explicitly targets it.
9. Never commit credentials, service-account files, signing material, store secrets, or project-specific production identifiers.

## Documentation and compatibility

- Read the affected package README and changelog before changing a public contract.
- Update documentation when installation, configuration, public behavior, or migration requirements change.
- Document breaking changes and provide a migration path; do not silently remove or rename public APIs.
- Keep package dependency declarations consistent with actual assembly references.
- Repository version information must come from the current package manifests, tags, and `CHANGELOG.md`; do not trust a version copied into an agent prompt or stale prose.

## Versioning and releases

- Treat each current package manifest and the changelog as authoritative. Package versions already differ; do not normalize unrelated package versions merely because older README prose describes a shared version.
- Feature PRs into `staging` do not bump package versions and do not create tags. Apply exactly one `release:major`, `release:minor`, `release:patch`, or `release:none` label when known.
- Version and changelog changes belong to a release PR from `staging` to `main`.
- A release PR updates only the affected package versions under the maintainer's selected release policy and describes user-visible changes and migrations. Changes to the repository-wide versioning policy require explicit human approval.
- Create a release tag only after the human maintainer approves and merges the release PR into `main`.
- Agents must not push directly to `staging` or `main`, merge PRs, create release tags before merge, or bypass branch protection.

## Cursor branch workflow

Cursor must establish the correct branch before editing files:

1. Inspect the current branch, working-tree status, configured remotes, and whether the branch already has an open PR.
2. Never implement a feature directly on `main` or `staging`. Never commit or push directly to either protected branch.
3. Reuse the current branch only when it is an unprotected task branch and its name and existing diff clearly match the user's current task.
4. Otherwise, before making changes, create a focused branch from the latest available `staging` using a descriptive prefix such as `feature/`, `fix/`, `refactor/`, `docs/`, or `chore/`.
5. Do not silently move, overwrite, stash, reset, or mix pre-existing uncommitted work. If unrelated changes prevent a safe branch switch, stop and ask the user how to preserve or separate them.
6. Keep one logical task per branch. If the user's request materially changes scope, create a separate branch rather than accumulating unrelated changes.

When the user asks Cursor to push the completed task branch, that request also authorizes the normal hand-off for this workflow:

1. Run the required repository checks and report any failures; do not conceal failures merely to open the PR.
2. Commit the task changes on the unprotected task branch if they are not committed yet, using a concise descriptive commit message.
3. Push that task branch to `origin` with upstream tracking. Never force-push unless the user explicitly requests it and the target is confirmed to be an unprotected task branch.
4. Create a **draft** pull request with base `agent/jules` and head set to the pushed task branch. If a PR for that head/base pair already exists, update or report that PR instead of creating a duplicate.
5. Give the PR a clear title and a body containing a summary, validation performed, known limitations, and the appropriate `release:major`, `release:minor`, `release:patch`, or `release:none` recommendation when labels are available.
6. Opening that draft pull request is the Jules trigger. Do not claim Jules or Codex has run until its actual check/review exists for the current head SHA.
7. Return the draft PR URL to the user. Do not mark it ready, approve it, merge it, push to `agent/jules` or `staging`, or continue into the Codex/release stages on the user's behalf.

If `agent/jules` does not exist remotely, authentication cannot push/create a PR, the base branch is ambiguous, or the GitHub integration is unavailable, complete all safe local work and report the exact blocker instead of falling back to `staging` or `main` or pretending the hand-off succeeded.

## Pull request automation

The intended flow is:

1. A developer opens a draft PR from a task branch to `agent/jules`. `.github/workflows/jules-audit.yml` must be on the default branch `main` before GitHub will run it. The workflow then starts a Jules session for that pull request, including drafts. A ready pull request into `agent/jules` starts the same audit.
2. Jules reviews the full diff of the task branch against `agent/jules`. It is an additional reviewer rather than a test author. Small, unambiguous corrections come back as a separate pull request into the task branch. Jules does not merge it. Pull requests authored by Jules, or whose head branch starts with `jules-`, do not start another audit.
3. A new commit on the same pull request starts a new audit and cancels the previous Jules session for that pull request.
4. Repository CI and any available Unity/package validation run on the resulting head commit.
5. Codex reviews the complete result after Jules, including package architecture, public compatibility, Unity lifecycle/platform behavior, documentation, and Jules' edits. It approves or requests changes.
6. Every new commit makes earlier automated review state stale. Re-run the required checks and final Codex review for the new head SHA; avoid automatic bot-to-bot loops.
7. Only the human maintainer may approve and merge the task PR into `agent/jules`, and only the human maintainer may merge `agent/jules` into `staging`.
8. A release PR from protected `staging` to protected `main` carries version and changelog changes. Codex reviews it, and only the human maintainer may approve and merge it.
9. Trusted release automation may create the tag only after merge into `main`.

### Jules scope

- Review the full pull request diff against its base, not only the latest commit. For a task PR that base is `agent/jules`.
- Look for correctness problems, accidental API breaks, missing `.meta` files, invalid assembly/package dependencies, unsafe optional-integration assumptions, Unity lifecycle issues, and platform-specific regressions.
- Leave small corrections for the automatic pull request into the task branch. Do not push directly to the task branch, `agent/jules`, `staging`, or `main`, and do not merge.
- Make changes only when the correction is small, local, and unambiguous.
- Do not perform broad refactors, add unrelated dependencies, change package versions, create tags, or push to protected branches.
- For larger or ambiguous problems, leave a blocking review comment with the affected path and expected behavior.

### Codex scope

- Review the final cumulative diff only after Jules completes its pass.
- Check the repository-wide design, package boundaries, public API and serialization compatibility, optional integrations, async failure behavior, consumer migration impact, documentation, and release implications.
- Review Jules' commits with the same scrutiny as developer commits.
- Small fixes may be proposed only as a pull request into the task branch when local and low risk. Public API changes, package-layout changes, new dependencies, platform policy changes, and broad refactors require findings for the developer rather than autonomous rewrites.
- After a Codex fix, require validation and a new review of the resulting head SHA.
- Do not approve an outdated commit, push to protected branches, merge, bump versions in feature PRs, or create release tags.

### Review hand-off

Use explicit GitHub checks or labels such as `jules:requested`, `jules:complete`, `codex:requested`, `codex:approved`, and `codex:changes-requested`. Request Codex only for the exact head SHA that completed Jules and CI. A new commit clears completion/approval state. Automated approvals never replace the required human review.
