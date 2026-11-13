# Git releases and candidate fixes

Read with [git.md](git.md) only for version tasks, candidate preparation, candidate fixes, promotions, tags, or rollbacks. Publishing and integration still require the authorization described in [common.md](common.md). A human controls `staging`, `main`, and release tags.

## Branches and merge rules

There is no permanent release branch. A short `release/<version>` points at the handed-over candidate SHA. Urgent fixes follow `feat/fix → dev → staging → main` unless a person sets another order.

`release/<version>` → `staging` and `staging` → `main` use merge commits, never squash. Candidate fixes also use merge commits into `dev`; they do not merge current `dev` into their branch by default.

## Version candidate

The release number is the `version` field of a package that is published. A Unity package follows its stack document. A `chore/version-<version>` task for the next candidate branches from current `dev` and merges back into `dev`. The version task for a candidate fix branches from the frozen head of that fix, not from current `dev`. A feature branch does not change the field. A package that is not published stays out of the PR.

The step follows the published package. A breaking change raises major, a new capability raises minor, a fix raises patch.

This task does not change dependency versions or the lockfile. A separate task that explicitly asks for that does. When the project keeps a changelog, this number's entry is in the same PR.

After the merge, the candidate is checked again. The person receives the number, the exact SHA, the contents, the CI result, and the remaining limits. The PR into `staging` opens from `release/<version>`, and that branch's head is the handed-over SHA. The tip of `dev` is not the PR head. The branch is deleted when the person accepts the candidate or rejects it. Tasks merged into `dev` after this SHA stay out of the PR and keep merging into `dev` as usual.

A promotion into `staging` or `main` has three steps. The preflight and the merge use one recorded base SHA and one recorded candidate SHA. If either SHA changes, repeat the preflight before merging. A merge commit that only records an earlier promotion does not by itself make the next candidate stale.

1. Before the merge, `git merge-tree --write-tree <base> <candidate>` must print the candidate tree. A conflict or any other tree means the promotion does not start.
2. Merge those same two SHAs.
3. `git rev-parse <result>^{tree}` must equal `git rev-parse <candidate>^{tree}`. A mismatch is a revert of that merge commit, with checks. `staging` and `main` are not reset.

A merge commit between permanent branches keeps the shared history. The candidate tree is the content that must arrive.

A remark on a candidate is a separate task from its SHA. The fix branch starts at that SHA. Its own commits advance the recorded head: the fix, the tests for that fix, and the version number. That head stays frozen until someone explicitly merges `dev` into the fix branch. That merge is a separate decision: it pulls features that landed after the freeze, and it is not the ordinary branch update. The fix branch is merged into `dev` with a merge commit, not a squash, so those commits exist in both `staging` and `dev` and the next merge-base moves forward. The fix does not remain only on the frozen SHA. A handed-over number is not reused.

`staging` and `main` receive the number that was already prepared. A person tags the accepted `main` commit. Deploy runs as a separate authorized release process.

## Promotion protections

A promotion PR's head does not have to contain later commits from the base. The three promotion steps above are its acceptance gate.

Rollback uses a separate revert with checks. Do not reset `staging` or `main`, rewrite published history, or change protections.
