# TypeScript

Shared language for Vue, other clients, and a Node API.

A new project is checked with `pnpm verify` at the root. An existing project runs the script from its `package.json` or the command from `PROJECT.md`.

## Compiler

Enabled: `strict`, `noUncheckedIndexedAccess`, `noUnusedLocals`, `noUnusedParameters`, `noImplicitReturns`, `verbatimModuleSyntax`, `forceConsistentCasingInFileNames`, `erasableSyntaxOnly`.

`enum`, `namespace`, and parameter properties are not used: they have runtime behavior and do not erase to plain JavaScript. `erasableSyntaxOnly` checks that. A status or mode is a union of literals: `'idle' | 'loading' | 'ready' | 'error'`.

## Types and config

- Type names name the domain entity.
- A data shape or a union is a `type`. `interface` stays for declaration merging: `RouteMeta`, `ImportMetaEnv`.
- Exact literals are inferred from the value: `as const`, `satisfies`, `(typeof ids)[number]`.
- A complex static object lives in `configs`: one file per entity, a barrel `index.ts`. The map type requires an entry for every key.
- A role suffix is used when several files of one entity sit together and the bare name does not distinguish them: `quote.schema.ts`, `catalog.service.ts`. A file named after the entity has no suffix: `quote.ts`, `catalog.ts`. A schema that is the source of an inferred type stays in the type file. It does not need a separate `.schema.ts`.
- A type is imported with `import type`.
- A domain type and a boundary error type live in their own file. The adapter and the store import them. For the catalog that file is `stores/catalog/types.ts`. `Item` is `z.infer` of the schema in that file, and the adapter parses with that same schema. `RequestError` is written by hand, as in Data boundaries.

## Module

A `.ts` file exports named values. `export default` is not used in `.ts`. Vite and Vitest configs are the exception: their API requires a default export.

Import order: external packages, a blank line, workspace packages, a blank line, relative modules of this application. A new project does not add an `@/` alias. Under `moduleResolution: nodenext` a relative import ends in `.js`:

```ts
import { quote } from '../quote.js'
```

## Method comments

An exported function this change adds or edits has a JSDoc comment. The comment says what the function does and names every parameter. A local function this change adds or edits is commented the same way when its parameters are not obvious from the signature. The comment does not restate the type. A function this change does not touch keeps its existing comment, including the absence of one.

```ts
/**
 * Loads catalog items from the API.
 * @param signal Aborts this request when a newer load starts.
 */
export async function getItems(signal: AbortSignal): Promise<Result<Item[], RequestError>>
```

## Functions and classes

By default, code is modules of small functions and explicit data. Calculations and transformations stay apart from external effects and stay pure when the task allows it. A class holds state, an invariant, or a lifecycle, or it exists because the API requires a class. A class is not a grouping for functions. Composition is preferred. An extra abstraction, or a functional-programming library, is added only when it helps this task. `Result` is already the type for an expected failure.

## Data boundaries

Zod 4 describes external data. It both checks the value and produces the type. `unknown` arrives at the check. Functions after it accept the checked types.

Shared fields and constraints are reused. Form, request, response, and storage schemas are separate when their contracts differ. Derived schemas are built with Zod operations: `pick`, `omit`, `partial`, `extend`. A constraint written in the schema runs during the check. A schema that describes a length or an allowed list and does not reject a violation is not used.

The shared result of an operation that can fail expectedly:

```ts
type Result<T, E = unknown> =
  | { ok: true; data: T }
  | { ok: false; error: E }
```

Expected failures return through `Result`. An unexpected programming error is not automatically turned into a nameless failure: it stays an exception.

HTTP status is not part of that type. It lives on the network client's error, `RequestError`: `{ status: number; body: unknown }`. It is the client's error shape, not a parsed body. No HTTP response, or a body that could not be read, is `{ status: 0; body: null }`. A `200` whose body fails the schema stays `{ status: 200; body }`: that is a contract violation, not an HTTP failure. When the operation's contract requires JSON, an empty or invalid body is an error. A body-less response is allowed when the contract says so, including `204 No Content`.

The client boundary is `fetch`, then `unknown`, then `safeParse`, then `Result`. The store records data or an error. The screen shows the error.

```ts
export async function getItems(signal: AbortSignal): Promise<Result<Item[], RequestError>> {
  const { VITE_API_URL } = env()

  let response: Response
  try {
    response = await fetch(new URL('/items', VITE_API_URL), { signal })
  } catch (caught) {
    if (signal.aborted) throw caught
    return { ok: false, error: { status: 0, body: null } }
  }

  let body: unknown
  try {
    body = await readBody(response)
  } catch (caught) {
    if (signal.aborted) throw caught
    return { ok: false, error: { status: 0, body: null } }
  }

  if (!response.ok) return { ok: false, error: { status: response.status, body } }

  const parsed = itemsSchema.safeParse(body)
  if (!parsed.success) return { ok: false, error: { status: response.status, body } }
  return { ok: true, data: parsed.data }
}
```

A validation function enforces its stated constraints and rejects invalid input. Passing a value through without checking it is not validation. Returning the same value after a successful check is valid.

Bad: `function isPositive(value: number): number { return value }`

Good: `function parseAmount(value: unknown): Result<number, { issues: string[] }>` — on failure `safeParse` returns `{ ok: false; error }`.

## Tests

A test file sits next to the code and is named `*.test.ts`.

Tests cover the store, Zod schemas, and functions in `packages/utils`. A schema without a test that rejects a violation is not done.

The test replaces `fetch`. The network is not called:

```ts
vi.stubGlobal(
  'fetch',
  vi.fn(async () => new Response(JSON.stringify([{ id: '1', title: 'First' }]), { status: 200 })),
)
```

`vi.stubEnv('VITE_API_URL', 'http://localhost:3000')` sets the environment the schema reads at startup.

## Log and unfinished work

A browser log channel is chosen from a typed list: `log.page`, `log.fetch`, `log.router`. A message starts with its channel: `[PAGE]:: ...`. The client `log.ts` assigns the global `log` at startup, and the application imports that module once before `mount`. The global is declared in that module and in ESLint. The log object is not `any`. The server does not declare this global. The screen that starts a load writes an unexpected exception here and shows the failure.

An unfinished spot names the note's author:

```ts
// TODO:ROUTER Anna: describe meta for the settings screen
```

`AREA` is a short feature name (`ROUTER`, `API`). The prefix and the author make the note searchable.
