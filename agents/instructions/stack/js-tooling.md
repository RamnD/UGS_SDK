# Versions, commands, CI, linter, and hooks

For a task about dependencies, CI, the linter, formatting, or hooks, and with [new-project.md](new-project.md) for a new project. An existing project keeps its package manager and scripts: this document does not convert an npm repository to pnpm.

## Prettier

One file formats the whole codebase:

```json
{
  "semi": false,
  "singleQuote": true,
  "printWidth": 100,
  "trailingComma": "all",
  "tabWidth": 2,
  "useTabs": false,
  "arrowParens": "always",
  "endOfLine": "lf",
  "bracketSpacing": true,
  "vueIndentScriptAndStyle": true
}
```

Indentation is spaces. A package or application in the same repository does not get a second config.

Snippets use `;` between type members, as Prettier leaves them: `{ ok: true; data: T }`. A statement has no trailing semicolon: `semi` is off.

## Versions

As of 9 November 2026. A new project pins versions in `package.json`. `packageManager` selects pnpm.

| Package | Version | Why |
| --- | --- | --- |
| pnpm | 10 | Package manager of a new repository. |
| TypeScript | 6.0 | Latest major that `typescript-eslint` accepts (`<6.1`). The linter does not accept TypeScript 7 yet. |
| Vite | 8 | Current major. `@vitejs/plugin-vue` accepts it. |
| Vue | 3.5 | Current stable release. |
| Pinia | 4 | Current major for Vue 3.5. The peer `@vue/devtools-api` is required next to it. |
| Vue Router | 5 | Current major. The route table stays explicit. |
| ESLint | 10, flat config | `typescript-eslint` and `eslint-plugin-vue` accept it. |
| Vitest | 5 | Current major next to Vite 8. |
| Zod | 4 | Schema at the data boundary. |
| VueUse | 15 | Added when a ready-made composable is needed. A new project without that scenario does not include it. |
| Prettier | 3 | One format for the repository. |
| eslint-plugin-vue | 10 | Recommended flat set for `.vue`. |
| eslint-config-prettier | 10 | Last block of the flat config. |
| tsx | 4 | Dev runner for a Node API. |
| jsdom | 31 | Vitest environment for Vue. |
| vite-plugin-checker | 1 | Type check in the Vite dev server. |
| Hono | 4 | Server HTTP framework. |
| Husky | 9 | Commit hook. |
| lint-staged | 17 | Prettier and ESLint on the touched files. |
| @types/node | 24 | Node types of the same major as `engines` and CI. |

A new project's root `package.json` sets `engines.node` to `24.x`. CI installs Node 24. That is the same major.

The table sets majors. Exact versions that pass `pnpm install --frozen-lockfile` and `pnpm verify` together: TypeScript 6.0.3, Vite 8.3.7, Vue 3.5.45, Pinia 4.1.0, `@vue/devtools-api` 8.2.1, Vue Router 5.4.0, ESLint 10.14.0, `typescript-eslint` 8.72.1, Vitest 5.1.0, Zod 4.6.7, `vue-tsc` 3.3.14, pnpm 10.27.0. Also Prettier 3.6.2, `eslint-plugin-vue` 10.12.0, `eslint-config-prettier` 10.1.8, `@vitejs/plugin-vue` 6.0.10, `vite-plugin-checker` 1.0.0, tsx 4.23.15, jsdom 31.0.0, Hono 4.13.17, `@hono/node-server` 2.1.6, `@types/node` 24.19.2, Husky 9.1.7, lint-staged 17.6.0. A new project copies this set instead of picking the latest patch of each package again.

## Commands

A new project has one check for the agent and for CI:

```json
{
  "lint": "eslint . --max-warnings=0",
  "format": "prettier --write .",
  "format:check": "prettier --check .",
  "test": "vitest run",
  "verify": "pnpm typecheck && pnpm lint && pnpm format:check && pnpm test"
}
```

A rule set to warn stays a warning in the editor. `pnpm verify` does not let it through: `eslint . --max-warnings=0` fails on any warning.

In a workspace, Prettier runs once per file. The root `format:check` is `prettier --check . '!apps/**' '!packages/**'`: a new root file is included on its own. A package's `verify` looks at that package's files and passes `--ignore-path` to the root `.prettierignore`, so `dist` is not treated as source. The root `verify` does not run Prettier over the whole tree a second time, and it runs packages one at a time so `packages/utils` finishes building before application typecheck.

The `dev` command starts processes in parallel. A second process is not placed behind `&&` after a long-running first one.

## CI

```yml
name: ci

on:
  pull_request:
  push:
    branches: [dev, staging, main]

permissions:
  contents: read

jobs:
  verify:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: pnpm/action-setup@v6
      - uses: actions/setup-node@v7
        with:
          node-version: 24
          cache: pnpm
      - run: pnpm install --frozen-lockfile
      - run: pnpm verify
      - run: pnpm -r --workspace-concurrency=1 --if-present build
```

`pnpm verify` does not run the production build. After it, CI builds applications and packages with `build`: `vite build` for the browser, `tsc` for the Node API and the shared package. Packages run one at a time so `packages/utils` finishes before the applications. The check-and-build job is required. The workflow permission is only `contents: read`. The actions are the current majors: `actions/checkout@v7`, `pnpm/action-setup@v6`, `actions/setup-node@v7`.

## ESLint

Flat config. `eslint-config-prettier` is last, so ESLint does not argue with Prettier.

- `@typescript-eslint/consistent-type-imports`: error, `prefer: type-imports`.
- `@typescript-eslint/no-unused-vars`: warn, names with `_` are allowed.
- `@typescript-eslint/no-explicit-any`: warn.
- `@typescript-eslint/no-empty-object-type`: error, `allowInterfaces: 'with-single-extends'`, so `ImportMetaEnv extends AppEnv` stays declaration merging.
- `eqeqeq`: error.
- `prefer-const` and `no-var`: error.
- `no-console`: warn, `warn` and `error` are allowed.
- For Vue: the recommended `eslint-plugin-vue` flat set. The multi-word component name rule is off.

The full `strictTypeChecked` set is off in the application. Two Promise rules are enabled on their own, and the linter gets types for them through `parserOptions.projectService: true`:

- `@typescript-eslint/no-floating-promises`: error
- `@typescript-eslint/no-misused-promises`: error

In an application without Vue these rules use `files: ['**/*.ts']`. In a Vue application they use `files: ['**/*.ts', '**/*.vue']`. For `.vue`, the parser has `projectService: true` and `extraFileExtensions: ['.vue']`, otherwise a floating promise in `onMounted` or a handler is invisible. The same block sets `@typescript-eslint/no-explicit-any` to `warn` on `.vue` too: the recommended set would otherwise leave it as `error`.

The global `log` is declared in `languageOptions.globals` when a browser project uses it. The server module `log.ts` is not declared as a global.

## Hooks

Husky and lint-staged run Prettier and ESLint on the touched files at commit. For the same file, Prettier finishes before ESLint: lint-staged receives them as one command list. The hook does not bump a package version, create a tag, or run `git add`. A new project's full check in CI is `pnpm verify`.

`prepare` runs `husky` and builds `packages/utils`. After install the hook is on, `core.hooksPath` points at `.husky`, and `dist` is already there for the editor and for type-aware ESLint.
