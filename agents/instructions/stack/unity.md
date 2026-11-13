# Unity

The marker is `ProjectSettings/ProjectVersion.txt`. An ordinary task does not raise the editor version in that file.

Generated root directories — `/Library/`, `/Temp/`, `/Obj/`, `/Logs/`, `/UserSettings/`, `/Build*` — stay out of context. Patterns in the preset are anchored to the root, otherwise they hide same-named folders under `Assets/`. The preset is `agents/instructions/cursorignore/unity`. A `.meta` file stays visible: it moves with the asset, and its GUID is not recreated.

`*.unity`, `*.prefab`, and `*.asset` are not indexed. A change to a serialized field or a component reference reads and edits the affected asset by path. `*.csproj` and `*.sln` are generated; do not edit them. The preset is `agents/instructions/cursorignore/unity.indexing`.

`Packages/manifest.json` and `packages-lock.json` are dependencies. Do not change them without a task, same as package versions in other stacks.

Keep existing asmdefs. Leave scenes, prefabs, and serialized fields unchanged except for the field or reference this task changes. An architecture migration is a separate task.

## Serialization

A renamed serialized field keeps `[FormerlySerializedAs]` with the previous name. The attribute preserves the saved value across that rename. A changed type, a split or merged field, or a dropped reference is checked in the affected scenes, prefabs, and `.asset` files. Those files stay out of the index; read the affected path. `/Library/` and build output stay out of context.

## Components

A C# change is finished together with the component, reference, and scene or prefab settings it requires. A script that expects a missing reference leaves the task open.

## Lifetime

A subscription, an asynchronous operation, and a resource end with their owner. The change accounts for the object being enabled again and for shutdown. The subscribe and release points are the ones the project already uses.

## Checks

Choose a check for the changed logic, scene or prefab, Play Mode, or target build. Diff reading and static inspection are always available. Launch Unity, Play Mode, Unity-hosted tests, or a build only through a command authorized in `PROJECT.md` or explicitly requested by the user. The restriction applies to intermediate and final checks alike. A standalone test that does not launch Unity or build the project may run when relevant. Report any missing authorization or unavailable check; do not claim the behavior was verified.

## Runtime and Editor

Runtime code does not reference `UnityEditor`. Keep Editor-only code out of Player assemblies. Without asmdefs, use an `Editor` directory. Under an existing asmdef, that directory alone does not isolate its scripts: use an Editor-only asmdef or an asmref to an Editor-only assembly. Check the actual assembly and platform settings. Do not introduce assembly definitions throughout a project just for this rule.

## C# fields and private methods

A private field or method this change adds uses an explicit `private` modifier. A private instance field this change adds uses an `_camelCase` name. C# defaults do not replace the modifier. A field or method this change does not add keeps its name and modifier.

A serialized field keeps its name unless this task's purpose is that rename. The rename is a separate change. It keeps `[FormerlySerializedAs]` and checks the affected assets, as the serialization section requires.

```csharp
// Bad — implicit private
[SerializeField] Button backpackButton;
float radius;
readonly List<Item> items = new();

// Good
[SerializeField] private Button _backpackButton;
private float _radius;
private readonly List<Item> _items = new();
```

```csharp
// Bad
void RefreshLayout() { }

// Good
private void RefreshLayout() { }
```

Public properties, public methods, and interface members do not use a leading underscore. Structs or DTOs used only as data bags may keep public fields without an underscore when that matches the surrounding serialization or layer-crossing style. A static utility type with no instance state exposes static methods only.

## Method comments

A public method this change adds or edits has an XML comment. The comment says what the method does and names every parameter. A private method this change adds or edits is commented the same way when its parameters are not obvious from the signature. The comment does not restate the type. A method this change does not touch keeps its existing comment, including the absence of one.

```csharp
/// <summary>Loads catalog items from the API.</summary>
/// <param name="cancellationToken">Aborts this request when a newer load starts.</param>
Item[] LoadItems(CancellationToken cancellationToken);
```
