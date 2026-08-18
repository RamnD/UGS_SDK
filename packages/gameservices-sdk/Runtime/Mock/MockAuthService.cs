using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Mock <see cref="IAuthService"/> implementation.
/// </summary>
public sealed class MockAuthService : IAuthService
{
    private const string MockPlayerId = "mock-player-000";

    private readonly NameValidatorConfig _validatorConfig;
    private readonly HashSet<string> _linkedTypeIds = new(StringComparer.Ordinal);
    private string _playerName = "";
    private string _openIdConnectIdProviderName;

    public MockAuthService(
        NameValidatorConfig config = null,
        GameServicesAuthProviderConfig providerConfig = null)
    {
        _validatorConfig = config ?? NameValidatorConfig.Empty;
        _openIdConnectIdProviderName = providerConfig?.OpenIdConnectIdProviderName;
    }

    /// <inheritdoc/>
    public bool IsSignedIn { get; private set; }

    /// <inheritdoc/>
    public string GetPlayerId() => IsSignedIn ? MockPlayerId : "unknown";

    /// <inheritdoc/>
    public string GetPlayerName() => _playerName;

    /// <inheritdoc/>
    public NameValidationError? ValidatePlayerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return NameValidationError.Empty;
        if (name.Length < 3)                 return NameValidationError.TooShort;
        if (name.Length > 50)                return NameValidationError.TooLong;
        foreach (char c in name)
            if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_' && c != '.')
                return NameValidationError.InvalidCharacter;
        string lower = name.ToLowerInvariant();
        foreach (var word in _validatorConfig.BannedWords)
            if (lower.Contains(word.ToLowerInvariant()))
                return NameValidationError.Profanity;
        if (_validatorConfig.BannedPattern != null &&
            _validatorConfig.BannedPattern.IsMatch(name))
            return NameValidationError.Profanity;
        return null;
    }

    /// <inheritdoc/>
    public Task<NameValidationError?> SetPlayerNameAsync(string name,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var error = ValidatePlayerName(name);
        if (error != null)
        {
            AppLog.Warn("MockAuth", $"Invalid name — {error}");
            return Task.FromResult<NameValidationError?>(error);
        }
        _playerName = name;
        AppLog.DebugLog("MockAuth", $"PlayerName → \"{_playerName}\"");
        return Task.FromResult<NameValidationError?>(null);
    }

    /// <inheritdoc/>
    public Task<bool> SignInAsync(AuthPlatform platform, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsSignedIn = true;
        AppLog.DebugLog("MockAuth", $"Signed in. Platform={platform}, ID={MockPlayerId}");
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<AccountLinkResult> LinkWithAccountAsync(AuthPlatform platform,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSignedIn)
            return Task.FromResult(AccountLinkResult.NotSignedIn);
        if (!AuthPlatformKind.IsLinkable(platform))
            return Task.FromResult(AccountLinkResult.Failed);

        string typeId = AuthPlatformKind.GetExternalIdTypeId(platform, _openIdConnectIdProviderName);
        if (!string.IsNullOrEmpty(typeId))
            _linkedTypeIds.Add(typeId);

        AppLog.DebugLog("MockAuth", $"LinkWithAccount ({platform}) — mock Linked.");
        return Task.FromResult(AccountLinkResult.Linked);
    }

    /// <inheritdoc/>
    public Task<bool> UnlinkWithAccountAsync(AuthPlatform platform, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSignedIn || !AuthPlatformKind.IsLinkable(platform))
            return Task.FromResult(false);

        string typeId = AuthPlatformKind.GetExternalIdTypeId(platform, _openIdConnectIdProviderName);
        if (!string.IsNullOrEmpty(typeId))
            _linkedTypeIds.Remove(typeId);

        AppLog.DebugLog("MockAuth", $"Unlink ({platform}) — mock.");
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public bool IsIdentityLinked(AuthPlatform platform)
    {
        if (!IsSignedIn)
            return false;
        string typeId = AuthPlatformKind.GetExternalIdTypeId(platform, _openIdConnectIdProviderName);
        return !string.IsNullOrEmpty(typeId) && _linkedTypeIds.Contains(typeId);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetLinkedIdentityTypeIds()
    {
        if (!IsSignedIn)
            return Array.Empty<string>();
        return new List<string>(_linkedTypeIds);
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsSignedIn = false;
        _playerName = "";
        _linkedTypeIds.Clear();
        AppLog.DebugLog("MockAuth", "DeleteAccount — mock.");
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public void Reset()
    {
        IsSignedIn = false;
        _playerName = "";
        _linkedTypeIds.Clear();
        AppLog.DebugLog("MockAuth", "Session reset.");
    }
}
