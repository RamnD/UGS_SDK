# Shared utilities

A repeated function with no domain types lives in a package. A second application does not get its own copy.

A function belongs in the package when all of these are true:

- it does not know domain types
- a second application needs it in the same form
- it has a test for its behavior

A function of one screen, engine, or protocol stays in the application. The shared package is not assembled from the application's whole `utils` directory.

## Layout

Two packages, both without Vue:

```text
packages/utils/        pure functions, importable from Node too
  result.ts            Result<T, E>
  guard.ts             narrowing such as isKeyOf
  date.ts              formatting, when the server needs it too

packages/dom/          browser only
  cookie.ts
  query.ts             parse a parameter, the key is escaped
  log.ts               createLogger({ channels, enabled })
```

`log.ts` returns a typed object. A channel that is not in the list does not compile. `window.log` stays a thin binding in the application when a global call is needed.

These packages contain no Vue components or composables. A composable is looked up in VueUse first. A local `use*` appears in the application and moves to a separate Vue package only after a second copy in another application.

`packages/utils` holds the shared `Result<T, E>`. The network client's error type, including HTTP status, does not belong there: a config check and a calculation have no response status. `fetch`, authorization headers, and parsing of a specific error stay in the application until a second product uses the same client.

## Build

The package compiles to `dist`. `exports` points at the built files, not the source `.ts`:

```json
{
  "files": ["dist"],
  "exports": {
    ".": {
      "types": "./dist/index.d.ts",
      "import": "./dist/index.js"
    }
  }
}
```

Vite, the server's `node dist`, and an internal registry publish all use that entry. Source `.ts` is not imported at runtime. Even when the package exports only a type so far, the build and `exports` already point at `dist`: the first runtime function does not change the entry.

An application builds the package before its own `typecheck` and `dev`, through `pretypecheck` and `predev`. The server has the same step in `prebuild`, because `node dist` uses the built package. The root `prepare` builds the package right after install, so the editor and lint-staged do not wait for the first `dev`.

## Consumption

The package lives in the product's pnpm workspace and has Vitest tests. When another repository needs the same code, the package is published to an internal registry or linked as a git dependency. Public npm is not used. The publication contains `dist`, not `src`.

The scope name is chosen when the package repository is created. Until then, `packages/utils` and `packages/dom` are enough.
