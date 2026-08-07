# Ads privacy (ATT → UMP → COPPA)

← [Back to README](../README.md) · [Ads (LevelPlay)](ads.md)

---

## Overview

Before `LevelPlay.Init`, run the shared privacy pipeline:

1. **ATT** (iOS) — App Tracking Transparency  
2. **UMP** — Google User Messaging Platform (EEA/UK consent form)  
3. **COPPA / GDPR flags** on LevelPlay (`SetCOPPA`, `SetGDPRConsent`)

Age-gate UI stays in the game. Pass `IsChildDirected` into the pipeline.

```csharp
using RamnD.GameServices.Ads.Privacy;

await AdsPrivacyPipeline.EnsureCompletedAsync(new AdsPrivacyOptions
{
    IsChildDirected = ageGateIsChild,
    // Debug only:
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

## AdMob App ID (no duplicate config in this SDK)

UMP reads the AdMob App ID from the **native** app (`GADApplicationIdentifier` / `APPLICATION_ID`).

Do **not** pass App IDs into `AdsPrivacyOptions`. Use the existing LevelPlay AdMob slot:

1. Create apps in [AdMob](https://apps.admob.com/) → copy Android / iOS App IDs (`ca-app-pub-…~…`)
2. AdMob → Privacy & messaging → publish **GDPR** (+ **IDFA** for iOS)
3. In Unity: **Ads Mediation → Developer Settings / Mediated Network Settings** (or `AdMobConfigurations.json`):
   - set `AndroidAdMobAppId` / `IOSAdMobAppId`
   - enable AdMob (`EnableAdMob`) so LevelPlay postprocessors inject the ID into AndroidManifest / Info.plist

LevelPlay **App Key** and IAP store SKUs are different identifiers — they are not AdMob App IDs.

## Google Mobile Ads package (required for UMP UI)

UMP APIs ship with **Google Mobile Ads for Unity** (`com.google.ads.mobile`).

Add OpenUPM scope `com.google` and dependency, e.g.:

```json
"com.google.ads.mobile": "11.3.0"
```

When the package is present, this SDK enables define `RAMND_HAS_GOOGLE_MOBILE_ADS` and compiles optional assembly `RamnD.GameServices.UGS.GoogleUmp`, which registers the real UMP gate.

Without GMA: ATT + COPPA still run; UMP is a logged no-op (fail-open).

## Child-directed

When `IsChildDirected == true`:

- ATT is skipped  
- UMP form is not shown (`TagForUnderAgeOfConsent`)  
- `LevelPlayPrivacySettings.SetCOPPA(true)` and `SetGDPRConsent(false)`

## Testing

- Set `DebugGeography = Eea` (+ test device hashed id from logcat/Xcode) to force the form outside the EU  
- Non-EEA → form should not appear  
- After deny/accept, ads should still load without crashing (restricted vs personalized)

## API

| Type | Role |
|------|------|
| `AdsPrivacyPipeline` | Orchestrator |
| `AdsPrivacyOptions` | `IsChildDirected`, optional debug geography |
| `IAdsUmpConsentGate` | Pluggable UMP (default null / Google) |
