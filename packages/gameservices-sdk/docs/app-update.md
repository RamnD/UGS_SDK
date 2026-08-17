# App update (Google Play Immediate)

← [Back to README](../README.md)

Android-only native Play In-App Update. **iOS has no equivalent API** — do not add a custom store popup in this SDK.

## Behaviour

1. As soon as the loading screen is visible, the game **awaits** `AppUpdatePipeline.PromptIfAvailableAsync()` — before shader warmup / UGS / lobby.
2. If `com.google.play.appupdate` is installed, optional assembly `RamnD.GameServices.UGS.GooglePlayAppUpdate` registers the Play adapter (`AlwaysLinkAssembly` + `link.xml`, same pattern as UMP).
3. Play `GetAppUpdateInfo` → if an update is available and **Immediate** is allowed, `StartUpdate` shows the **native Play UI**. The process restarts after install; if the player dismisses, loading continues.
4. Editor / iOS / sideload / no Play plugin → no-op (fail-open). A just-installed build typically reports no update, so first-run intro/tutorial is unaffected.

Flexible updates and iOS “open App Store” UI are out of scope.

## Game install

OpenUPM (same registry as Play Review):

```json
"scopedRegistries": [{
  "name": "package.openupm.com",
  "url": "https://package.openupm.com",
  "scopes": ["com.google.play.appupdate"]
}],
"dependencies": {
  "com.google.play.appupdate": "1.8.4"
}
```

Call as the first step under the loading screen (await it — Immediate restarts the process):

```csharp
using RamnD.GameServices.AppUpdate;

await AppUpdatePipeline.PromptIfAvailableAsync(cancellationToken);
```

Test on a **Play-installed** internal/closed track build; Editor and debug APKs report `UpdateNotAvailable`.
