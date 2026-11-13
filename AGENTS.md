# Code instructions

Reply in the user's language. Instruction files stay English; repository text languages follow the last shared rule below.

Standard: v1. The changelog is `CHANGELOG.md` in the standards repository and is not copied into a project. Sync overwrites this file and `agents/instructions`, except `agents/instructions/PROJECT.md`. Sync does not touch `.cursorignore` or `.cursorindexingignore`: copy them once from `agents/instructions/cursorignore/` for the selected stack. Project-specific lines stay in those files.

Read `agents/instructions/PROJECT.md` first when it exists. Where it explicitly differs, it wins over the shared documents.

Read only the documents selected below and their explicitly required sections. A reference link alone does not require reading another document.

Version control:

- both `.git` and `.plastic` are present → `PROJECT.md` chooses which document to read
- `.git` → `agents/instructions/vcs/common.md`, then `agents/instructions/vcs/git.md`
- `.plastic` → `agents/instructions/vcs/common.md`, then `agents/instructions/vcs/plastic.md`

Choose the stack from the directory the task changes. When a repository contains several stacks, `PROJECT.md` holds a path → document map. Without that map, a marker from another directory does not apply to this task. A path the map lists as a UPM package uses `agents/instructions/stack/unity-package.md` even when its `package.json` has no `"unity"` field. A UPM package inside a TypeScript workspace does not switch the other directories to Unity rules.

- a directory with `ProjectSettings/ProjectVersion.txt` → `agents/instructions/stack/unity.md`
- a directory whose `package.json` has a `"unity"` field, or a path `PROJECT.md` lists as a UPM package → `agents/instructions/stack/unity-package.md`
- a TypeScript directory: root `package.json` plus root `tsconfig.json` or `tsconfig.base.json`, or a `tsconfig.json` in a package listed in `pnpm-workspace.yaml` → `agents/instructions/stack/typescript.md`, plus the line that applies, if any:
  - the directory depends on `vue` → `agents/instructions/stack/vue.md`
  - a browser client without `vue` → `agents/instructions/stack/web-client.md`
  - a server that handles HTTP requests → `agents/instructions/stack/node-api.md`
- Vue without `tsconfig.json` → `PROJECT.md` sets the rules. `vue.md` assumes `lang="ts"` and does not apply

For a TypeScript task only:

- dependencies, versions, CI, linter, formatting, hooks → `agents/instructions/stack/js-tooling.md`
- new project setup, `tsconfig`, Vite config, application scripts, environment variables, workspace layout → `agents/instructions/stack/new-project.md`; for a new project, also `js-tooling.md`
- a function needed by a second application → `agents/instructions/stack/utilities.md`

Nothing matched → ask once and propose text for `PROJECT.md`. The write is its own commit or changeset, not part of the current task. The next task reads the file and does not ask again. A JavaScript project without `tsconfig.json` is the same case: TypeScript rules do not apply.

Shared rules:

- An existing project keeps its contracts, layout, and commands, including API consumers, saved data, and exchange formats. A migration is a separate task.
- A module's role comes from its calls, dependencies, and a few neighboring implementations. A file name or the first example does not set the pattern. Do not scan the whole repository for this.
- A change fixes the cause in the module, function, or type that owns it. An open file does not receive an unrelated responsibility. A new type appears for its own responsibility.
- Read the code and the documents this router names before asking. When substantially different product options remain, ask. For a reversible technical choice with small risk, choose one and name the assumption.
- Verify the changed behavior with the check from the stack document or `PROJECT.md`, within that document's execution limits. The same limits apply throughout the task. Diff reading and static inspection remain available. Reuse relevant results while they still cover the final revision. Check calculations by their results, UI by its state, and saved data by a read/write round trip in test data. Report checks that failed, could not run, or were not authorized.
- Generated output is changed through its generator or the generator's settings. User-facing text goes through the content mechanism the project already uses. Names of generators and content tables stay in `PROJECT.md`. When the project has no such mechanism, this task does not add one.
- Fix a failure this task caused, even when the failing file is outside the diff. A failure that already existed stays out of this change and is reported separately.
- Do not change dependencies, their versions, or the lockfile unless the task asks. Only a version task changes the release number.
- Do not format files this change does not touch.
- Do not read or print `.env*`, except `.env.example`.
- Commit and changeset messages, PR bodies, TODO notes, and ordinary comments use the languages from the `Languages` section of `PROJECT.md`. Without that section, or for a line still set to `<language>`, they are English. Identifiers, branch names, and JSDoc and XML method comments are English.
