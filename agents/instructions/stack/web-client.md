# Browser client without Vue

For a browser client that does not depend on `vue`. Read with [typescript.md](typescript.md).

The hierarchy grows from the screen. Root technical folders hold only what several screens use.

```text
pages/<screen>/index.html
pages/<screen>/index.css
pages/<screen>/index.ts
pages/<screen>/logic/controllers
pages/<screen>/components
components/          elements used by several screens
services/            animation, time, templates, shared process state
configs/             static objects, styles, the route table
types/
utils/               only what the shared package does not have
core/                the engine, when it is not itself a screen
```

A page receives its dependencies from outside. A subscription is created in `init` and removed in `destroy`. The page or the service keeps the subscription list.

A new screen is a folder plus a row in the route table. A new entity of an existing kind is a config file plus an entry in a typed map. Class names and durations come from the style config.
