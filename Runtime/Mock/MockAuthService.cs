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
    private string _playerName = "";

    public MockAuthService(NameValidatorConfig config = null)
    {
        _validatorConfig = config ?? NameValidatorConfig.Empty;
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
            Debug.LogWarning($"[Mock Auth] Invalid name — {error}");
            return Task.FromResult<NameValidationError?>(error);
        }
        _playerName = name;
        Debug.Log($"[Mock Auth] PlayerName → \"{_playerName}\"");
        return Task.FromResult<NameValidationError?>(null);
    }

    /// <inheritdoc/>
    public Task<bool> SignInAsync(AuthPlatform platform, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsSignedIn = true;
        Debug.Log($"[Mock Auth] Signed in. Platform={platform}, ID={MockPlayerId}");
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<AccountLinkResult> LinkWithAccountAsync(AuthPlatform platform,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Debug.Log($"[Mock Auth] LinkWithAccount ({platform}) — mock Linked.");
        return Task.FromResult(AccountLinkResult.Linked);
    }

    /// <inheritdoc/>
    public void Reset()
    {
        IsSignedIn = false;
        _playerName = "";
        Debug.Log("[Mock Auth] Session reset.");
    }
}
