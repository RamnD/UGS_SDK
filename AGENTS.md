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

## Automation branches

- Protected integration target for Cursor feature work and the scheduled Jules batch: `agent/jules`.
- Protected human promotion target: `staging`.
- Protected release target: `main`.
- `agent/jules` is a cumulative integration branch, not a disposable Jules session branch. Individual Jules sessions may create their own temporary branches and PRs back into `agent/jules`.
- Do not send normal feature PRs directly to `staging` while this pipeline is in use. The cumulative promotion review is the `agent/jules` → `staging` pull request. A task pull request can become eligible for Codex after a clean Jules verdict; that does not replace the promotion review.

## Cursor branch workflow

Cursor must establish the correct branch before editing files:

1. Inspect the current branch, working-tree status, configured remotes, and whether the branch already has an open PR.
2. Never implement a feature directly on `main` or `staging`. Never commit or push directly to either protected branch.
3. Reuse the current branch only when it is an unprotected task branch and its name and existing diff clearly match the user's current task.
4. Otherwise, before making changes, create a focused branch from the latest available `agent/jules` (after confirming it contains the current `staging`) using a descriptive prefix such as `feature/`, `fix/`, `refactor/`, `docs/`, or `chore/`.
5. Do not silently move, overwrite, stash, reset, or mix pre-existing uncommitted work. If unrelated changes prevent a safe branch switch, stop and ask the user how to preserve or separate them.
6. Keep one logical task per branch. If the user's request materially changes scope, create a separate branch rather than accumulating unrelated changes.

When the user asks Cursor to push the completed task branch, that request also authorizes the normal hand-off for this workflow:

1. Run the required repository checks and report any failures; do not conceal failures merely to open the PR.
2. Commit the task changes on the unprotected task branch if they are not committed yet, using a concise descriptive commit message.
3. Push that task branch to `origin` with upstream tracking. Never force-push unless the user explicitly requests it and the target is confirmed to be an unprotected task branch.
4. Create a **draft** pull request with base `agent/jules` and head set to the pushed task branch. If a PR for that head/base pair already exists, update or report that PR instead of creating a duplicate.
5. Give the PR a clear title and a body containing a summary, validation performed, known limitations, and the appropriate `release:major`, `release:minor`, `release:patch`, or `release:none` recommendation when labels are available.
6. Mark/request the Jules stage using the repository's supported label or integration trigger. Do not claim Jules or Codex has run until its actual check/review exists for the current head SHA.
7. Return the draft PR URL to the user. Do not mark it ready, approve it, merge it, push to `agent/jules`/`staging`, or continue into the Jules, Codex, or release stages on the user's behalf.

If `agent/jules` does not exist remotely, it does not contain the required `staging` baseline, authentication cannot push/create a PR, or the GitHub integration is unavailable, complete all safe local work and report the exact blocker instead of falling back to `staging`/`main` or pretending the hand-off succeeded.

## Pull request automation

The flow has two review layers. A task pull request is reviewed before a human merges it into `agent/jules`. The promotion pull request is reviewed again on the cumulative `agent/jules` → `staging` diff. Marking a pull request ready only makes it eligible for the repository's configured Codex GitHub integration. Readiness is not evidence that Codex reviewed the current head SHA, and a later commit makes that hand-off stale.

1. A developer opens a draft PR from a focused task branch to protected `agent/jules`; only a human merges task PRs into that integration branch.
2. Google Jules audits task branches and the cumulative integration branch separately. The trusted workflow starts a task audit when a same-repository pull request targets `agent/jules`. A manual run can audit `agent/jules` against `staging`. Jules is an additional reviewer rather than a test author. It may make small, directly related corrections on its own temporary branch and open a pull request into the branch it audited. It never merges that pull request, and it never pushes to `main`, `staging`, or `agent/jules`.
3. Session completion, the number of changed files, and the verdict are separate facts.
   - `jules:complete` means the Jules session finished for that head. It is not a clean verdict.
   - Automation may close a Jules pull request with zero changed files, and may mark the audited draft ready, only when the verdict is explicitly clean, the head SHA is still the audited SHA, and exactly one target pull request matches.
   - Zero changed files with a blocking finding stays open. The finding is kept, and the audited pull request stays draft with `jules:blocking`.
   - If the verdict cannot be determined, the pull request stays pending with `jules:verdict-unknown` and is not marked ready.
   - A real code correction leaves the task pull request in draft with `jules:awaiting-fix`. After a human merges that correction, automation marks the task pull request ready only when its head SHA is exactly the merge commit and the pull request is not labeled blocking or verdict-unknown. The correction must carry a consistent, explicitly clean audit marker. Missing markers, blocking or unknown marker verdicts, and markers for different audited SHAs or sessions withhold the hand-off.
4. A cumulative correction targets `agent/jules`. The hand-off does not search for a pull request from `agent/jules` to `agent/jules`. The Codex target is the single open promotion pull request whose head is `agent/jules`, whose base is `staging`, and whose head SHA is the merge commit. If that target is missing, stale, or ambiguous, automation fails visibly and does not create another promotion pull request.
5. After a human accepts the audited task pull request into `agent/jules`, create or update one draft promotion PR with head `agent/jules` and base `staging`. Do not open duplicate promotion PRs.
6. Repository CI and any available Unity/package validation run on the promotion PR head.
7. Codex reviews the complete cumulative promotion diff after Jules, including package architecture, public compatibility, Unity lifecycle/platform behavior, documentation, and Jules' edits. It approves or requests changes for that exact head SHA. Codex eligibility on a task pull request does not replace this promotion review.
8. Every new commit makes earlier automated review state stale. An audit of an older SHA is not approval of the new head. Re-run required checks and final Codex review; return to Jules only when the change affects behavior that requires another pass, and avoid automatic bot-to-bot loops.
9. Only the human maintainer may approve and merge the promotion PR into `staging`. Automation does not merge protected branches.
10. After promotion, keep `agent/jules` based on the resulting `staging` history before accepting the next batch. Prefer a merge commit for the promotion or sync `staging` back through a reviewed PR; agents must not force-reset the integration branch.
11. A release PR from protected `staging` to protected `main` carries version and changelog changes. Codex reviews it, and only the human maintainer may approve and merge it.
12. Trusted release automation may create the tag only after merge into `main`.

### Workflow rollout

`jules-audit.yml` and `jules-fix-ready.yml` are trusted `pull_request_target` workflows. GitHub reads that workflow definition from the repository default branch, `main`. It does not read it from the pull request head. Merging the workflow into `agent/jules`, or updating it only on a task branch, does not activate it. The jobs do not checkout the pull request, so they do not execute untrusted pull request code. They set `GH_REPO` to the base repository and pass `--repo` on `gh` commands that need a repository. These changes take effect only after they are merged into `main`. Until then, GitHub Actions runs the workflow currently on `main`.

### Jules scope

- Review the full PR diff against `staging`, not only the latest commit.
- Look for correctness problems, accidental API breaks, missing `.meta` files, invalid assembly/package dependencies, unsafe optional-integration assumptions, Unity lifecycle issues, and platform-specific regressions.
- Keep any edits on the feature branch and in separate, identifiable commits.
- Make changes only when the correction is small, local, and unambiguous.
- Do not perform broad refactors, add unrelated dependencies, change package versions, create tags, push to protected branches, or merge.
- For larger or ambiguous problems, leave a blocking review comment with the affected path and expected behavior.

### Codex scope

- Review the final cumulative `agent/jules` → `staging` promotion diff only after Jules completes its pass. A task-branch Codex review, when the repository's GitHub integration runs one, does not replace this promotion review.
- Check the repository-wide design, package boundaries, public API and serialization compatibility, optional integrations, async failure behavior, consumer migration impact, documentation, and release implications.
- Review Jules' commits with the same scrutiny as developer commits.
- Small fixes may be committed only to the feature branch when local and low risk. Public API changes, package-layout changes, new dependencies, platform policy changes, and broad refactors require findings for the developer rather than autonomous rewrites.
- After a Codex fix, require validation and a new review of the resulting head SHA.
- Do not approve an outdated commit, push to protected branches, merge, bump versions in feature PRs, or create release tags.

### Review hand-off

Use explicit GitHub checks or labels such as `jules:requested`, `jules:complete`, `jules:blocking`, `jules:verdict-unknown`, `jules:awaiting-fix`, `codex:requested`, `codex:approved`, and `codex:changes-requested`.

- On a task pull request, a clean Jules verdict for the current head SHA can mark the draft ready. That makes the pull request eligible for the repository's Codex GitHub integration. It does not mean Codex has reviewed that SHA. `jules:complete` alone is not that hand-off.
- On the promotion pull request (`agent/jules` → `staging`), request Codex for the exact head SHA that completed Jules and CI. A task-branch hand-off does not request or complete that promotion review. A new commit clears completion and approval state.
- These workflows do not call a Codex API. Whether marking a pull request ready starts Codex depends on the Codex GitHub app or rules configured for the repository. Those settings are outside the workflow files.
- Automated approvals never replace the required human review. Automation must not merge `agent/jules`, `staging`, or `main`.
