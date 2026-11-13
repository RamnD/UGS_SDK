# Vue 3 and Pinia

For a Vue product. Read with [typescript.md](typescript.md).

## Stack

- Vue 3.5, `<script setup lang="ts">`.
- Pinia 4 and the peer dependency `@vue/devtools-api`. A store uses the setup-store syntax.
- Vue Router 5. The route table is written out.
- Vite and `vue-tsc`.

Use the Composition API. Options API, Vuex, mixins, and reactivity-transform macros (`$ref`, `$computed`, `$()`) are not used.

## Folders

A screen owns its flow. Root technical folders stay for what several screens use.

```text
views/<screen>/            the screen and its parts
components/                elements used by several screens
stores/<domain>/           store, network adapter, and neighboring modules of one area
composables/               reactive behavior not tied to one screen
configs/
types/
utils/                     only what the shared package and VueUse do not have
router/index.ts
```

Parts of one screen sit next to it. A component moves to `components/` on its second use. The area's network adapter sits in `stores/<domain>/` next to the store: the store is `stores/catalog/catalog.ts`, the adapter is `stores/catalog/items.ts`, and the area types are `stores/catalog/types.ts`.

A route describes the screen and its contract in `meta`: chrome, transition, data to warm. `RouteMeta` is typed by declaration merging in `router/index.ts`. File-based routing is not used: the screen contract is the route table.

```ts
declare module 'vue-router' {
  interface RouteMeta {
    title: string
  }
}
```

Vue, components, and stores are not auto-imported. An import is visible in the file and in search.

## Component

Block order: `script`, then `template`, then `style`.

```vue
<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  title: string
  count?: number
}>()

const emit = defineEmits<{
  select: [id: string]
}>()

const label = computed(() => props.title)
</script>
```

Props and emits are declared as types. A component receives data and reports an action. Markup does not call the network: the store, or a function the store calls, does that.

Markup, a long flow, and direct requests in one component are split. The flow lives in a store or a composable.

Component styles, when present, live in `<style scoped>`. A new project does not add CSS modules or Tailwind.

`v-html` accepts only a string that a separate function has already cleaned. A raw server response or user text does not go into `v-html`.

Before writing a composable, check VueUse. Timer, interval, resize, mouse, debounce, clipboard, and websocket are already there. Write a local `use*` when VueUse does not have the behavior or the behavior is tied to the domain. A new project adds VueUse together with the scenario it covers.

## Store

`items === null` means only "not loaded yet". The error lives in a separate `error`. A request in flight sets `loading`. A repeated `load` aborts the previous request with `AbortController`. A cancelled call writes neither the list nor the error. The list already on screen stays until a new successful response arrives. `loading` clears in `finally`, and only when this call is still current: `controller === next`. An exception from an older request does not clear the new call's flag.

```ts
export const useCatalogStore = defineStore('catalog', () => {
  const items = ref<Item[] | null>(null)
  const error = ref<RequestError | null>(null)
  const loading = ref(false)
  let controller: AbortController | undefined

  async function load(): Promise<Result<Item[], RequestError> | undefined> {
    controller?.abort()
    const next = new AbortController()
    controller = next
    loading.value = true
    error.value = null

    try {
      const result = await getItems(next.signal)
      if (next.signal.aborted) return undefined
      if (!result.ok) {
        error.value = result.error
        return result
      }

      items.value = result.data
      return result
    } catch (caught) {
      if (next.signal.aborted) return undefined
      throw caught
    } finally {
      if (controller === next) loading.value = false
    }
  }

  return { items, error, loading, load }
})
```

The adapter half of the chain and `RequestError` are in Data boundaries of `typescript.md`. The screen shows the failure itself. An expected failure is already in `error`. `load` rethrows an unexpected exception and leaves `error` empty. The screen catches that rejection, writes the stack to `log.page`, and shows the same failure:

```vue
<p v-if="loading">Loading</p>
<p v-if="error || failed">Could not load the catalog</p>
<ul v-if="items">
  <li v-for="item in items" :key="item.id">{{ item.title }}</li>
</ul>
```

A setup store returns every piece of state that must stay reactive. Code is grouped by responsibility: a large area keeps mappers, operations, and the network adapter next to the store. That is several modules, not one file.

Reading `catalog.items` directly in a template and in `setup` is valid. `storeToRefs` is needed when reactive state or a getter is pulled out into its own variable. Actions are taken directly, without `storeToRefs`:

```ts
const catalog = useCatalogStore()
const { items, error, loading } = storeToRefs(catalog)
const { load } = catalog
const failed = ref(false)

onMounted(() => {
  void load().catch((caught: unknown) => {
    failed.value = true
    const detail = caught instanceof Error ? (caught.stack ?? caught.message) : String(caught)
    log.page(`Catalog\n${detail}`)
  })
})
```

A store does not import a component. An HTTP module does not import a store and does not show a notification: the notification stays on the screen.

An expected failure is visible in `Result`. The screen decides whether to show it. The handler above is what shows an unexpected exception; `void load()` alone drops that rejection and leaves the screen blank.

## Names

A screen component and an element are PascalCase: `CatalogView.vue`, `IconButton.vue`. A composable is `useThing.ts`. A store is `useThingStore` in `thing.ts`.
