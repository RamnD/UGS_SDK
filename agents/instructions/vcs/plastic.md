# Plastic SCM

The repository marker is a `.plastic` directory. Shared agent limits are in [common.md](common.md).

The current branch is the `smartbranch` field in `.plastic/plastic.selector`. Plastic projects use different paths, so `feat → dev → staging` is not copied here. Do not create a task branch until `PROJECT.md` names one.

History is a changeset. Inspect state with `cm status` and `cm diff`. Checkin and switching branches happen only when explicitly asked. `cm undo` of someone else's change is the same forbidden reset as in [common.md](common.md).

## Foreign changeset into the workspace

Bringing someone else's changeset or branch into the current working copy stays local. The user checkins after their own review.

A single changeset and a branch merge are different operations. Neither uses `--to` or `--destination`: those flags run a server-side merge and can create a changeset without a separate `cm checkin`.

One changeset:

- Preview with `cm merge cs:<id> --cherrypicking` and without `--merge`.
- Apply only that changeset with `cm merge cs:<id> --cherrypicking --merge`.
- When a file already diverged on the current branch, merge the needed pieces into the local version instead of replacing the whole file.

A branch:

- Preview with `cm merge <branch-spec>` and without `--merge`.
- Apply that branch with `cm merge <branch-spec> --merge`.

At the end, list the touched files and anything that needed a manual merge.

Do not:

- Run `cm checkin`, create a changeset on the branch, or treat "into the branch" / "cherry-pick" as permission to publish.
- Checkin unless the user explicitly asks to checkin.

## Locks and review

A binary asset can have a checkout lock. Do not overwrite a file locked by someone else's checkout. A `.meta` file moves with its asset. Do not recreate someone else's GUID.

Review happens in Plastic. Squash, GitHub protections, and GitHub workflows do not apply to this repository.
