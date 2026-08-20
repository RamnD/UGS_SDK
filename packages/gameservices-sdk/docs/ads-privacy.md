# Ads privacy (ATT → CMP → COPPA)

← [Back to README](../README.md) · [Ads (LevelPlay)](ads.md)

---

## Overview

Before `LevelPlay.Init`, run the shared privacy pipeline:

1. **ATT** (iOS) — App Tracking Transparency  
2. **CMP** — consent form (Google UMP **or** InMobi Choice)  
3. **COPPA / GDPR flags** on LevelPlay (`SetCOPPA`; GDPR via `SetGDPRConsent` on LevelPlay 9.5+, or `LevelPlay.SetConsent` on 9.4.x)

Age-gate UI stays in the game. Pass `IsChildDirected` into the pipeline.

```csharp
using RamnD.GameServices.Ads.Privacy;

await AdsPrivacyPipeline.EnsureCompletedAsync(new AdsPrivacyOptions
{
    IsChildDirected = ageGateIsChild,
    InMobiChoicePCode = "YOUR_PCODE_WITHOUT_p-_PREFIX", // when using InMobi Choice
    // Debug only (Google UMP):
    // DebugGeography = AdsPrivacyDebugGeography.Eea,
    // DebugTestDeviceHashedId = "YOUR_HASHED_ID",
});

// then LevelPlay.Init / BeginSdkInitialization
```

Privacy options (settings / profile):

```csharp
if (AdsPrivacyPipeline.IsPrivacyOptionsRequired)
    await AdsPrivacyPipeline.ShowPrivacyOptionsAsync();
```

## CMP providers

The pipeline uses a pluggable `IAdsUmpConsentGate`. Two built-in options:

| Provider | Package | Registration |
|----------|---------|--------------|
| **InMobi Choice** (recommended when AdMob is disabled) | InMobi CMP Unity package (manual import) | Auto when `ChoiceCMP` is in the project + `InMobiChoicePCode` set |
| **Google UMP** | `com.google.ads.mobile` 8.5.0+ | Optional assembly `RamnD.GameServices.UGS.GoogleUmp` |

If both are present, **InMobi Choice wins** (registers after Google UMP bootstrap).

Without any CMP: ATT + COPPA still run; consent form is a logged no-op (fail-open).

### InMobi Choice

1. Configure the app in the [InMobi Choice portal](https://choice.inmobi.com/) and download the Unity SDK.
2. Import the package (`Assets → Import Package → Custom Package`).
3. Add `com.unity.nuget.newtonsoft-json` if not already present.
4. Copy your **p-code** from the portal (omit the leading `p-` prefix).
5. Pass it via `AdsPrivacyOptions.InMobiChoicePCode` at runtime (e.g. from your `ScriptableObject` config).

SDK calls (via reflection):

- `ChoiceCMP.StartChoice(pCode, shouldDisplayIDFA: false)` — ATT is handled separately by the pipeline  
- Waits for `CMPUIStatusChangedEvent` (**Visible** → **Dismissed** / Hidden / Disabled) so bootstrap does not continue under an open form  
- `ChoiceCMP.ForceDisplayUI()` — privacy settings entry point (same dismiss wait)  
- `ChoiceCMP.GetTCString()` + `IABTCF_PurposeConsents` → LevelPlay GDPR flag

Android: add InMobi CMP gradle deps to `mainTemplate.gradle` (see InMobi docs).  
iOS: resolve `InMobiCMP` via CocoaPods / EDM (`InMobiCMPDependencies.xml` in the Unity Choice package) — no manual Xcode framework drop-in.

### Google UMP (optional)

UMP reads the AdMob App ID from the **native** app (`GADApplicationIdentifier` / `APPLICATION_ID`).

Do **not** pass App IDs into `AdsPrivacyOptions`. Use LevelPlay AdMob settings when AdMob mediation is enabled.

Add OpenUPM scope `com.google` and dependency:

```json
"com.google.ads.mobile": "11.3.0"
```

When GMA **8.5.0+** is present, optional assembly `RamnD.GameServices.UGS.GoogleUmp` sets `RAMND_HAS_GOOGLE_MOBILE_ADS` and registers the UMP gate.

## Child-directed

When `IsChildDirected == true`:

- ATT is skipped  
- CMP form is not shown  
- `LevelPlayPrivacySettings.SetCOPPA(true)` and GDPR consent false (9.5 `SetGDPRConsent`, 9.4 `LevelPlay.SetConsent`)

## Testing

- **InMobi:** configure test geography in the Choice portal; verify first-launch form + Profile → Privacy Options (`ForceDisplayUI`).
- **Google UMP:** set `DebugGeography = Eea` (+ test device hashed id from logcat/Xcode).
- Non-EEA → form should not appear.
- After deny/accept, ads should still load without crashing (restricted vs personalized).

## API

| Type | Role |
|------|------|
| `AdsPrivacyPipeline` | Orchestrator |
| `AdsPrivacyOptions` | `IsChildDirected`, `InMobiChoicePCode`, optional UMP debug |
| `IAdsUmpConsentGate` | Pluggable CMP (InMobi / Google / custom) |
