using System.Collections.Generic;
using NUnit.Framework;

public class UGSEnvironmentResolverTests
{
    [Test]
    public void SelectEnvironmentName_UsesTierWhenCustomNameIsAbsent()
    {
        Assert.That(
            UGSEnvironmentResolver.SelectEnvironmentName(UGSEnvironmentResolver.StagingName, null),
            Is.EqualTo("staging"));
        Assert.That(
            UGSEnvironmentResolver.SelectEnvironmentName(UGSEnvironmentResolver.ProductionName, ""),
            Is.EqualTo("production"));
    }

    [Test]
    public void SelectEnvironmentName_CustomNameReplacesTheTierDataset()
    {
        Assert.That(
            UGSEnvironmentResolver.SelectEnvironmentName("staging", "staging-custom"),
            Is.EqualTo("staging-custom"));
    }

    [Test]
    public void TrySelect_UsesTheEarliestDefinedRow()
    {
        var entries = new List<UgsEnvironmentNameEntry>
        {
            new UgsEnvironmentNameEntry { symbol = "UGS_ENV_NAME_A", environmentName = "staging-a" },
            new UgsEnvironmentNameEntry { symbol = "UGS_ENV_NAME_B", environmentName = "staging-b" },
        };

        bool selected = UgsEnvironmentNameMap.TrySelect(
            entries,
            new[] { "UGS_ENV_NAME_B", "UGS_ENV_NAME_A" },
            out string environmentName,
            out int matchCount);

        Assert.That(selected, Is.True);
        Assert.That(environmentName, Is.EqualTo("staging-a"));
        Assert.That(matchCount, Is.EqualTo(2));
    }

    [Test]
    public void TrySelect_SkipsBlankRowsAndUnknownSymbols()
    {
        var entries = new List<UgsEnvironmentNameEntry>
        {
            new UgsEnvironmentNameEntry { symbol = "UGS_ENV_NAME_BLANK", environmentName = "  " },
            new UgsEnvironmentNameEntry { symbol = "UGS_ENV_NAME_REAL", environmentName = "production-real" },
        };

        bool selected = UgsEnvironmentNameMap.TrySelect(
            entries,
            new[] { "UGS_ENV_NAME_OTHER" },
            out string environmentName,
            out int matchCount);

        Assert.That(selected, Is.False);
        Assert.That(environmentName, Is.Null);
        Assert.That(matchCount, Is.EqualTo(0));
    }

    [Test]
    public void EnvironmentNameMatchesTier_AcceptsTheTierOrAHyphenatedSuffix()
    {
        Assert.That(UGSEnvironmentResolver.EnvironmentNameMatchesTier("staging", "staging"), Is.True);
        Assert.That(UGSEnvironmentResolver.EnvironmentNameMatchesTier("staging-custom", "staging"), Is.True);
        Assert.That(UGSEnvironmentResolver.EnvironmentNameMatchesTier("production-custom", "production"), Is.True);
    }

    [Test]
    public void EnvironmentNameMatchesTier_RejectsADifferentTierOrAGluedSuffix()
    {
        Assert.That(UGSEnvironmentResolver.EnvironmentNameMatchesTier("staging-custom", "production"), Is.False);
        Assert.That(UGSEnvironmentResolver.EnvironmentNameMatchesTier("productionevil", "production"), Is.False);
        Assert.That(UGSEnvironmentResolver.EnvironmentNameMatchesTier(null, "staging"), Is.False);
    }
}
