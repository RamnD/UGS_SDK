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
- Feature work does not bump package versions and does not create tags. Apply exactly one `release:major`, `release:minor`, `release:patch`, or `release:none` label when known.
- Version and changelog changes belong to a release PR from `staging` to `main`.
- A release PR updates only the affected package versions under the maintainer's selected release policy and describes user-visible changes and migrations. Changes to the repository-wide versioning policy require explicit human approval.
- Create a release tag only after the human maintainer approves and merges the release PR into `main`.
- Agents must not push directly to `staging` or `main`, merge PRs, create release tags before merge, or bypass branch protection.

## Integration

Tasks follow `feat/<id>-<slug>` → `dev` → `staging` → `main`. The same shape applies to `fix/`, `docs/`, and `chore/` branches. There is no permanent branch named after an agent. Do not create one. Do not open pull requests against the retired `agent/jules` branch.

- `dev` is the protected integration branch. A task branch starts from current `dev` after confirming that `dev` contains current `staging`. Its draft pull request targets `dev`.
- A human merges a task pull request into `dev`. Agents do not merge it, approve it, or mark it ready.
- Promotion is one pull request, head `dev`, base `staging`. Codex reviews that promotion diff for the exact head SHA. Antigravity does not replace that review.
- Release is a pull request from `staging` to `main`. Only a human maintainer merges promotion and release. Create a release tag only after that merge into `main`.
- If `dev` does not exist, or it does not contain current `staging`, finish the safe local work and report the blocker. Do not fall back to `staging` or `main`.
- Prefer a merge commit for promotion, or sync `staging` back into `dev` through a reviewed pull request. Do not force-reset `dev`.

## Cursor branch workflow

Cursor must establish the correct branch before editing files:

1. Inspect the current branch, working-tree status, configured remotes, and whether the branch already has an open pull request.
2. Never implement a feature directly on `dev`, `main`, or `staging`. Never commit or push directly to those branches.
3. Reuse the current branch only when it is an unprotected task branch and its name and existing diff clearly match the user's current task.
4. Otherwise, before making changes, create a focused branch from the latest `dev` (after confirming it contains current `staging`) using a descriptive prefix such as `feature/`, `fix/`, `refactor/`, `docs/`, or `chore/`.
5. Do not silently move, overwrite, stash, reset, or mix pre-existing uncommitted work. If unrelated changes prevent a safe branch switch, stop and ask the user how to preserve or separate them.
6. Keep one logical task per branch. If the user's request materially changes scope, create a separate branch rather than accumulating unrelated changes.

When the user asks Cursor to push the completed task branch, that request also authorizes this hand-off:

1. Run the required repository checks and report any failures.
2. Commit the task changes on the unprotected task branch if they are not committed yet.
3. Push that task branch to `origin` with upstream tracking. Never force-push unless the user explicitly requests it and the target is an unprotected task branch.
4. Create a draft pull request with base `dev` and head set to the pushed task branch. If a pull request for that pair already exists, update it.
5. Give the pull request a clear title and a body containing a summary, validation performed, known limitations, and one release-label recommendation when labels are available.
6. Do not claim Antigravity or Codex has run until a check or review exists for the current head SHA.
7. Return the draft pull request URL. Do not mark it ready, approve it, merge it, push to `dev`, `staging`, or `main`, or continue into Codex or release.

## Pull request review

Antigravity performs the primary review of a same-repository task pull request into `dev`. Codex performs the final review of the promotion pull request into `staging`, and reviews the release pull request from `staging` to `main`. A review of an older SHA is not a review of a newer commit. A failed agent, a missing result, or an exhausted quota is not a successful review.

### Antigravity

`.github/workflows/antigravity-review.yml` is a trusted `pull_request_target` workflow. GitHub reads it from `main`. Merging it only into `dev` does not activate it. The job checks out the default branch, downloads the pull request diff through the GitHub API, and does not run code from the pull request. The model step receives `GEMINI_API_KEY` and an empty GitHub token. A later step publishes the comment and the `antigravity-review` commit status.

The workflow reviews the head SHA from the triggering event. When a model run starts, it posts `pending` on that SHA and removes `antigravity:clean`, `antigravity:remarks`, `antigravity:quota`, and `antigravity:failed`. Those labels are not the required check. The required check is the commit status context `antigravity-review` on the current head SHA. A status on an older SHA does not satisfy it.

`agy` runs in review mode: one `-p` prompt, `--sandbox`, `--mode default`, and deny rules for writes, commands, URLs, and MCP. It does not receive `--dangerously-skip-permissions`. One technical failure may be retried once. Quota exhaustion and timeouts are not retried. Automatic edits are a separate stage.

Outcomes for that SHA:

- `clean` — commit status success. The comment lists no findings.
- `remarks` — commit status failure. The comment lists findings. The review completed and is not an approval.
- `quota` — commit status error. The Gemini API quota was exhausted. This is not a code verdict.
- `failure` — commit status error. The agent did not finish, returned no usable result, or the diff was not sent. This is not a successful review.

Repository diffs are sent only when the repository variable `ANTIGRAVITY_ALLOW_PRIVATE_DIFF` is `true`. Until then a pull request run records a failure and does not call the model. `workflow_dispatch` reviews a built-in public diff and does not read the repository.

Unity and UPM review points: public API and serialization compatibility, `.meta` files and GUIDs, package and assembly dependencies, optional integrations, and platform limits. Do not ask for tests whose only purpose is to raise coverage.

The model is `gemini-3.8-flash-medium` unless the repository variable `ANTIGRAVITY_MODEL` sets another `agy` model slug. With `GEMINI_API_KEY`, requests go to the Gemini API. Quota for that key is shown in [Google AI Studio](https://aistudio.google.com/rate-limit) and documented at <https://ai.google.dev/gemini-api/docs/rate-limits>. The Antigravity `/usage` panel applies to a signed-in Antigravity account, which this workflow does not use. Account model availability is documented at <https://antigravity.google/docs/models/>.

### Codex

Codex reviews the promotion pull request whose head is `dev` and whose base is `staging`, for the exact head SHA. Antigravity does not request Codex and does not mark a pull request ready. Whether opening or marking ready starts Codex depends on the Codex GitHub app settings. Configure that app for pull requests into `staging`, not for task pull requests into `dev`.

`codex-cursor-handoff.yml` requests `@codex review` only after a recorded Cursor handoff on a pull request into `staging`. It does not run for a task pull request into `dev`. It refuses protected heads `dev`, `staging`, and `main`. The loop still stops after three Cursor iterations with `codex:needs-human`. The handoff still uses the maintainer-bound `JULES_HANDOFF_TOKEN`; the secret name is unchanged.

Automated approvals do not replace human review. Automation does not merge `dev`, `staging`, or `main`.
