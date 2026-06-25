using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Authentication;

/// <summary>Внутренний шаг между успешным Sign-In и включением зависимых UGS-сервисов.</summary>
internal static class AuthenticationSdkReadiness
{
    /// <summary>
    /// Дожидается стабильно заполненного PlayerId в Unity Authentication после входа
    /// вместо фиксированного Task.Delay в один тик.
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
