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

## Checks

No Unity command is authorized here. Launch Unity, Play Mode, Unity-hosted tests, or a build only when the user explicitly asks. Diff reading and static inspection remain available.

## Version control

`vcs/git.md` applies, including `feat/<id>-<slug>` → `dev` → `staging` → `main`. Existing task branches may also use the `feature/` prefix. Do not open a pull request against the retired `agent/jules` branch. If `dev` does not exist, or it does not contain current `staging`, finish the safe local work and report the blocker. Do not fall back to `staging` or `main`.

This section differs from `vcs/git-release.md`. There is no `release/<version>` branch and no `chore/version-*` candidate cut from `dev`. Version and changelog changes belong to a release pull request from `staging` to `main`. That pull request updates only the affected package versions under the maintainer's selected policy and describes user-visible changes and migrations. A change to the repository-wide versioning policy needs explicit human approval. Create a release tag only after the human maintainer merges that pull request into `main`. Feature work does not bump package versions. Apply exactly one `release:major`, `release:minor`, `release:patch`, or `release:none` label when the bump is known.

Agents do not merge, approve, or mark a pull request ready. They do not push to `dev`, `staging`, or `main`.

## Review

Antigravity is the independent review of a same-repository task pull request into `dev`, for that head SHA. Codex reviews the promotion pull request from `dev` to `staging` and the release pull request from `staging` to `main`. Antigravity does not replace the Codex review and does not mark a pull request ready. A review of an older SHA does not cover a newer commit. A failed run, a missing result, or an exhausted quota is not a successful review.

The required check is the commit status context `antigravity-review` on the current head SHA. `.github/workflows/antigravity-review.yml` is read from `main`. It checks out the default branch, downloads the pull request diff through the GitHub API, and does not run pull request code. Repository diffs are sent only when `ANTIGRAVITY_ALLOW_PRIVATE_DIFF` is `true`. The model is `gemini-3.8-flash-medium` unless `ANTIGRAVITY_MODEL` sets another `agy` slug.

The review looks for public API and serialization compatibility, `.meta` files and GUIDs, package and assembly dependencies, optional integrations, and platform limits. It does not ask for tests whose only purpose is to raise coverage. Automatic edits are a separate stage.

`codex-cursor-handoff.yml` requests Codex only for a pull request into `staging`, after a recorded Cursor handoff. It uses `JULES_HANDOFF_TOKEN`. The loop stops after three iterations with `codex:needs-human`.
