using Unity.Services.Analytics;

/// <summary>
/// UGS Analytics writes <c>unityPlayerID</c> only for standard events (gameStarted, etc.).
/// Custom events use a different buffer path — enrich them here with the authenticated player id.
/// Parameter name must not use the reserved <c>unity</c> prefix (Dashboard rejects it).
/// </summary>
internal static class AnalyticsCustomEventEnricher
{
    /// <summary>Custom parameter name (snake_case). Add to UGS event schemas for filtering.</summary>
    public const string UgsPlayerIdParam = "ugs_player_id";

    public static void ApplyUgsPlayerId(CustomEvent customEvent, string playerId)
    {
        if (customEvent == null || string.IsNullOrEmpty(playerId) || playerId == "unknown")
            return;

        customEvent.Add(UgsPlayerIdParam, playerId);
    }
}
