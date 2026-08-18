using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR && RAMND_HAS_APPLE_SIGNIN
using AppleAuth;
using AppleAuth.Enums;
using AppleAuth.Extensions;
using AppleAuth.Interfaces;
using AppleAuth.Native;
#endif

/// <summary>
/// Apple Sign-In identity token (JWT) bridge for UGS Link/SignIn with Apple.
/// Requires <c>com.lupidan.apple-signin-unity</c>.
/// </summary>
public static class AppleSignInIdentityTokenProvider
{
    static int _requestInFlight;

    public static bool IsPluginInstalled
    {
        get
        {
#if UNITY_IOS && !UNITY_EDITOR && RAMND_HAS_APPLE_SIGNIN
            return AppleAuthManager.IsCurrentPlatformSupported;
#else
            return false;
#endif
        }
    }

    public static async UniTask<string> RequestAsync(CancellationToken cancellationToken = default)
    {
#if UNITY_IOS && !UNITY_EDITOR && RAMND_HAS_APPLE_SIGNIN
        if (!AppleAuthManager.IsCurrentPlatformSupported)
        {
            Debug.LogError("[Auth] Apple Sign-In is not supported on this device.");
            return null;
        }

        if (Interlocked.CompareExchange(ref _requestInFlight, 1, 0) != 0)
        {
            Debug.LogWarning("[Auth] Apple Sign-In already in progress.");
            return null;
        }

        try
        {
            var manager = new AppleAuthManager(new PayloadDeserializer());
            var tcs = new UniTaskCompletionSource<string>();

            using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
            {
                var loginArgs = new AppleAuthLoginArgs(LoginOptions.IncludeEmail | LoginOptions.IncludeFullName);
                manager.LoginWithAppleId(
                    loginArgs,
                    credential =>
                    {
                        if (credential is not IAppleIDCredential appleIdCredential)
                        {
                            tcs.TrySetResult(null);
                            return;
                        }

                        byte[] tokenBytes = appleIdCredential.IdentityToken;
                        if (tokenBytes == null || tokenBytes.Length == 0)
                        {
                            Debug.LogError("[Auth] Apple Sign-In returned empty identity token.");
                            tcs.TrySetResult(null);
                            return;
                        }

                        tcs.TrySetResult(Encoding.UTF8.GetString(tokenBytes, 0, tokenBytes.Length));
                    },
                    error =>
                    {
                        if (error != null
                            && error.GetAuthorizationErrorCode() == AuthorizationErrorCode.Canceled)
                        {
                            Debug.Log("[Auth] Apple Sign-In cancelled by user.");
                            tcs.TrySetCanceled();
                            return;
                        }

                        Debug.LogWarning($"[Auth] Apple Sign-In failed: {error?.LocalizedDescription ?? "unknown"}");
                        tcs.TrySetResult(null);
                    });

                while (tcs.UnsafeGetStatus() == UniTaskStatus.Pending)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    manager.Update();
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }

                return await tcs.Task;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Auth] Apple Sign-In exception: {ex.Message}");
            return null;
        }
        finally
        {
            Interlocked.Exchange(ref _requestInFlight, 0);
        }
#else
        Debug.LogWarning(
            "[Auth] Apple Sign-In identity token requires iOS device build + " +
            "com.lupidan.apple-signin-unity.");
        await UniTask.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        return null;
#endif
    }
}
