using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Shared in-memory filtering for <see cref="PurchaseCatalogEntry"/> lists.
/// </summary>
public static class PurchaseCatalogFiltering
{
    /// <summary>Returns entries from <paramref name="entries"/> matching <paramref name="query"/>.</summary>
    public static IReadOnlyList<PurchaseCatalogEntry> Apply(
        IReadOnlyList<PurchaseCatalogEntry> entries,
        PurchaseCatalogQuery query)
    {
        if (entries == null || entries.Count == 0)
            return Array.Empty<PurchaseCatalogEntry>();

        return entries.Where(e => Matches(e, query)).ToList();
    }

    /// <summary>True when <paramref name="entry"/> satisfies all filters in <paramref name="query"/>.</summary>
    public static bool Matches(PurchaseCatalogEntry entry, PurchaseCatalogQuery query)
    {
        if (query.Kind.HasValue && entry.Kind != query.Kind.Value)
            return false;

        if (!string.IsNullOrEmpty(query.IdContains) &&
            entry.Id.IndexOf(query.IdContains, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        if (query.RewardResourceIds != null && query.RewardResourceIds.Count > 0 &&
            !MatchesResourceIds(entry.Rewards, query.RewardResourceIds, query.RewardMatch))
            return false;

        if (query.CostResourceIds != null && query.CostResourceIds.Count > 0 &&
            !MatchesResourceIds(entry.Costs, query.CostResourceIds, query.CostMatch))
            return false;

        return true;
    }

    static bool MatchesResourceIds(
        IReadOnlyList<PurchaseCatalogLine> lines,
        IReadOnlyList<string> resourceIds,
        PurchaseResourceMatchMode matchMode)
    {
        var required = new HashSet<string>(resourceIds.Where(id => !string.IsNullOrEmpty(id)),
            StringComparer.OrdinalIgnoreCase);
        if (required.Count == 0)
            return true;

        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (!string.IsNullOrEmpty(line.ResourceId) && required.Contains(line.ResourceId))
                present.Add(line.ResourceId);
        }

        return matchMode == PurchaseResourceMatchMode.All
            ? present.Count == required.Count
            : present.Count > 0;
    }
}
