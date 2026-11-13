# Node API

For a server directory. Read with [typescript.md](typescript.md).

The server is separate from the client. The HTTP framework is Hono. Domain calculation does not import Hono.

A route is a handler function. A class appears when an area has several coordinated operations and shared state, as in Functions and classes of `typescript.md`. One calculation function is not wrapped in a class.

```text
<domain>/       this area's calculation, for example quote.ts
http/           routes: read the body and turn Result into a response
log.ts          server channels
config/
types/
models/
utils/
```

The request body is `unknown` until the handler's schema checks it. The domain function then accepts the inferred type and returns `Result`.

An expected schema failure is `400`. An expected domain failure is `422` and the error body from `Result`. An exception is not rewritten into `Result`: `onError` writes the stack to `log.http` and responds `500` without the exception text.

```ts
export function createApp() {
  const app = new Hono()

  app.onError((error, c) => {
    log.http(error.stack ?? error.message)
    return c.json({ reason: 'internal' }, 500)
  })

  app.post('/quotes', async (c) => {
    let body: unknown
    try {
      body = await c.req.json()
    } catch {
      body = undefined
    }

    const parsed = quoteRequestSchema.safeParse(body)
    if (!parsed.success) {
      return c.json({ issues: parsed.error.issues.map((issue) => issue.message) }, 400)
    }

    const result = quote(parsed.data)
    if (!result.ok) return c.json(result.error, 422)

    return c.json({ amount: result.data })
  })

  return app
}
```

The server log is the `log.ts` module, not the browser global. The channel is typed: `log.http`, `log.domain`. A message starts with its place: `[HTTP]:: ...`. The log object is not `any`.
