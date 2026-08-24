using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR && RAMND_HAS_GOOGLE_PLAY_GAMES
using GooglePlayGames;
#endif

/// <summary>
/// Google Play Games profile fields available after successful GPGS authentication.
/// </summary>
public static class GooglePlayGamesProfileProvider
{
    public static bool IsPluginReady
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR && RAMND_HAS_GOOGLE_PLAY_GAMES
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Initializes Play Games SDK early (before the user taps Link). Call once at game bootstrap
    /// on Android so <c>PlayGamesSdk.initialize</c> is not first hit inside ManuallyAuthenticate.
    /// </summary>
    public static void WarmUp()
    {
#if UNITY_ANDROID && !UNITY_EDITOR && RAMND_HAS_GOOGLE_PLAY_GAMES
        try
        {
#if !UGS_ENV_PRODUCTION
            PlayGamesPlatform.DebugLogEnabled = true;
#endif
            PlayGamesPlatform.Activate();
            AppLog.Info("Auth",
                $"GPGS warmup appId={GameInfo.ApplicationId} webClientSet={GameInfo.WebClientIdInitialized()}");
        }
        catch (System.Exception ex)
        {
            AppLog.Warn("Auth", $"Google Play Games warmup failed: {ex.Message}");
        }
#endif
    }

    public static string TryGetAuthenticatedDisplayName()
    {
#if UNITY_ANDROID && !UNITY_EDITOR && RAMND_HAS_GOOGLE_PLAY_GAMES
        try
        {
            if (!PlayGamesPlatform.Instance.IsAuthenticated())
                return null;

            string name = PlayGamesPlatform.Instance.GetUserDisplayName();
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch (System.Exception ex)
        {
            AppLog.Warn("Auth", $"Google Play Games display name read failed: {ex.Message}");
        }
#endif
        return null;
    }

    public static string TryGetAuthenticatedAvatarUrl()
    {
#if UNITY_ANDROID && !UNITY_EDITOR && RAMND_HAS_GOOGLE_PLAY_GAMES
        try
        {
            if (!PlayGamesPlatform.Instance.IsAuthenticated())
                return null;

            string url = PlayGamesPlatform.Instance.GetUserImageUrl();
            return string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        }
        catch (System.Exception ex)
        {
            AppLog.Warn("Auth", $"Google Play Games avatar URL read failed: {ex.Message}");
        }
#endif
        return null;
    }
}
