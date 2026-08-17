namespace RamnD.GameServices.Ads.Privacy
{
    /// <summary>
    /// Inputs for <see cref="AdsPrivacyPipeline"/>. App / AdMob IDs are not passed here —
    /// native inject comes from LevelPlay AdMob settings (or Google Mobile Ads settings).
    /// </summary>
    public sealed class AdsPrivacyOptions
    {
        /// <summary>True when the player is under 13 / child-directed (COPPA).</summary>
        public bool IsChildDirected { get; set; }

        /// <summary>
        /// UMP debug geography override for device testing. Leave <see cref="AdsPrivacyDebugGeography.Disabled"/> in production.
        /// </summary>
        public AdsPrivacyDebugGeography DebugGeography { get; set; } = AdsPrivacyDebugGeography.Disabled;

        /// <summary>
        /// Optional hashed test device id for UMP debug (see AdMob UMP docs). Ignored when empty.
        /// </summary>
        public string DebugTestDeviceHashedId { get; set; }
    }

    /// <summary>Maps to Google UMP <c>DebugGeography</c> when GMA is present.</summary>
    public enum AdsPrivacyDebugGeography
    {
        Disabled = 0,
        Eea = 1,
        NotEea = 2,
    }
}
