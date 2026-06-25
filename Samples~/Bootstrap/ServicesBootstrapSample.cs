using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Minimal bootstrap sample. Import via Package Manager → Samples → Bootstrap.
/// Replace app key / auth credentials and wire economy / cloud save in OnAuthenticated.
/// </summary>
public sealed class ServicesBootstrapSample : MonoBehaviour
{
    [SerializeField] private bool _forceAnonymous = true;
    [SerializeField] private string _levelPlayAppKey = "YOUR_LEVELPLAY_APP_KEY";

    private async void Start()
    {
        await new UGSServicesBuilder()
            .WithForceAnonymous(_forceAnonymous)
            .WithAds(string.IsNullOrWhiteSpace(_levelPlayAppKey)
                ? new TestAdsManager()
                : new LevelPlayAdsManager(_levelPlayAppKey))
            .OnAuthenticated(async _ =>
            {
                // Example: var economy = new UGSEconomyService<MyCurrency>(new MyCurrencyMapper());
                // await economy.RefreshBalancesAsync();
                await Task.CompletedTask;
            })
            .BuildAsync(destroyCancellationToken);
    }
}
