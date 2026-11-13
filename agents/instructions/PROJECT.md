# Project

Where this file explicitly differs, it wins over the shared documents. Sync does not overwrite it.

It holds only this repository's differences. Do not copy the shared instructions into it.

## Languages

- Commit and changeset messages: English
- PR bodies: English
- TODO notes and ordinary comments: English

## Repository

This directory is a Unity Package Manager monorepo. Games consume the packages through Package Manager.

| Path | Package |
| --- | --- |
| `packages/gameservices-sdk` | `com.ramnd.gameservices-sdk` |
| `packages/core-logging` | `com.ramnd.core-logging` |
| `packages/service-fault` | `com.ramnd.service-fault` |
| `packages/gameservices-servicefault` | `com.ramnd.gameservices-servicefault` |

`packages/*` uses `stack/unity-package.md`. Package versions already differ. The root `LICENSE` is MIT and covers these packages. Keep that file and its copyright notice.

An indeterminate purchase or auth result stays indeterminate. It is not reported as success and is not turned into a destructive failure.

## Checks

No Unity command is authorized here. Launch Unity, Play Mode, Unity-hosted tests, or a build only when the user explicitly asks. Diff reading and static inspection remain available.

## Version control

This section differs from `vcs/git.md` and `vcs/git-release.md`. There is no `dev` branch and no `release/<version>` branch cut from `dev`. The shared squash-into-`dev` step does not apply.

Protected integration branch: `agent/jules`. Protected promotion target: `staging`. Protected release target: `main`.

`agent/jules` is a cumulative integration branch, not a disposable Jules session branch. A Jules session may create its own temporary branch and a pull request back into `agent/jules`. A normal feature pull request targets `agent/jules`. It does not target `staging`. The promotion review is the pull request from `agent/jules` to `staging`. A task pull request can become eligible for Codex after a clean Jules verdict. That does not replace the promotion review.

Feature work does not bump package versions and does not create tags. Apply exactly one `release:major`, `release:minor`, `release:patch`, or `release:none` label when the bump is known. Version and changelog changes belong to a release pull request from `staging` to `main`. That pull request updates only the affected package versions under the maintainer's selected policy and describes user-visible changes and migrations. A change to the repository-wide versioning policy needs explicit human approval. Create a release tag only after the human maintainer merges that pull request into `main`.

Agents do not push directly to `staging` or `main`, merge pull requests, create a release tag before that merge, or bypass branch protection.

### Task branch

Before editing, inspect the current branch, working tree, configured remotes, and whether the branch already has an open pull request.

Do not implement a feature on `main` or `staging`. Do not commit or push directly to either branch. Reuse the current branch only when it is an unprotected task branch and its name and existing diff match the current task. Otherwise create a focused branch from the latest `agent/jules` after confirming that branch contains current `staging`. Use a prefix such as `feature/`, `fix/`, `refactor/`, `docs/`, or `chore/`.

Do not move, overwrite, stash, reset, or mix pre-existing uncommitted work. If unrelated changes block a safe branch switch, stop and ask how to preserve them. Keep one task on a branch. A material change of scope is a separate branch.

An explicit request to push the completed task branch also authorizes this hand-off:

1. Run the required checks and report failures.
2. Commit the task changes on the unprotected task branch when they are not committed yet.
3. Push that branch to `origin` with upstream tracking. Force-push only when the user explicitly asks and the target is an unprotected task branch.
4. Open a draft pull request with base `agent/jules` and head set to the pushed branch. If that pair already has a pull request, update it.
5. The title and body include a summary, the validation performed, known limitations, and one release-label recommendation when labels are available.
6. Request the Jules stage with the repository's label or integration trigger. Do not claim Jules or Codex has run until a check or review exists for the current head SHA.
7. Return the draft pull request URL. Do not mark it ready, approve it, merge it, push to `agent/jules`, `staging`, or `main`, or continue into Jules, Codex, or release.

If `agent/jules` is missing, does not contain current `staging`, or the push or pull request cannot be created, finish the safe local work and report the blocker. Do not fall back to `staging` or `main`.

### Review layers

A task pull request is reviewed before a human merges it into `agent/jules`. The promotion pull request is reviewed again on the cumulative `agent/jules` → `staging` diff. Marking a pull request ready makes it eligible for the repository's Codex GitHub integration. Readiness is not evidence that Codex reviewed the current head SHA. A later commit makes that hand-off stale.

1. A developer opens a draft pull request from a task branch to `agent/jules`. Only a human merges it.
2. Jules audits task branches and the cumulative integration branch separately. A same-repository pull request that targets `agent/jules` starts a task audit. A manual run can audit `agent/jules` against `staging`. Jules is a reviewer. It may make a small, directly related correction on its own temporary branch and open a pull request into the branch it audited. It does not merge that pull request and does not push to `main`, `staging`, or `agent/jules`.
3. Session completion, the number of changed files, and the verdict are separate facts.
   - `jules:complete` means the session finished for that head. It is not a clean verdict.
   - Automation may close a Jules pull request with zero changed files and mark the audited draft ready only when the verdict is explicitly clean, the head SHA is still the audited SHA, and exactly one target pull request matches.
   - Zero changed files with a blocking finding stays open. The finding stays, and the audited pull request stays draft with `jules:blocking`.
   - An undetermined verdict stays pending with `jules:verdict-unknown` and is not marked ready.
   - A real code correction leaves the task pull request in draft with `jules:awaiting-fix`. After a human merges that correction, automation marks the task pull request ready only when its head SHA is exactly the merge commit and the pull request is not labeled blocking or verdict-unknown. The correction carries a consistent, explicitly clean audit marker for that SHA and session. A missing, blocking, or unknown marker, or a marker for another SHA or session, withholds the hand-off.
4. A cumulative correction targets `agent/jules`. The hand-off does not look for a pull request from `agent/jules` to `agent/jules`. The Codex target is the single open promotion pull request whose head is `agent/jules`, whose base is `staging`, and whose head SHA is the merge commit. If that target is missing, stale, or ambiguous, automation fails visibly and does not open another promotion pull request.
5. After a human accepts the audited task pull request into `agent/jules`, create or update one draft promotion pull request with head `agent/jules` and base `staging`.
6. Repository CI and any available Unity or package validation run on the promotion head.
7. Codex reviews the cumulative promotion diff after Jules, including package architecture, public compatibility, Unity lifecycle and platform behavior, documentation, and Jules' edits. It approves or requests changes for that exact head SHA. Codex eligibility on a task pull request does not replace this review.
8. A new commit makes earlier automated review stale. An audit of an older SHA is not approval of the new head. Re-run the required checks and the final Codex review. Return to Jules only when the change affects behavior that needs another pass.
9. Only the human maintainer may approve and merge the promotion pull request into `staging`. Automation does not merge protected branches.
10. After promotion, keep `agent/jules` based on the resulting `staging` history before the next batch. Prefer a merge commit for the promotion, or sync `staging` back through a reviewed pull request. Do not force-reset the integration branch.
11. A release pull request from `staging` to `main` carries version and changelog changes. Codex reviews it. Only the human maintainer may approve and merge it.
12. Trusted release automation may create the tag only after the merge into `main`.

`jules-audit.yml` and `jules-fix-ready.yml` are trusted `pull_request_target` workflows. GitHub reads that workflow from the default branch, `main`, not from the pull request head. Merging the workflow into `agent/jules`, or updating it only on a task branch, does not activate it. The jobs do not checkout the pull request, so they do not execute untrusted pull request code. They set `GH_REPO` to the base repository and pass `--repo` on `gh` commands that need a repository. These changes take effect only after they are merged into `main`.

Jules reviews the full pull request diff against `staging`, not only the latest commit. It looks for correctness problems, accidental API breaks, missing `.meta` files, invalid assembly or package dependencies, unsafe optional-integration assumptions, Unity lifecycle issues, and platform-specific regressions. Edits stay on the feature branch in separate commits. A correction is small, local, and unambiguous. A broad refactor, an unrelated dependency, a package version change, a tag, a push to a protected branch, or a merge is out of scope. A larger or ambiguous problem is a blocking review comment with the path and the expected behavior.

Codex reviews the final cumulative `agent/jules` → `staging` promotion diff only after Jules completes its pass. A task-branch Codex review does not replace that promotion review. The review covers repository design, package boundaries, public API and serialization compatibility, optional integrations, async failure behavior, consumer migration, documentation, and release implications. Jules' commits get the same scrutiny as developer commits. A small fix may land on the feature branch when it is local and low risk. A public API change, a package-layout change, a new dependency, a platform policy change, or a broad refactor is a finding, not an autonomous rewrite. After a Codex fix, validate again and review the resulting head SHA. Do not approve an outdated commit, push to a protected branch, merge, bump a version in a feature pull request, or create a release tag.

### Review labels

Use `jules:requested`, `jules:complete`, `jules:blocking`, `jules:verdict-unknown`, `jules:awaiting-fix`, `codex:requested`, `codex:approved`, and `codex:changes-requested`.

A clean Jules verdict for the current head SHA can mark the task draft ready. That makes the pull request eligible for Codex. It does not mean Codex has reviewed that SHA. `jules:complete` alone is not that hand-off.

On the promotion pull request, request Codex for the exact head SHA that completed Jules and CI. A task-branch hand-off does not request or complete that promotion review. A new commit clears completion and approval.

These workflows do not call a Codex API. Whether marking a pull request ready or pushing a commit starts Codex depends on the Codex GitHub app settings for the repository.

`codex-cursor-handoff.yml` watches the machine-readable Codex review summary. For a completed review with unresolved findings on the current head SHA, it posts one `@cursoragent` request through the maintainer-bound `JULES_HANDOFF_TOKEN`. It refuses stale, closed, forked, duplicate, malformed, and protected-head hand-offs. Created and edited events for the same Codex summary are serialized by comment ID. Cursor pushes and unrelated comments use separate concurrency groups. There is no manual reprocess, so a second handoff cannot race the automatic comment. The loop stops after three Cursor iterations with `codex:needs-human`.

After a push whose new head directly descends from a recorded Cursor handoff SHA, the workflow requests `@codex review` for that new head. An ordinary push without that marker does not trigger this extra review. Each Codex review names the current SHA. A previous review does not approve a later Cursor commit.

Automated approvals do not replace human review. Automation does not merge `agent/jules`, `staging`, or `main`.
