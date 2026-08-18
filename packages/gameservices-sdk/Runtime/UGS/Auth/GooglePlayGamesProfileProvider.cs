using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
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
#if UNITY_ANDROID && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

    public static string TryGetAuthenticatedDisplayName()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (!PlayGamesPlatform.Instance.IsAuthenticated())
                return null;

            string name = PlayGamesPlatform.Instance.GetUserDisplayName();
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Auth] Google Play Games display name read failed: {ex.Message}");
        }
#endif
        return null;
    }

    public static string TryGetAuthenticatedAvatarUrl()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (!PlayGamesPlatform.Instance.IsAuthenticated())
                return null;

            string url = PlayGamesPlatform.Instance.GetUserImageUrl();
            return string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Auth] Google Play Games avatar URL read failed: {ex.Message}");
        }
#endif
        return null;
    }
}
