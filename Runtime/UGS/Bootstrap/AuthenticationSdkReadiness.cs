using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Authentication;

/// <summary>Internal step between successful sign-in and enabling dependent UGS services.</summary>
internal static class AuthenticationSdkReadiness
{
    /// <summary>
    /// Waits for a stable PlayerId in Unity Authentication after sign-in
    /// instead of a fixed one-tick Task.Delay.
    /// </summary>
    public static async Task WaitForPlayerSessionStableAsync(CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(2);
        var deadline = DateTime.UtcNow.Add(limit);

        while (DateTime.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var auth = AuthenticationService.Instance;
            if (auth.IsSignedIn && !string.IsNullOrEmpty(auth.PlayerId))
            {
                await Task.Yield();
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(16), cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
