using ShadowrunAi.Core.Models;

namespace ShadowrunAi.Core.Abstractions;

public interface IDataService
{
    Task<ChatSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task UpsertSessionAsync(ChatSession session, CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatSessionSummary>> ListSessionsAsync(string accountId, Guid? currentSessionId = null, CancellationToken cancellationToken = default);
}

