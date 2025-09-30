using System.ComponentModel.DataAnnotations;
using Refit;
using ShadowrunAi.Core.Models;

namespace ShadowrunAi.Core.Abstractions;

public interface IGeminiApi
{
    [Post("/v1beta/models/{model}:streamGenerateContent?alt=sse&key={apiKey}")]
    Task<IApiResponse<Stream>> StreamGenerateContentAsync(string model, string apiKey, [Body] GenerateContentRequest request, CancellationToken cancellationToken = default);

    [Post("/v1beta/models/{model}:generateContent?key={apiKey}")]
    Task<IApiResponse<GenerateContentResponse>> GenerateContentAsync(string model, string apiKey, [Body] GenerateContentRequest request, CancellationToken cancellationToken = default);

    [Post("/v1beta/caches?key={apiKey}")]
    Task<IApiResponse<CacheResponse>> CreateCacheAsync(string apiKey, [Body] CreateCacheRequest request, CancellationToken cancellationToken = default);

    [Patch("/v1beta/{cacheName}?key={apiKey}")]
    Task<IApiResponse<CacheResponse>> UpdateCacheAsync(string cacheName, string apiKey, [Body] UpdateCacheRequest request, CancellationToken cancellationToken = default);

    [Get("/v1beta/{cacheName}?key={apiKey}")]
    Task<IApiResponse<CacheResponse>> GetCacheAsync(string cacheName, string apiKey, CancellationToken cancellationToken = default);

    [Post("/upload/v1beta/files:upload?uploadType=multipart&key={apiKey}")]
    [Headers("Content-Type: multipart/related; boundary=BOUNDARY", "X-Goog-Upload-Protocol: multipart")]
    Task<IApiResponse<FileUploadResponse>> UploadFileAsync(string apiKey, [Body] HttpContent content, CancellationToken cancellationToken = default);

    [Get("/v1beta/files/{fileId}?key={apiKey}")]
    Task<IApiResponse<FileStatusResponse>> GetFileAsync(string fileId, string apiKey, CancellationToken cancellationToken = default);

    // Resumable upload: start session to get upload URL
    [Post("/upload/v1beta/files?uploadType=resumable&key={apiKey}")]
    [Headers("X-Goog-Upload-Protocol: resumable", "X-Goog-Upload-Command: start", "Content-Type: application/json")]
    Task<IApiResponse<object>> StartResumableUploadAsync(
        string apiKey,
        [Header("X-Goog-Upload-Header-Content-Length")] long contentLength,
        [Header("X-Goog-Upload-Header-Content-Type")] string contentType,
        [Body] object metadata,
        CancellationToken cancellationToken = default);
}

