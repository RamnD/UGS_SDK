using System;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// Shared anonymous SignIn restore when Auth is unexpectedly signed out
/// after recover / resume while the network is still available.
/// </summary>
public static class AuthSessionEnsure
{
    /// <summary>
    /// Returns true when Auth is signed in already or after anonymous restore.
    /// Fails softly when Auth is unavailable, offline, cancelled, or restore fails.
    /// </summary>
    public static async UniTask<bool> EnsureSignedInAsync(
        string context,
        CancellationToken cancellationToken = default)
    {
        IAuthService auth = GameServicesLocator.Services?.Auth;
        if (auth == null)
            return false;

        if (auth.IsSignedIn)
            return true;

        if (!NetworkStatus.IsOnline)
        {
            Debug.LogWarning($"[Auth] {context}: signed out and offline - skip anonymous SignIn.");
            return false;
        }

        try
        {
            Debug.LogWarning($"[Auth] {context}: signed out - restoring anonymous session...");
            bool ok = await auth.SignInAsync(AuthPlatform.Anonymous, cancellationToken);
            if (ok && auth.IsSignedIn)
            {
                string playerId = auth.GetPlayerId();
                Debug.Log($"[Auth] {context}: anonymous SignIn OK. PlayerId={playerId}");
                return true;
            }

            Debug.LogWarning($"[Auth] {context}: anonymous SignIn did not restore session.");
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Auth] {context}: anonymous SignIn failed: {ex.Message}");
            return false;
        }
    }
}
