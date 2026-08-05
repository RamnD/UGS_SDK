# Ads (LevelPlay mediation) — optional Pangle

← [Back to README](../README.md)

---
## Overview

This SDK doesn’t know individual ad networks (AdMob, Pangle, etc.) directly. Instead, it exposes a single entry point:

- provide an `IAdsManager` implementation via `.WithAds(...)` in `UGSServicesBuilder`
- in your game, call `ShowRewardedAd(...)` / `ShowInterstitial(...)` using the same `placementId` you configure in the ad mediator

For new projects the recommended path is `LevelPlayAdsManager`, which wraps **Unity LevelPlay** SDK.

---
## Pangle support (optional) via LevelPlay mediation

`Pangle` is supported when you enable it in **LevelPlay Mediation** (Network Manager). The SDK will then serve Pangle ads the same way it serves any other mediated network — by your LevelPlay `placementId`s.

### Unity Editor setup

1. Open **Ads Mediation → Network Manager** in your Unity project.
2. Add/Download the **Pangle** adapter for Android (and iOS, if you use iOS).
3. Configure Pangle parameters in LevelPlay (sign in to your Pangle account and fill required adapter fields).

### Android (optional Gradle dependencies)

Depending on how you import the adapter, LevelPlay can require additional Gradle dependencies. In that case, follow the adapter’s instructions and (when needed) ensure your app module build files include:

- repository: `https://artifact.bytedance.com/repository/pangle`
- dependencies (use versions required by the LevelPlay Pangle adapter you download):
  - `com.unity3d.ads-mediation:pangle-adapter`
  - `com.pangle.global:pag-sdk`

### Placement IDs

Your game still calls the SDK like:

```csharp
GameServicesLocator.Services.Ads.ShowRewardedAd(
    placementId: "YOUR_LEVELPLAY_AD_UNIT_ID",
    onSuccess: () => { /* grant */ },
    onFailed:  () => { /* no ad */ });
```

Make sure `placementId` matches the **LevelPlay ad units / placements** you configured for Pangle in the LevelPlay dashboard.

---
## What happens if Pangle isn’t enabled

If you don’t enable Pangle in LevelPlay mediation (or the adapter isn’t imported correctly), the SDK won’t crash — it will just fail to load/show ads and call your `onFailed` handlers (when provided).

For offline testing, note that the SDK also uses `NetworkStatus.IsOnline` to skip shows and invoke `onFailed` immediately.

