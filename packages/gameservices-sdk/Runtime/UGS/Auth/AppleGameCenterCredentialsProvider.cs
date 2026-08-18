using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR && APPLE_GAMEKIT
using Apple.GameKit;
#endif

/// <summary>
/// GameKit -> UGS Apple Game Center credentials.
/// Requires Apple.Core + Apple.GameKit packages and scripting define <c>APPLE_GAMEKIT</c>.
/// </summary>
public static class AppleGameCenterCredentialsProvider
{
    static int _requestInFlight;

    public static bool IsPluginReady
    {
        get
        {
#if UNITY_IOS && !UNITY_EDITOR && APPLE_GAMEKIT
            return true;
#else
            return false;
#endif
        }
    }

    public static string TryGetAuthenticatedDisplayName()
    {
#if UNITY_IOS && !UNITY_EDITOR && APPLE_GAMEKIT
        try
        {
            var localPlayer = GKLocalPlayer.Local;
            if (localPlayer == null || !localPlayer.IsAuthenticated)
                return null;

            if (!string.IsNullOrWhiteSpace(localPlayer.DisplayName))
                return localPlayer.DisplayName.Trim();

            if (!string.IsNullOrWhiteSpace(localPlayer.Alias))
                return localPlayer.Alias.Trim();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Auth] Game Center display name read failed: {ex.Message}");
        }
#endif
        return null;
    }

    public static async UniTask<Texture2D> TryLoadAuthenticatedPhotoAsync(
        CancellationToken cancellationToken = default)
    {
#if UNITY_IOS && !UNITY_EDITOR && APPLE_GAMEKIT
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var localPlayer = GKLocalPlayer.Local;
            if (localPlayer == null || !localPlayer.IsAuthenticated)
                return null;

            return await localPlayer.LoadPhoto(GKPlayer.PhotoSize.Normal);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Auth] Game Center photo load failed: {ex.Message}");
        }
#endif
        await UniTask.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    public static async UniTask<AppleGameCenterCredentials> RequestAsync(
        CancellationToken cancellationToken = default)
    {
#if UNITY_IOS && !UNITY_EDITOR && APPLE_GAMEKIT
        if (Interlocked.CompareExchange(ref _requestInFlight, 1, 0) != 0)
        {
            Debug.LogWarning("[Auth] Apple Game Center auth already in progress.");
            return null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!GKLocalPlayer.Local.IsAuthenticated)
            {
                await GKLocalPlayer.Authenticate();
                cancellationToken.ThrowIfCancellationRequested();
            }

            var localPlayer = GKLocalPlayer.Local;
            if (localPlayer == null || !localPlayer.IsAuthenticated)
            {
                Debug.LogWarning("[Auth] Apple Game Center: player is not authenticated.");
                return null;
            }

            var fetchItemsResponse = await GKLocalPlayer.Local.FetchItems();
            cancellationToken.ThrowIfCancellationRequested();
            if (fetchItemsResponse == null)
            {
                Debug.LogError("[Auth] Apple Game Center: FetchItems returned null.");
                return null;
            }

            byte[] signatureBytes = fetchItemsResponse.GetSignature();
            byte[] saltBytes = fetchItemsResponse.GetSalt();
            if (signatureBytes == null || signatureBytes.Length == 0 || saltBytes == null || saltBytes.Length == 0)
            {
                Debug.LogError("[Auth] Apple Game Center: signature/salt missing.");
                return null;
            }

            var credentials = new AppleGameCenterCredentials
            {
                Signature = Convert.ToBase64String(signatureBytes),
                TeamPlayerId = localPlayer.TeamPlayerId,
                PublicKeyUrl = fetchItemsResponse.PublicKeyUrl,
                Salt = Convert.ToBase64String(saltBytes),
                Timestamp = ConvertTimestamp(fetchItemsResponse.Timestamp),
            };

            if (!credentials.IsValid)
            {
                Debug.LogError("[Auth] Apple Game Center: credentials invalid after FetchItems.");
                return null;
            }

            return credentials;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Auth] Apple Game Center auth failed: {ex.Message}");
            return null;
        }
        finally
        {
            Interlocked.Exchange(ref _requestInFlight, 0);
        }
#else
        Debug.LogWarning(
            "[Auth] Apple Game Center requires iOS device build + Apple.GameKit packages " +
            "and scripting define APPLE_GAMEKIT.");
        await UniTask.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        return null;
#endif
    }

    public static async Task<AppleGameCenterCredentials> RequestAsTaskAsync(
        CancellationToken cancellationToken = default) =>
        await RequestAsync(cancellationToken);

#if UNITY_IOS && !UNITY_EDITOR && APPLE_GAMEKIT
    static ulong ConvertTimestamp(object timestamp)
    {
        switch (timestamp)
        {
            case ulong u:
                return u;
            case long l when l >= 0:
                return (ulong)l;
            case int i when i >= 0:
                return (ulong)i;
            case string s when ulong.TryParse(s, out ulong parsed):
                return parsed;
            default:
                if (timestamp != null && ulong.TryParse(timestamp.ToString(), out ulong fromString))
                    return fromString;
                return 0;
        }
    }
#endif
}
