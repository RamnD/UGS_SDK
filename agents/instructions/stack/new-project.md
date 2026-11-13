# New TypeScript project and build configuration

For new project setup and for changes to `tsconfig`, Vite config, application scripts, environment variables, or workspace layout. Versions, commands, CI, ESLint, and hooks are in [js-tooling.md](js-tooling.md).

The `.cursorignore` preset is `agents/instructions/cursorignore/node`. The lockfile and `dist` stay out of the index, and can still be opened for a task: `agents/instructions/cursorignore/node.indexing`.

The editor checks the project with the same TypeScript as `tsc`. In `.vscode/settings.json`, `typescript.tsdk` points at the installed TypeScript `lib` directory that contains `tsserver.js`, and `typescript.enablePromptUseWorkspaceTsdk` is on. `.vscode` is not copied into a project on sync: the project writes this setting itself.

## Browser, Vite

```json
{
  "dev": "vite --mode dev",
  "typecheck": "tsc --noEmit",
  "build": "pnpm typecheck && vite build --mode prod"
}
```

The scripts above are for a browser client without Vue. For Vue, replace `typecheck` with `vue-tsc --noEmit`; do not add `vue-tsc` to a client without Vue. `typecheck` runs inside `build` before the bundle. In dev, `vite-plugin-checker` uses the corresponding TypeScript or Vue checker. The Vite build does not repeat that check: the plugin's `enableBuild` is off.

```json
{
  "compilerOptions": {
    "target": "esnext",
    "module": "esnext",
    "moduleResolution": "bundler",
    "strict": true,
    "noEmit": true,
    "jsx": "preserve",
    "lib": ["ES2022", "DOM", "DOM.Iterable"],
    "types": ["vite/client"],
    "noUncheckedIndexedAccess": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noImplicitReturns": true,
    "verbatimModuleSyntax": true,
    "forceConsistentCasingInFileNames": true,
    "erasableSyntaxOnly": true,
    "isolatedModules": true,
    "skipLibCheck": true
  }
}
```

`skipLibCheck` is on for the browser and for Node: application files are checked, dependency declarations are not.

## Node API

Vite scripts are not copied here. `@types/node` matches the `engines` major.

```json
{
  "dev": "tsx watch src/index.ts",
  "typecheck": "tsc --noEmit",
  "build": "tsc"
}
```

```json
{
  "compilerOptions": {
    "target": "esnext",
    "module": "nodenext",
    "moduleResolution": "nodenext",
    "strict": true,
    "skipLibCheck": true,
    "outDir": "dist",
    "rootDir": "src",
    "noUncheckedIndexedAccess": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noImplicitReturns": true,
    "verbatimModuleSyntax": true,
    "forceConsistentCasingInFileNames": true,
    "erasableSyntaxOnly": true
  }
}
```

## Environment

Vite modes are `dev`, `staging`, `prod`, and `local` when needed. One Zod schema names the variables. `ImportMetaEnv` extends the type inferred from it and takes each field's type from that same inference. `readEnv` runs once at startup, before `mount`. Later code reads the checked value. Secrets are not committed and are not written as literals in a CI file.

```ts
const envSchema = z.object({
  VITE_API_URL: z.url({ protocol: /^https?$/ }),
})

export type AppEnv = z.infer<typeof envSchema>

declare global {
  interface ImportMetaEnv extends AppEnv {}
}

let current: AppEnv | undefined

export function readEnv(source: unknown): AppEnv {
  const parsed = envSchema.safeParse(source)
  if (!parsed.success) throw new Error('Invalid environment')
  current = parsed.data
  return current
}

export function env(): AppEnv {
  if (!current) throw new Error('Environment is not loaded')
  return current
}
```

The schema field is declared once, on `AppEnv`. An empty `interface ImportMetaEnv extends AppEnv` exists for declaration merging. ESLint accepts it with `@typescript-eslint/no-empty-object-type` and `allowInterfaces: 'with-single-extends'`. `env()` before `readEnv` reports that the environment has not been read yet.

A local name example: `VITE_API_URL=http://localhost:3000`. The protocol allows `http` and `https`. `ftp`, `file`, and `mailto` fail the schema.

## Several applications

Several applications of one new product are a pnpm workspace. Shared code is a package in `packages/` with its own `package.json` and a `workspace:*` dependency. npm is not used in that new repository.
