# Git

For a repository with a `.git` directory or worktree file. Shared limits are in [common.md](common.md). Existing branches and protections change only in a separate migration task. Checks come from the stack document or `PROJECT.md`.

## Task branches

```text
feat/<id>-<slug> → dev → staging → main
fix/…  docs/…  chore/…
```

A short task branch starts from current `dev`; its main PR targets `dev`. An extra branch of the task returns by PR into that task branch, not directly into a permanent branch.

Merge current `dev` into the task branch before integration; do not rewrite its history. Squash the finished task into `dev`, keeping one commit per task. A feature task does not change the release version.

`dev` collects checked tasks and version candidates. A human controls `staging` for acceptance, `main` for release, and release tags. Agents do not promote to those branches.

For a version task, candidate preparation, candidate fix, promotion, tag, or rollback, read [git-release.md](git-release.md) before acting. Candidate fixes use a frozen base and a merge commit into `dev`: do not apply the ordinary branch-update or squash rules to them. Ordinary feature and fix tasks do not need the release procedure.

## Pull requests and protections

The PR describes the goal, observable acceptance check, scope, checks run, and remaining limits. A small change needs only a short text.

An explicit request to "push" or "open a PR" publishes the task branch and its draft PR into `dev`. It does not authorize merging or release.

On `dev`, `staging`, and `main`:

- changes arrive through PRs; no direct push, force-push, or rewrite of published history
- the required status check comes from the stack document or `PROJECT.md`
- blocking conversations are resolved before merge
- ordinary task branches contain current `dev` before integration; candidate fixes and promotions follow `git-release.md`

Repository settings: squash merges and merge commits are both allowed. Tasks use squash; candidate fixes and promotions use merge commits. "Require branches to be up to date before merging" stays off on `dev`, `staging`, and `main`: a candidate fix and a promotion do not contain the current base by design. The author and the reviewer check that an ordinary task branch contains current `dev`; that setting does not.

Before any merge into `dev`, including a candidate fix, record the current base SHA, the task head SHA, and the prospective merged tree. Run the required checks on that combined content and have the independent pass review the integration against that base. A green check on the task head alone is insufficient. Serialize integrations from base capture through merge; immediately before merging, confirm both SHAs still match. If either changed, recompute the merge and repeat the integration checks and review. A candidate fix is checked in a separate working tree or CI merge revision; its frozen branch does not take current `dev`. This gate is required even with the up-to-date setting off.

Do not delete someone else's branch or change protections. A rollback is a separate revert with checks. A small stream does not use a merge queue. Permanent branches allow merge commits; do not require linear history.

A review label or comment does not replace the required check. The independent review pass from `common.md`, a GitHub approval, and a human's merge decision are separate events. Approval counts only from an account other than the branch publisher; one shared agent account does not supply it.

An agent token, if allowed to merge at all, is limited to `dev`.

After a head or base change, integration checks and review do not carry over to the new combination. Record the head, base, and checked tree SHAs, plus GitHub's merge SHA when CI checked that commit.
