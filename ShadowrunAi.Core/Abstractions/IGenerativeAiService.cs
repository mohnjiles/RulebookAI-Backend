using ShadowrunAi.Core.Models;

namespace ShadowrunAi.Core.Abstractions;

public interface IGenerativeAiService
{
    Task<ChatResponse> GenerateChatAsync(ChatSession session, string newMessage, CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> GenerateChatStreamAsync(ChatSession session, string newMessage, CancellationToken cancellationToken = default);

    Task<string?> GenerateTitleAsync(ChatSession session, string prompt, CancellationToken cancellationToken = default);

    Task<UploadResult> UploadPdfAsync(Guid sessionId, Stream pdfStream, string fileName, string? systemInstruction, CancellationToken cancellationToken = default);

    Task<CacheResult?> GetCacheAsync(string cacheId, CancellationToken cancellationToken = default);
}

