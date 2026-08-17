using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Optional store in-app update prompt. Android Play Immediate is the only native flow;
/// other platforms no-op until a game adds its own UI.
/// </summary>
public interface IAppUpdateService
{
    /// <summary>
    /// If the store reports an update, start the native flow. No-op when unavailable
    /// (Editor, sideload, iOS, missing Play plugin). Never throws to the game.
    /// </summary>
    Task PromptIfAvailableAsync(CancellationToken cancellationToken = default);
}
